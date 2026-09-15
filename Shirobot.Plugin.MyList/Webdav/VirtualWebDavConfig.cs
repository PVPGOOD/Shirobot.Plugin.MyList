namespace Shirobot.Plugin.MyList.Webdav;

public sealed class VirtualWebDavConfig
{
    public const string UploadModeAuto = "auto";
    public const string UploadModeBase64 = "base64";
    public const string UploadModeFile = "file";
    public const string FileTransferDirect = "direct";
    public const string FileTransferSmb = "smb";

    public bool Enabled { get; set; } = true;

    public string ListenPrefix { get; set; } = "http://127.0.0.1:19089/";

    public List<string> ListenPrefixes { get; set; } = [];

    public bool RequireAuthentication { get; set; } = true;

    public string Username { get; set; } = "openlist";

    public string Password { get; set; } = "openlist";

    public string Realm { get; set; } = "ShiroBot Virtual WebDAV";

    public bool VerboseLogging { get; set; } = false;

    public long UploadTargetGroupId { get; set; } = 0;

    public string UploadTargetGroupName { get; set; } = "网盘群";

    public string UploadMode { get; set; } = UploadModeAuto;

    public int UploadBase64ThresholdMb { get; set; } = 200;

    public string FileTransferMode { get; set; } = FileTransferDirect;

    public string SmbWriteRoot { get; set; } = @"\\nas\shirobot-upload";

    public string SmbMilkyRoot { get; set; } = @"Z:\shirobot-upload";

    public string SmbUsername { get; set; } = string.Empty;

    public string SmbPassword { get; set; } = string.Empty;

    public IReadOnlyList<string> GetListenPrefixes()
    {
        return ListenPrefixes.Count > 0 ? ListenPrefixes : [ListenPrefix];
    }

    public string GetNormalizedUploadMode()
    {
        var mode = UploadMode.Trim().ToLowerInvariant();
        return mode switch
        {
            UploadModeBase64 => UploadModeBase64,
            UploadModeFile => UploadModeFile,
            _ => UploadModeAuto
        };
    }

    public string GetNormalizedFileTransferMode()
    {
        var mode = FileTransferMode.Trim().ToLowerInvariant();
        return mode switch
        {
            FileTransferSmb => FileTransferSmb,
            _ => FileTransferDirect
        };
    }
}
