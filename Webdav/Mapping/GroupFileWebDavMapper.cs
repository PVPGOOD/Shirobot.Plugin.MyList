using ShiroBot.Model.QQ;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyList.Webdav.Diagnostics;
using Shirobot.Plugin.MyList.Webdav.Infrastructure;
using Shirobot.Plugin.MyList.Webdav.Models;

namespace Shirobot.Plugin.MyList.Webdav.Mapping;

internal sealed class GroupFileWebDavMapper
{
    private readonly IBotContext _context;
    private readonly MyListConfig _config;
    private readonly WebDavLog _log;
    private readonly long _base64ThresholdBytes;

    public GroupFileWebDavMapper(IBotContext context, MyListConfig config)
    {
        _context = context;
        _config = config;
        _log = new WebDavLog(config);
        _base64ThresholdBytes = Math.Max(1, _config.UploadBase64ThresholdMb) * 1024L * 1024L;
    }

    public async Task<IReadOnlyList<WebDavItem>> GetRootItemsAsync()
    {
        var groups = await GetGroupsAsync();
        return groups
            .OrderBy(group => group.GroupId)
            .Select(group => WebDavItem.Directory(
                "/" + BuildGroupSegment(group),
                group.GroupName,
                group.GroupId,
                "/"))
            .ToList();
    }

    public async Task<WebDavResolution> ResolveAsync(string path, bool includeChildren)
    {
        var normalizedPath = WebDavPathHelper.NormalizePath(path);
        if (normalizedPath == "/")
        {
            var rootItems = await GetRootItemsAsync();
            return new WebDavResolution(WebDavItem.Root(), includeChildren ? rootItems : []);
        }

        var segments = WebDavPathHelper.SplitSegments(normalizedPath);
        if (segments.Count == 0)
        {
            return new WebDavResolution(WebDavItem.Root(), []);
        }

        var groups = await GetGroupsAsync();
        var group = groups.FirstOrDefault(item => string.Equals(BuildGroupSegment(item), segments[0], StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return WebDavResolution.NotFound;
        }

        var groupRootPath = "/" + segments[0];

        if (segments.Count == 1)
        {
            var children = await GetFolderChildrenAsync(group.GroupId, groupRootPath, "/");
            return new WebDavResolution(
                WebDavItem.Directory(groupRootPath, group.GroupName, group.GroupId, "/"),
                includeChildren ? children : []);
        }

        var currentFolderId = "/";
        for (var i = 1; i < segments.Count; i++)
        {
            var children = await GetFolderChildrenAsync(group.GroupId, groupRootPath, currentFolderId);
            var isLastSegment = i == segments.Count - 1;
            var currentSegment = segments[i];

            var folder = children
                .Where(item => item.IsDirectory)
                .FirstOrDefault(item => string.Equals(WebDavPathHelper.GetLastSegment(item.Path), currentSegment, StringComparison.OrdinalIgnoreCase));

            if (folder is not null)
            {
                currentFolderId = folder.RemoteId ?? "/";

                if (isLastSegment)
                {
                    var subChildren = includeChildren ? await GetFolderChildrenAsync(group.GroupId, groupRootPath, currentFolderId) : [];
                    return new WebDavResolution(folder, subChildren);
                }

                continue;
            }

            var file = children
                .Where(item => !item.IsDirectory)
                .FirstOrDefault(item => string.Equals(WebDavPathHelper.GetLastSegment(item.Path), currentSegment, StringComparison.OrdinalIgnoreCase));

            if (file is not null && isLastSegment)
            {
                return new WebDavResolution(file, []);
            }

            return WebDavResolution.NotFound;
        }

        return WebDavResolution.NotFound;
    }

    public async Task<WebDavFileContent?> GetFileContentAsync(string path, CancellationToken cancellationToken)
    {
        var resolution = await ResolveAsync(path, includeChildren: false);
        if (!resolution.Exists || resolution.Item is null || resolution.Item.IsDirectory || resolution.Item.GroupId is null || string.IsNullOrWhiteSpace(resolution.Item.RemoteId))
        {
            return null;
        }

        var downloadUrl = await GetFileApi().GetGroupFileDownloadUrlAsync(resolution.Item.GroupId.Value, resolution.Item.RemoteId);
        var finalUrl = WebDavPathHelper.EnsureFileNameInDownloadUrl(downloadUrl, resolution.Item.Name);
        return new WebDavFileContent(finalUrl, resolution.Item.ContentType);
    }

    public async Task<WebDavWriteResult> PutFileAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var normalizedPath = WebDavPathHelper.NormalizePath(path);
        var segments = WebDavPathHelper.SplitSegments(normalizedPath);
        _log.Trace($"WebDAV Mapper 开始上传: rawPath={path}, normalizedPath={normalizedPath}, segmentCount={segments.Count}");
        if (segments.Count < 2)
        {
            _log.Trace($"WebDAV Mapper 上传拒绝: path={normalizedPath}, reason=invalid-target");
            return WebDavWriteResult.Invalid("必须上传到群目录下的文件路径。");
        }

        var fileName = segments[^1];
        var parentPath = "/" + string.Join('/', segments.Take(segments.Count - 1));
        _log.Trace($"WebDAV Mapper 上传解析: path={normalizedPath}, parentPath={parentPath}, fileName={fileName}");
        var parentResolution = await ResolveAsync(parentPath, includeChildren: false);
        if (!parentResolution.Exists || parentResolution.Item is null || !parentResolution.Item.IsDirectory || parentResolution.Item.GroupId is null)
        {
            _log.Trace(
                $"WebDAV Mapper 上传冲突: path={normalizedPath}, parentExists={parentResolution.Exists}, parentNull={parentResolution.Item is null}, parentIsDirectory={parentResolution.Item?.IsDirectory}, groupId={parentResolution.Item?.GroupId}");
            return WebDavWriteResult.CreateConflict("目标父目录不存在。");
        }

        var existing = await ResolveAsync(normalizedPath, includeChildren: false);
        if (existing is { Exists: true, Item.IsDirectory: true })
        {
            _log.Trace($"WebDAV Mapper 上传冲突: path={normalizedPath}, reason=target-is-directory, remoteId={existing.Item.RemoteId}");
            return WebDavWriteResult.CreateConflict("不能用文件覆盖目录。");
        }

        var tempFilePath = BuildTempFilePath(fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempFilePath)!);
        await using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await content.CopyToAsync(fileStream, cancellationToken);
        }

        var fileInfo = new FileInfo(tempFilePath);
        string fileUri;
        string transport;
        var uploadMode = _config.GetNormalizedUploadMode();
        var useBase64 = uploadMode switch
        {
            MyListConfig.UploadModeBase64 => true,
            MyListConfig.UploadModeFile => false,
            _ => fileInfo.Length <= _base64ThresholdBytes
        };

        if (useBase64)
        {
            await using var buffer = new MemoryStream();
            await using (var readStream = new FileStream(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await readStream.CopyToAsync(buffer, cancellationToken);
            }

            var payload = Convert.ToBase64String(buffer.ToArray());
            fileUri = "base64://" + payload;
            transport = "base64";
            _log.Trace($"WebDAV Mapper 上传编码完成: path={normalizedPath}, mode={uploadMode}, bytes={fileInfo.Length}, base64Length={payload.Length}");
        }
        else
        {
            var fileTransferMode = _config.GetNormalizedFileTransferMode();
            if (fileTransferMode == MyListConfig.FileTransferSmb)
            {
                using var smbScope = SmbConnectionScope.ConnectIfNeeded(_config);
                var smbFilePath = CopyToSmbUpload(fileName, tempFilePath);
                fileUri = BuildSmbMilkyFileUri(smbFilePath);
                transport = "file+smb";
                _log.Trace($"WebDAV Mapper 上传切换到 SMB 文件: path={normalizedPath}, mode={uploadMode}, fileTransferMode={fileTransferMode}, bytes={fileInfo.Length}, smbPath={smbFilePath}, fileUri={fileUri}");
            }
            else
            {
                fileUri = BuildLocalFileUri(tempFilePath);
                transport = "file+direct";
                _log.Trace($"WebDAV Mapper 上传切换到本地文件: path={normalizedPath}, mode={uploadMode}, fileTransferMode={fileTransferMode}, bytes={fileInfo.Length}, localPath={tempFilePath}, fileUri={fileUri}");
            }
        }

        _log.Trace(
            $"WebDAV Mapper 上传命中: path={normalizedPath}, groupId={parentResolution.Item.GroupId.Value}, parentFolderId={parentResolution.Item.RemoteId ?? "/"}, transport={transport}");

        if (existing is { Exists: true, Item.IsDirectory: false } && !string.IsNullOrWhiteSpace(existing.Item.RemoteId))
        {
            _log.Trace($"WebDAV Mapper 上传覆盖: groupId={parentResolution.Item.GroupId.Value}, oldFileId={existing.Item.RemoteId}");
            await GetFileApi().DeleteGroupFileAsync(parentResolution.Item.GroupId.Value, existing.Item.RemoteId);
        }

        try
        {
            _log.Trace($"WebDAV Mapper 调用 UploadGroupFileAsync: groupId={parentResolution.Item.GroupId.Value}, fileName={fileName}, parentFolderId={parentResolution.Item.RemoteId ?? "/"}, scheme={transport}");
            await GetFileApi().UploadGroupFileAsync(
                parentResolution.Item.GroupId.Value,
                fileUri,
                fileName,
                parentResolution.Item.RemoteId ?? "/");

            WebDavLog.Info($"WebDAV Mapper 上传完成: path={normalizedPath}, created={!existing.Exists}");

            return existing.Exists
                ? WebDavWriteResult.Success(created: false)
                : WebDavWriteResult.Success(created: true);
        }
        finally
        {
            TryDeleteFile(tempFilePath);
        }
    }

    public async Task<WebDavWriteResult> CreateFolderAsync(string path)
    {
        var normalizedPath = WebDavPathHelper.NormalizePath(path);
        var segments = WebDavPathHelper.SplitSegments(normalizedPath);
        _log.Trace($"WebDAV Mapper 开始创建目录: rawPath={path}, normalizedPath={normalizedPath}, segmentCount={segments.Count}");
        if (segments.Count == 0)
        {
            _log.Trace("WebDAV Mapper MKCOL 幂等成功: 目标为根目录。");
            return WebDavWriteResult.Exists();
        }

        if (segments.Count == 1)
        {
            _log.Trace($"WebDAV Mapper MKCOL 幂等成功: 目标为群根目录。 path={normalizedPath}");
            return WebDavWriteResult.Exists();
        }

        if (segments.Count < 2)
        {
            return WebDavWriteResult.Invalid("必须在群目录下创建文件夹。");
        }

        var folderName = segments[^1];
        var parentPath = "/" + string.Join('/', segments.Take(segments.Count - 1));
        _log.Trace($"WebDAV Mapper 创建目录解析: path={normalizedPath}, parentPath={parentPath}, folderName={folderName}");
        var parentResolution = await ResolveAsync(parentPath, includeChildren: false);
        if (!parentResolution.Exists || parentResolution.Item is null || !parentResolution.Item.IsDirectory || parentResolution.Item.GroupId is null)
        {
            _log.Trace(
                $"WebDAV Mapper 创建目录冲突: path={normalizedPath}, parentExists={parentResolution.Exists}, parentNull={parentResolution.Item is null}, parentIsDirectory={parentResolution.Item?.IsDirectory}, groupId={parentResolution.Item?.GroupId}");
            return WebDavWriteResult.CreateConflict("目标父目录不存在。");
        }

        var existing = await ResolveAsync(normalizedPath, includeChildren: false);
        if (existing.Exists)
        {
            _log.Trace($"WebDAV Mapper MKCOL 幂等成功: path={normalizedPath}, existingRemoteId={existing.Item?.RemoteId}, isDirectory={existing.Item?.IsDirectory}");
            return WebDavWriteResult.Exists();
        }

        _log.Trace($"WebDAV Mapper 调用 CreateGroupFolderAsync: groupId={parentResolution.Item.GroupId.Value}, folderName={folderName}");
        await GetFileApi().CreateGroupFolderAsync(parentResolution.Item.GroupId.Value, folderName);
        WebDavLog.Info($"WebDAV Mapper 创建目录完成: path={normalizedPath}, groupId={parentResolution.Item.GroupId.Value}, folderName={folderName}");
        return WebDavWriteResult.Success(created: true);
    }

    public async Task<WebDavWriteResult> DeleteAsync(string path)
    {
        var normalizedPath = WebDavPathHelper.NormalizePath(path);
        _log.Trace($"WebDAV Mapper 开始删除: rawPath={path}, normalizedPath={normalizedPath}");
        if (normalizedPath == "/")
        {
            _log.Trace("WebDAV Mapper 删除拒绝: 目标为根目录。");
            return WebDavWriteResult.Invalid("不能删除根目录。");
        }

        var resolution = await ResolveAsync(normalizedPath, includeChildren: false);
        if (!resolution.Exists || resolution.Item is null || resolution.Item.GroupId is null || string.IsNullOrWhiteSpace(resolution.Item.RemoteId))
        {
            _log.Trace($"WebDAV Mapper 删除未命中: path={normalizedPath}, exists={resolution.Exists}, itemNull={resolution.Item is null}");
            return WebDavWriteResult.CreateNotFound();
        }

        _log.Trace(
            $"WebDAV Mapper 删除命中: path={normalizedPath}, name={resolution.Item.Name}, isDirectory={resolution.Item.IsDirectory}, groupId={resolution.Item.GroupId}, remoteId={resolution.Item.RemoteId}");

        if (resolution.Item.IsDirectory)
        {
            if (resolution.Item.RemoteId == "/")
            {
                _log.Trace($"WebDAV Mapper 删除拒绝: path={normalizedPath}, reason=group-root");
                return WebDavWriteResult.Invalid("不能删除群根目录。");
            }

            _log.Trace($"WebDAV Mapper 调用 DeleteGroupFolderAsync: groupId={resolution.Item.GroupId.Value}, folderId={resolution.Item.RemoteId}");
            await GetFileApi().DeleteGroupFolderAsync(resolution.Item.GroupId.Value, resolution.Item.RemoteId);
            WebDavLog.Info($"WebDAV Mapper 删除文件夹完成: groupId={resolution.Item.GroupId.Value}, folderId={resolution.Item.RemoteId}");
            return WebDavWriteResult.Success(created: false);
        }

        _log.Trace($"WebDAV Mapper 调用 DeleteGroupFileAsync: groupId={resolution.Item.GroupId.Value}, fileId={resolution.Item.RemoteId}");
        await GetFileApi().DeleteGroupFileAsync(resolution.Item.GroupId.Value, resolution.Item.RemoteId);
        WebDavLog.Info($"WebDAV Mapper 删除文件完成: groupId={resolution.Item.GroupId.Value}, fileId={resolution.Item.RemoteId}");
        return WebDavWriteResult.Success(created: false);
    }

    public async Task<WebDavMoveResult> MoveAsync(string sourcePath, string destinationPath, bool overwrite)
    {
        var normalizedSourcePath = WebDavPathHelper.NormalizePath(sourcePath);
        var normalizedDestinationPath = WebDavPathHelper.NormalizePath(destinationPath);
        _log.Trace($"WebDAV Mapper 开始移动: source={normalizedSourcePath}, destination={normalizedDestinationPath}, overwrite={overwrite}");

        var source = await ResolveAsync(normalizedSourcePath, includeChildren: false);
        if (!source.Exists || source.Item is null || source.Item.GroupId is null || string.IsNullOrWhiteSpace(source.Item.RemoteId))
        {
            return WebDavMoveResult.CreateNotFound();
        }

        if (normalizedSourcePath == "/" || normalizedDestinationPath == "/")
        {
            return WebDavMoveResult.ForbiddenOp("不能移动根目录。");
        }

        var destinationParentPath = WebDavPathHelper.GetParentPath(normalizedDestinationPath);
        var destinationName = WebDavPathHelper.GetLastSegment(normalizedDestinationPath);
        var destinationParent = await ResolveAsync(destinationParentPath, includeChildren: false);
        if (!destinationParent.Exists || destinationParent.Item is null || !destinationParent.Item.IsDirectory || destinationParent.Item.GroupId is null)
        {
            return WebDavMoveResult.CreateConflict("目标父目录不存在。");
        }

        if (source.Item.GroupId != destinationParent.Item.GroupId)
        {
            return WebDavMoveResult.ForbiddenOp("当前仅支持在同一个群内移动。");
        }

        var destination = await ResolveAsync(normalizedDestinationPath, includeChildren: false);
        if (destination is { Exists: true, Item: not null })
        {
            if (!overwrite)
            {
                return WebDavMoveResult.Exists();
            }

            if (destination.Item.IsDirectory)
            {
                return WebDavMoveResult.CreateConflict("不能覆盖目标目录。");
            }

            await GetFileApi().DeleteGroupFileAsync(destination.Item.GroupId!.Value, destination.Item.RemoteId!);
        }

        var sourceParentPath = WebDavPathHelper.GetParentPath(normalizedSourcePath);
        var sourceParent = await ResolveAsync(sourceParentPath, includeChildren: false);
        var sourceParentFolderId = sourceParent.Item?.RemoteId ?? "/";
        var destinationParentFolderId = destinationParent.Item.RemoteId ?? "/";

        if (source.Item.IsDirectory)
        {
            if (!string.Equals(sourceParentPath, destinationParentPath, StringComparison.OrdinalIgnoreCase))
            {
                return WebDavMoveResult.ForbiddenOp("当前仅支持文件夹重命名，不支持跨目录移动文件夹。");
            }

            if (string.Equals(source.Item.Name, destinationName, StringComparison.Ordinal))
            {
                return WebDavMoveResult.Success(created: false);
            }

            _log.Trace($"WebDAV Mapper 调用 RenameGroupFolderAsync: groupId={source.Item.GroupId.Value}, folderId={source.Item.RemoteId}, newName={destinationName}");
            await GetFileApi().RenameGroupFolderAsync(source.Item.GroupId.Value, source.Item.RemoteId, destinationName);
            WebDavLog.Info($"WebDAV Mapper 文件夹移动完成: source={normalizedSourcePath}, destination={normalizedDestinationPath}");
            return WebDavMoveResult.Success(created: !destination.Exists);
        }

        if (!string.Equals(sourceParentFolderId, destinationParentFolderId, StringComparison.OrdinalIgnoreCase))
        {
            _log.Trace($"WebDAV Mapper 调用 MoveGroupFileAsync: groupId={source.Item.GroupId.Value}, fileId={source.Item.RemoteId}, sourceParentFolderId={sourceParentFolderId}, destinationParentFolderId={destinationParentFolderId}");
            await GetFileApi().MoveGroupFileAsync(source.Item.GroupId.Value, source.Item.RemoteId, destinationParentFolderId, sourceParentFolderId);
        }

        if (!string.Equals(source.Item.Name, destinationName, StringComparison.Ordinal))
        {
            _log.Trace($"WebDAV Mapper 调用 RenameGroupFileAsync: groupId={source.Item.GroupId.Value}, fileId={source.Item.RemoteId}, newName={destinationName}, parentFolderId={destinationParentFolderId}");
            await GetFileApi().RenameGroupFileAsync(source.Item.GroupId.Value, source.Item.RemoteId, destinationName, destinationParentFolderId);
        }

        WebDavLog.Info($"WebDAV Mapper 文件移动完成: source={normalizedSourcePath}, destination={normalizedDestinationPath}");
        return WebDavMoveResult.Success(created: !destination.Exists);
    }

    public async Task<IReadOnlyList<WebDavItem>> EnumerateSubtreeAsync(string path)
    {
        var resolution = await ResolveAsync(path, includeChildren: true);
        if (!resolution.Exists || resolution.Item is null)
        {
            return [];
        }

        if (!resolution.Item.IsDirectory)
        {
            return [resolution.Item];
        }

        var items = new List<WebDavItem> { resolution.Item };
        await AppendDescendantsAsync(resolution.Item, items);
        return items;
    }

    private async Task<IReadOnlyList<WebDavItem>> GetFolderChildrenAsync(long groupId, string groupRootPath, string folderId)
    {
        var result = await GetFileApi().GetGroupFilesAsync(groupId, folderId);
        var prefix = await BuildFolderPathPrefixAsync(groupRootPath, groupId, folderId);

        var folders = result.Folders
            .OrderBy(folder => folder.FolderName, StringComparer.OrdinalIgnoreCase)
            .Select(folder => WebDavItem.Directory(
                prefix + "/" + WebDavPathHelper.SanitizeSegment(folder.FolderName),
                folder.FolderName,
                groupId,
                folder.FolderId,
                folder.LastModifiedTime))
            .ToList();

        var files = result.Files
            .OrderBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(file => WebDavItem.File(
                prefix + "/" + WebDavPathHelper.SanitizeSegment(file.FileName),
                file.FileName,
                groupId,
                file.FileId,
                file.FileSize,
                WebDavPathHelper.GuessContentType(file.FileName),
                file.UploadedTime ?? DateTimeOffset.UtcNow))
            .ToList();

        return folders.Concat(files).ToList();
    }

    private async Task AppendDescendantsAsync(WebDavItem directory, List<WebDavItem> items)
    {
        if (!directory.IsDirectory || directory.GroupId is null || string.IsNullOrWhiteSpace(directory.RemoteId))
        {
            return;
        }

        var groupRootPath = WebDavPathHelper.GetGroupRootPath(directory.Path);
        var children = await GetFolderChildrenAsync(directory.GroupId.Value, groupRootPath, directory.RemoteId);
        foreach (var child in children)
        {
            items.Add(child);
            if (child.IsDirectory)
            {
                await AppendDescendantsAsync(child, items);
            }
        }
    }

    private async Task<string> BuildFolderPathPrefixAsync(string groupRootPath, long groupId, string folderId)
    {
        if (folderId == "/")
        {
            return groupRootPath;
        }

        var segments = await ResolveFolderSegmentsAsync(groupId, folderId, "/");
        return groupRootPath + string.Concat(segments.Select(segment => "/" + WebDavPathHelper.SanitizeSegment(segment)));
    }

    private async Task<List<string>> ResolveFolderSegmentsAsync(long groupId, string targetFolderId, string currentFolderId)
    {
        var result = await GetFileApi().GetGroupFilesAsync(groupId, currentFolderId);
        foreach (var folder in result.Folders)
        {
            if (string.Equals(folder.FolderId, targetFolderId, StringComparison.OrdinalIgnoreCase))
            {
                return [folder.FolderName];
            }

            var nested = await ResolveFolderSegmentsAsync(groupId, targetFolderId, folder.FolderId);
            if (nested.Count > 0)
            {
                nested.Insert(0, folder.FolderName);
                return nested;
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<QGroup>> GetGroupsAsync()
    {
        return await GetSystemApi().GetGroupListAsync();
    }

    private static string BuildGroupSegment(QGroup group) =>
        WebDavPathHelper.SanitizeSegment(group.GroupName);

    private IQFileApi GetFileApi() =>
        _context.GetAdapterExtension<IQFileApi>()
        ?? throw new NotSupportedException("The active adapter does not provide the QQ group-file API.");

    private IQSystemApi GetSystemApi() =>
        _context.GetAdapterExtension<IQSystemApi>()
        ?? throw new NotSupportedException("The active adapter does not provide the QQ system API.");

    private static string BuildTempFilePath(string fileName)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "upload.bin" : fileName;
        var tempDirectory = Path.Combine(Path.GetTempPath(), "Shirobot.Plugin.MyList", "uploads");
        var uniqueName = $"{Guid.NewGuid():N}_{safeFileName}";
        return Path.Combine(tempDirectory, uniqueName);
    }

    private static string BuildLocalFileUri(string localFilePath) =>
        "file:///" + Path.GetFullPath(localFilePath).Replace('\\', '/');

    private string CopyToSmbUpload(string fileName, string tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(_config.SmbWriteRoot))
        {
            throw new InvalidOperationException("未配置 smb_write_root。");
        }

        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "upload.bin" : fileName;
        var uploadDirectory = Path.Combine(_config.SmbWriteRoot, DateTime.UtcNow.ToString("yyyyMMdd"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(uploadDirectory);
        var destinationPath = Path.Combine(uploadDirectory, safeFileName);
        File.Copy(tempFilePath, destinationPath, overwrite: true);
        return destinationPath;
    }

    private string BuildSmbMilkyFileUri(string smbFilePath)
    {
        if (string.IsNullOrWhiteSpace(_config.SmbWriteRoot) || string.IsNullOrWhiteSpace(_config.SmbMilkyRoot))
        {
            throw new InvalidOperationException("未配置 SMB 上传路径映射。");
        }

        var relativePath = Path.GetRelativePath(_config.SmbWriteRoot, smbFilePath);
        var milkyPath = Path.Combine(_config.SmbMilkyRoot, relativePath);
        return "file:///" + milkyPath.Replace('\\', '/');
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // ignored
        }
    }

}
