using System.Text.RegularExpressions;

namespace Shirobot.Plugin.MyList.Webdav.Infrastructure;

internal static class WebDavPathHelper
{
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "/";
        }

        var normalized = "/" + path.Trim().Trim('/').Replace('\\', '/');
        return normalized == "//" ? "/" : normalized;
    }

    public static List<string> SplitSegments(string path) =>
        path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();

    public static string GetGroupRootPath(string path)
    {
        var segments = SplitSegments(path);
        return segments.Count == 0 ? "/" : "/" + segments[0];
    }

    public static string GetLastSegment(string path) =>
        path.TrimEnd('/').Split('/').Last();

    public static string GetParentPath(string path)
    {
        var normalized = NormalizePath(path);
        var segments = SplitSegments(normalized);
        if (segments.Count <= 1)
        {
            return "/";
        }

        return "/" + string.Join('/', segments.Take(segments.Count - 1));
    }

    public static string SanitizeSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "_";
        }

        var invalidChars = Path.GetInvalidFileNameChars().Concat(['/']).ToHashSet();
        var chars = value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars).Trim();
    }

    public static string GuessContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".txt" => "text/plain; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".mp4" => "video/mp4",
            ".mp3" => "audio/mpeg",
            ".pdf" => "application/pdf",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }

    public static string EnsureFileNameInDownloadUrl(string downloadUrl, string fileName)
    {
        if (string.IsNullOrWhiteSpace(downloadUrl) || string.IsNullOrWhiteSpace(fileName))
        {
            return downloadUrl;
        }

        var encodedFileName = Uri.EscapeDataString(fileName);
        if (Regex.IsMatch(downloadUrl, @"([?&])fname=", RegexOptions.IgnoreCase))
        {
            return Regex.Replace(
                downloadUrl,
                @"([?&])fname=[^&]*",
                $"$1fname={encodedFileName}",
                RegexOptions.IgnoreCase);
        }

        var separator = downloadUrl.Contains('?') ? "&" : "?";
        return downloadUrl + separator + "fname=" + encodedFileName;
    }
}
