using ShiroBot.Model.Common;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyList.Webdav;
using Shirobot.Plugin.MyList.Webdav.Diagnostics;
using Shirobot.Plugin.MyList.Webdav.Mapping;
using Shirobot.Plugin.MyList.Webdav.Server;

namespace Shirobot.Plugin.MyList;

public sealed class ShirobotPlugin : PluginBase
{
    private VirtualWebDavConfig _config = new();
    private VirtualWebDavServer? _server;
    private GroupFileWebDavMapper? _webDavMapper;
    private WebDavDiagnostics? _diagnostics;

    public override string Name => "Shirobot.Plugin.MyList";

    public override BotComponentMetadata Metadata { get; } = new()
    {
        Name = "Shirobot.Plugin.MyList",
        Version = "1.0.0",
        Description = ""
    };

    protected override Task LoadAsync()
    {
        BotLog.Info($"Shirobot.Plugin.MyList 开始初始化 config = {Context.Config.ConfigPath}");

        _config = Context.Config.Load<VirtualWebDavConfig>();
        Context.Config.Save(_config);
        BotLog.Info($"Shirobot.Plugin.MyList 配置已加载: enabled={_config.Enabled}, listen={string.Join(", ", _config.GetListenPrefixes())}, upload_mode={_config.GetNormalizedUploadMode()}, file_transfer_mode={_config.GetNormalizedFileTransferMode()}, upload_base64_threshold_mb={_config.UploadBase64ThresholdMb}, verbose_logging={_config.VerboseLogging}");

        _webDavMapper = new GroupFileWebDavMapper(Context, _config);
        _diagnostics = new WebDavDiagnostics(Context, _config, _webDavMapper);

        GroupCommands.MapExact("#webdav", HandleFilesAsync);
        GroupCommands.MapExact("#webdav files", HandleFilesAsync);
        GroupCommands.MapExact("#groupfile probe", HandleGroupFileProbeAsync);

        if (_config.Enabled)
        {
            try
            {
                _server = new VirtualWebDavServer(_config, _webDavMapper);
                _server.Start();
                BotLog.Success($"WebDAV 已启动: {string.Join(", ", _config.GetListenPrefixes())}");
            }
            catch (Exception ex)
            {
                BotLog.Error($"WebDAV 启动失败: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        }
        else
        {
            BotLog.Warning("WebDAV 未启用 请在 config.toml 中将 enabled 设为 true");
        }

        return Task.CompletedTask;
    }

    protected override Task OnUnloadAsync()
    {
        _server?.Dispose();
        _server = null;
        _webDavMapper = null;
        _diagnostics = null;
        BotLog.Info("Shirobot.Plugin.MyList 已卸载");
        return Task.CompletedTask;
    }

    private async Task HandleGroupFileProbeAsync(GroupIncomingMessage message)
    {
        var result = _diagnostics is null
            ? "WebDAV 诊断器未初始化。"
            : await _diagnostics.BuildGroupFileProbeTextAsync(message.Group.GroupId);
        await Context.Message.ReplyAsync(message, result);
    }

    private async Task HandleFilesAsync(GroupIncomingMessage message) =>
        await Context.Message.ReplyAsync(
            message,
            _diagnostics is null ? "WebDAV 诊断器未初始化。" : await _diagnostics.BuildFileListTextAsync());
}
