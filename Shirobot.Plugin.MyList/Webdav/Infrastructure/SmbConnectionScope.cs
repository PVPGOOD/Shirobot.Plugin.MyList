using System.ComponentModel;
using System.Runtime.InteropServices;
using Shirobot.Plugin.MyList.Webdav;

namespace Shirobot.Plugin.MyList.Webdav.Infrastructure;

internal sealed class SmbConnectionScope : IDisposable
{
    private readonly string? _shareRoot;
    private readonly bool _connected;

    private SmbConnectionScope(string? shareRoot, bool connected)
    {
        _shareRoot = shareRoot;
        _connected = connected;
    }

    public static SmbConnectionScope ConnectIfNeeded(VirtualWebDavConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.SmbWriteRoot))
        {
            return new SmbConnectionScope(null, false);
        }

        if (!config.SmbWriteRoot.TrimStart().StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new SmbConnectionScope(null, false);
        }

        if (string.IsNullOrWhiteSpace(config.SmbUsername) && string.IsNullOrWhiteSpace(config.SmbPassword))
        {
            return new SmbConnectionScope(null, false);
        }

        var shareRoot = GetShareRoot(config.SmbWriteRoot);
        var resource = new NetResource
        {
            Scope = 0,
            ResourceType = 1,
            DisplayType = 0,
            Usage = 0,
            LocalName = null,
            RemoteName = shareRoot,
            Comment = null,
            Provider = null
        };

        var result = WNetAddConnection2(
            ref resource,
            string.IsNullOrWhiteSpace(config.SmbPassword) ? null : config.SmbPassword,
            string.IsNullOrWhiteSpace(config.SmbUsername) ? null : config.SmbUsername,
            0);

        if (result == 0 || result == 1219 || result == 85)
        {
            return new SmbConnectionScope(shareRoot, result == 0);
        }

        throw new IOException($"连接 SMB 共享失败: {shareRoot}", new Win32Exception(result));
    }

    public void Dispose()
    {
        if (!_connected || string.IsNullOrWhiteSpace(_shareRoot))
        {
            return;
        }

        WNetCancelConnection2(_shareRoot, 0, true);
    }

    private static string GetShareRoot(string uncPath)
    {
        var trimmed = uncPath.Trim().TrimEnd('\\');
        if (!trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"smb_write_root 不是合法 UNC 路径: {uncPath}");
        }

        var parts = trimmed.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"smb_write_root 缺少共享名称: {uncPath}");
        }

        return $@"\\{parts[0]}\{parts[1]}";
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NetResource netResource,
        string? password,
        string? username,
        int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(
        string name,
        int flags,
        bool force);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int ResourceType;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
