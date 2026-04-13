namespace Shirobot.Plugin.MyList.Webdav.Models;

internal sealed record WebDavItem(
    string Path,
    string Name,
    bool IsDirectory,
    long? GroupId,
    string? RemoteId,
    long ContentLength,
    string ContentType,
    DateTimeOffset LastModified)
{
    public static WebDavItem Root() =>
        new("/", "/", true, null, null, 0, "httpd/unix-directory", DateTimeOffset.UtcNow);

    public static WebDavItem Directory(
        string path,
        string name,
        long? groupId,
        string remoteId,
        DateTimeOffset? lastModified = null) =>
        new(path, name, true, groupId, remoteId, 0, "httpd/unix-directory", lastModified ?? DateTimeOffset.UtcNow);

    public static WebDavItem File(
        string path,
        string name,
        long groupId,
        string remoteId,
        long contentLength,
        string contentType,
        DateTimeOffset lastModified) =>
        new(path, name, false, groupId, remoteId, contentLength, contentType, lastModified);
}

internal sealed record WebDavResolution(WebDavItem? Item, IReadOnlyList<WebDavItem> Children)
{
    public bool Exists => Item is not null;

    public static WebDavResolution NotFound { get; } = new(null, []);
}

internal sealed record WebDavFileContent(string DownloadUrl, string ContentType);

internal sealed record WebDavWriteResult(bool Succeeded, bool Created, bool AlreadyExists, bool NotFound, bool Conflict, string? ErrorMessage)
{
    public static WebDavWriteResult Success(bool created) => new(true, created, false, false, false, null);
    public static WebDavWriteResult Exists() => new(false, false, true, false, false, null);
    public static WebDavWriteResult CreateNotFound() => new(false, false, false, true, false, null);
    public static WebDavWriteResult CreateConflict(string message) => new(false, false, false, false, true, message);
    public static WebDavWriteResult Invalid(string message) => new(false, false, false, false, true, message);
}

internal sealed record WebDavMoveResult(bool Succeeded, bool Created, bool AlreadyExists, bool NotFound, bool Conflict, bool Forbidden, string? ErrorMessage)
{
    public static WebDavMoveResult Success(bool created) => new(true, created, false, false, false, false, null);
    public static WebDavMoveResult Exists() => new(false, false, true, false, false, false, null);
    public static WebDavMoveResult CreateNotFound() => new(false, false, false, true, false, false, null);
    public static WebDavMoveResult CreateConflict(string message) => new(false, false, false, false, true, false, message);
    public static WebDavMoveResult ForbiddenOp(string message) => new(false, false, false, false, false, true, message);
}
