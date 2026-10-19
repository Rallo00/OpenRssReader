using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace OpenRssReader.Services;

public sealed class GitHubUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Rallo00/OpenRssReader/releases/latest";
    private static readonly HttpClient Client = CreateClient();

    public async Task<GitHubRelease?> CheckForUpdateAsync()
    {
        using var response = await Client.GetAsync(LatestReleaseUrl);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagName) ? tagName.GetString() : null;
        if (!TryParseVersion(tag, out var releaseVersion) || releaseVersion <= GetCurrentVersion())
        {
            return null;
        }

        var asset = root.TryGetProperty("assets", out var assets)
            ? assets.EnumerateArray().FirstOrDefault(item =>
            {
                var name = item.TryGetProperty("name", out var assetName) ? assetName.GetString() : null;
                return !string.IsNullOrWhiteSpace(name) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            })
            : default;
        var downloadUrl = asset.ValueKind == JsonValueKind.Object && asset.TryGetProperty("browser_download_url", out var url)
            ? url.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return null;
        }

        var body = root.TryGetProperty("body", out var releaseBody) ? releaseBody.GetString() : string.Empty;
        return new GitHubRelease(tag!.Trim(), releaseVersion, downloadUrl, body?.Trim() ?? string.Empty);
    }

    public async Task<string> DownloadAsync(GitHubRelease release)
    {
        var directory = Path.Combine(Path.GetTempPath(), "OpenRssReader", "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var archivePath = Path.Combine(directory, "OpenRssReader-update.zip");
        using var response = await Client.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        await source.CopyToAsync(destination);
        return archivePath;
    }

    public void StartInstaller(string archivePath, string backupPath)
    {
        var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var updaterDirectory = Path.GetDirectoryName(archivePath) ?? throw new InvalidOperationException("Update working directory is unavailable.");
        var scriptPath = Path.Combine(updaterDirectory, "install-update.ps1");
        File.WriteAllText(scriptPath, InstallerScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-ArchivePath");
        startInfo.ArgumentList.Add(archivePath);
        startInfo.ArgumentList.Add("-TargetDirectory");
        startInfo.ArgumentList.Add(targetDirectory);
        startInfo.ArgumentList.Add("-BackupPath");
        startInfo.ArgumentList.Add(backupPath);
        Process.Start(startInfo);
    }

    public static string CurrentVersionLabel => $"v{GetCurrentVersion().ToString(2)}";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("OpenRssReader-Updater/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version GetCurrentVersion() => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0);

    private static bool TryParseVersion(string? value, out Version version)
    {
        return Version.TryParse(value?.Trim().TrimStart('v', 'V'), out version!);
    }

    private const string InstallerScript = """
param(
    [int] $ProcessId,
    [string] $ArchivePath,
    [string] $TargetDirectory,
    [string] $BackupPath
)

$ErrorActionPreference = 'Stop'
$workingDirectory = Split-Path -Parent $ArchivePath
$extractionDirectory = Join-Path $workingDirectory 'extracted'

try {
    Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $extractionDirectory -Force
    $children = @(Get-ChildItem -LiteralPath $extractionDirectory -Force)
    $sourceDirectory = $extractionDirectory
    if ($children.Count -eq 1 -and $children[0].PSIsContainer) {
        $sourceDirectory = $children[0].FullName
    }

    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory 'OpenRssReader.exe'))) {
        throw 'The downloaded archive does not contain OpenRssReader.exe.'
    }

    Get-ChildItem -LiteralPath $TargetDirectory -Force | Remove-Item -Force -Recurse
    Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $TargetDirectory -Force -Recurse
    Start-Process -FilePath (Join-Path $TargetDirectory 'OpenRssReader.exe') -ArgumentList '--restore-backup', $BackupPath
}
finally {
    Remove-Item -LiteralPath $workingDirectory -Force -Recurse -ErrorAction SilentlyContinue
}
""";
}

public sealed record GitHubRelease(string TagName, Version Version, string DownloadUrl, string Changelog);
