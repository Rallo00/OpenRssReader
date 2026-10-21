using System.IO;
using System.Text.Json;

namespace OpenRssReader.Services;

public sealed class StorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new();
    private static readonly JsonSerializerOptions BackupJsonOptions = new() { WriteIndented = true };
    private readonly string _storagePath;
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    public StorageService()
    {
        var baseDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenRssReader");
        Directory.CreateDirectory(baseDirectory);
        _storagePath = Path.Combine(baseDirectory, "state.json");
    }

    public Task<AppState?> LoadAsync() => Task.Run(LoadStateFromDiskAsync);

    private async Task<AppState?> LoadStateFromDiskAsync()
    {
        if (!File.Exists(_storagePath))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(_storagePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // Keep the damaged file for inspection and start from a clean state.
            var backupPath = Path.Combine(
                Path.GetDirectoryName(_storagePath)!,
                $"state.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.json");
            File.Move(_storagePath, backupPath, overwrite: true);
            return null;
        }
    }

    public async Task SaveAsync(AppState state)
    {
        await _saveGate.WaitAsync().ConfigureAwait(false);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(_storagePath)!,
            $"state.{Guid.NewGuid():N}.tmp");

        try
        {
            // Acquire the gate before scheduling work so queued snapshots keep
            // their save order, without serializing large documents on the UI.
            await Task.Run(async () =>
            {
                await using var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous);
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    File.Move(temporaryPath, _storagePath, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(100 * (attempt + 1)).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _saveGate.Release();
        }
    }

    public async Task SaveBackupAsync(string path, AppState state)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, state, BackupJsonOptions);
        await stream.FlushAsync();
    }

    public async Task<AppState> LoadBackupAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var state = await JsonSerializer.DeserializeAsync<AppState>(stream, JsonOptions);
        return state ?? throw new InvalidOperationException("The selected backup file is empty or invalid.");
    }
}
