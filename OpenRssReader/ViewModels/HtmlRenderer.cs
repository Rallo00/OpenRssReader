using System.Text.RegularExpressions;

namespace OpenRssReader.ViewModels;

public static class HtmlRenderer
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withoutTags = Regex.Replace(html, "<.*?>", " ");
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    public static string CreateDocument(string title, string htmlBody, string fallbackSummary)
    {
        var body = string.IsNullOrWhiteSpace(htmlBody)
            ? $"<p>{System.Net.WebUtility.HtmlEncode(fallbackSummary)}</p>"
            : htmlBody;

        body = RemoveUnsupportedEmbeds(body);

        return """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <style>
    body {
      background-color: #F6F6F4;
      font-family: Segoe UI, Arial, sans-serif;
      color: #403B36;
      margin: 0;
      padding: 0 28px 26px 0;
      background: #f6f6f4;
      line-height: 1.7;
      font-size: 18px;
      width: auto;
      overflow-x: hidden;
      overflow-wrap: anywhere;
    }

    html {
      width: 100%;
      overflow-x: hidden;
      background: #f6f6f4;
    }
    *, *::before, *::after {
      box-sizing: border-box;
      max-width: 100%;
    }
    h1, h2, h3 {
      color: #403B36;
      line-height: 1.18;
    }
    a {
      color: #403B36;
      font-weight: 700;
      text-decoration: none;
    }
    img, video, table, figure, picture {
      display: block;
      width: 100% !important;
      max-width: 100% !important;
      height: auto !important;
      border-radius: 18px;
      margin: 18px 0;
    }
    figure, picture, div, p {
      max-width: 100% !important;
      overflow-x: hidden;
    }
    blockquote {
      border-left: 4px solid #E8E0D5;
      margin: 22px 0;
      padding: 4px 0 4px 18px;
      color: #7E766E;
    }
  </style>
</head>
<body>
  __BODY__
</body>
</html>
""".Replace("__BODY__", body, StringComparison.Ordinal);
    }

    public static string ApplyReadingTypography(string html, string fontFamily, int fontSize, bool isDark)
    {
        var safeFontFamily = fontFamily.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
        var background = isDark ? "#292929" : "#F7F6F4";
        var articleText = isDark ? "#BDBDBD" : "#403B36";
        var title = isDark ? "#FFFFFF" : "#403B36";
        var muted = isDark ? "#BDBDBD" : "#7E766E";
        var typography = $"<style>html, body {{ background-color: {background} !important; color: {articleText} !important; }} body {{ font-family: '{safeFontFamily}', Segoe UI, Arial, sans-serif !important; font-size: {fontSize}px !important; }} p, li, blockquote, a {{ color: {articleText} !important; }} h1, h2, h3 {{ color: {title} !important; }} blockquote {{ border-color: {muted} !important; }}</style>";
        return html.Replace("</head>", $"{typography}</head>", StringComparison.OrdinalIgnoreCase);
    }

    public static string RemoveUnsupportedEmbeds(string html)
    {
        // The WPF WebBrowser uses the legacy IE engine. Embedded scripts and frames can
        // show modal runtime errors, so render article text and media only.
        var sanitized = Regex.Replace(html, @"<script\b[^>]*>.*?</script\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        sanitized = Regex.Replace(sanitized, @"<iframe\b[^>]*>.*?</iframe\s*>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return Regex.Replace(sanitized, @"<iframe\b[^>]*/?>", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    public static string ExtractImageUrl(string html)
    {
        var match = Regex.Match(html, "<img\\b[^>]*?\\bsrc\\s*=\\s*['\\\"](?<url>[^'\\\"]+)['\\\"]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["url"].Value : string.Empty;
    }
}
