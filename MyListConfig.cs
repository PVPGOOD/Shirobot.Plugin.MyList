using ShiroBot.SDK.Config;

namespace Shirobot.Plugin.MyList;

[ConfigModel]
public sealed class MyListConfig
{
    public const string UploadModeAuto = "auto";
    public const string UploadModeBase64 = "base64";
    public const string UploadModeFile = "file";
    public const string FileTransferDirect = "direct";
    public const string FileTransferSmb = "smb";

    [ConfigField("启用群文件 WebDAV 服务；修改后重载插件生效。", Label = "启用 WebDAV")]
    public bool Enabled { get; set; } = true;

    [ConfigField("监听地址需以 / 结尾；监听地址列表为空时使用此项。", Label = "默认监听地址")]
    public string ListenPrefix { get; set; } = "http://127.0.0.1:19089/";

    [ConfigField("可设置多个 HTTP 监听前缀，非空时覆盖默认监听地址。", Label = "监听地址列表")]
    public List<string> ListenPrefixes { get; set; } = [];

    [ConfigField("开启后 WebDAV 客户端必须提供下方用户名和密码。", Label = "启用身份验证")]
    public bool RequireAuthentication { get; set; } = true;

    [ConfigField("WebDAV HTTP Basic 身份验证的用户名。", Label = "WebDAV 用户名")]
    public string Username { get; set; } = "openlist";

    [ConfigField("WebDAV HTTP Basic 身份验证的密码。", Label = "WebDAV 密码", Type = "password")]
    public string Password { get; set; } = "openlist";

    [ConfigField("向 WebDAV 客户端显示的 HTTP 身份验证域名称。", Label = "身份验证域")]
    public string Realm { get; set; } = "ShiroBot Virtual WebDAV";

    [ConfigField("记录 WebDAV 请求和文件操作的详细诊断信息。", Label = "详细日志")]
    public bool VerboseLogging { get; set; } = false;

    [ConfigField("预留配置，当前实现按 WebDAV 请求的群目录上传，尚未使用此项。", Label = "上传目标群 ID")]
    public string UploadTargetGroupId { get; set; } = string.Empty;

    [ConfigField("预留配置，当前实现尚未使用此项选择上传目标。", Label = "上传目标群名")]
    public string UploadTargetGroupName { get; set; } = "网盘群";

    [ConfigField("auto 按文件大小选择；base64 将内容编码上传；file 使用适配器端可访问的文件路径。", Label = "上传模式", Type = "select", Options = ["auto", "base64", "file"])]
    public string UploadMode { get; set; } = UploadModeAuto;

    [ConfigField("auto 模式下不超过此大小使用 Base64，超过时使用文件路径；运行时最低按 1 MB 计算。", Label = "自动 Base64 阈值（MB）")]
    public int UploadBase64ThresholdMb { get; set; } = 200;

    [ConfigField("direct 使用本机路径；smb 先复制到共享目录，再映射到 Milky 端路径。", Label = "文件传递方式", Type = "select", Options = ["direct", "smb"])]
    public string FileTransferMode { get; set; } = FileTransferDirect;

    [ConfigField("smb 模式下宿主写入上传文件的共享目录路径。", Label = "宿主共享目录")]
    public string SmbWriteRoot { get; set; } = @"\\nas\shirobot-upload";

    [ConfigField("同一共享目录在 Milky 端的路径，用于生成上传文件 URI。", Label = "Milky 共享目录")]
    public string SmbMilkyRoot { get; set; } = @"Z:\shirobot-upload";

    [ConfigField("Windows 连接 SMB 共享使用的用户名；留空使用当前系统身份。", Label = "SMB 用户名")]
    public string SmbUsername { get; set; } = string.Empty;

    [ConfigField("连接 SMB 共享使用的密码。", Label = "SMB 密码", Type = "password")]
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
