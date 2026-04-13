using ShiroBot.Model.Common;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyList.Webdav;
using Shirobot.Plugin.MyList.Webdav.Mapping;

namespace Shirobot.Plugin.MyList.Webdav.Diagnostics;

internal sealed class WebDavDiagnostics(IBotContext context, VirtualWebDavConfig config, GroupFileWebDavMapper mapper)
{
    public async Task<string> BuildFileListTextAsync()
    {
        var items = await mapper.GetRootItemsAsync();
        if (items.Count == 0)
        {
            return "当前没有可映射的群。";
        }

        var lines = items
            .Take(20)
            .Select(item => item.Path)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

        return "当前 WebDAV 根目录:\n" + string.Join('\n', lines);
    }

    public async Task<string> BuildGroupFileProbeTextAsync(long groupId)
    {
        var lines = new List<string>
        {
            $"群文件探针: group = {groupId}"
        };

        try
        {
            var result = await context.File.GetGroupFilesAsync(groupId, "/");
            lines.Add($"folders = {result.Folders.Count}");
            lines.Add($"files = {result.Files.Count}");

            AppendFolderLines(lines, result.Folders);
            AppendFileLines(lines, result.Files);
            await AppendDownloadLineAsync(lines, groupId, result.Files);

            return string.Join('\n', lines);
        }
        catch (Exception ex)
        {
            lines.Add($"probe_error = {ex.GetType().Name}: {ex.Message}");
            lines.Add("当前 adapter 很可能还没有实现 IFileService。");
            return string.Join('\n', lines);
        }
    }

    private static void AppendFolderLines(List<string> lines, IReadOnlyList<GroupFolderEntity> folders)
    {
        foreach (var folder in folders.Take(5))
        {
            lines.Add($"[DIR] {folder.FolderName} ({folder.FolderId})");
        }
    }

    private static void AppendFileLines(List<string> lines, IReadOnlyList<GroupFileEntity> files)
    {
        foreach (var file in files.Take(5))
        {
            lines.Add($"[FILE] {file.FileName} ({file.FileId})");
        }
    }

    private async Task AppendDownloadLineAsync(List<string> lines, long groupId, IReadOnlyList<GroupFileEntity> files)
    {
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var firstFile = files[0];
            var download = await context.File.GetGroupFileDownloadUrlAsync(groupId, firstFile.FileId);
            lines.Add($"first_download = {download.DownloadUrl}");
        }
        catch (Exception ex)
        {
            lines.Add($"download_error = {ex.GetType().Name}: {ex.Message}");
        }
    }
}
