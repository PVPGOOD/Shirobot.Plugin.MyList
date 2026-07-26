using ShiroBot.Qq.Model;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;
using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyList.Webdav.Diagnostics;
using Shirobot.Plugin.MyList.Webdav.Mapping;
using Shirobot.Plugin.MyList.Webdav.Server;

namespace Shirobot.Plugin.MyList;

[BotPlugin(id:"MyList", 
    Name = "Mylist",
    Description ="Shirobot.Plugin.MyList", 
    Version = "1.1.0",
    GithubRepo = "PVPGOOD/Shirobot.Plugin.MyList"
    )]
public sealed class MyListPlugin : PluginBase
{
    private MyListConfig _config = new();
    private VirtualWebDavServer? _server;
    private GroupFileWebDavMapper? _webDavMapper;
    private WebDavDiagnostics? _diagnostics;

    public override string Name => "Shirobot.Plugin.MyList";

    protected override Task LoadAsync()
    {
        BotLog.Info($"Shirobot.Plugin.MyList 开始初始化 config = {Context.Config.ConfigPath}");

        _config = Context.Config.Load<MyListConfig>();
        Context.Config.Save(_config);
        BotLog.Info($"Shirobot.Plugin.MyList 配置已加载: enabled={_config.Enabled}, listen={string.Join(", ", _config.GetListenPrefixes())}, upload_mode={_config.GetNormalizedUploadMode()}, file_transfer_mode={_config.GetNormalizedFileTransferMode()}, upload_base64_threshold_mb={_config.UploadBase64ThresholdMb}, verbose_logging={_config.VerboseLogging}");

        var qqSystem = Context.GetAdapterExtension<IQqSystemApi>();
        var qqFile = Context.GetAdapterExtension<IQqFileApi>();
        if (qqSystem is null || qqFile is null)
        {
            BotLog.Warning("当前适配器不提供 QQ 群文件能力(IQqSystemApi/IQqFileApi),MyList 插件已跳过初始化。");
            return Task.CompletedTask;
        }

        _webDavMapper = new GroupFileWebDavMapper(qqSystem, qqFile, _config);
        _diagnostics = new WebDavDiagnostics(qqFile, _webDavMapper);

        GroupCommands.MapExact("#webdav", HandleFilesAsync);
        GroupCommands.MapExact("#webdav files", HandleFilesAsync);
        GroupCommands.MapExact("#groupfile probe", HandleGroupFileProbeAsync);

        // Context.WebHost.RegisterFile("/webdav", _webDavMapper);
        
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

    private async Task HandleGroupFileProbeAsync(MessageEvent message)
    {
        var result = _diagnostics is null
            ? "WebDAV 诊断器未初始化。"
            : await _diagnostics.BuildGroupFileProbeTextAsync(long.Parse(message.Channel.Id));
        await Context.Message.ReplyAsync(message, result);
    }

    private async Task HandleFilesAsync(MessageEvent message) =>
        await Context.Message.ReplyAsync(
            message,
            _diagnostics is null ? "WebDAV 诊断器未初始化。" : await _diagnostics.BuildFileListTextAsync());
}
