using ShiroBot.SDK.Config;
using ShiroBot.Model.QQ;
using ShiroBot.SDK.Abstractions;
using ShiroBot.SDK.Core;

using ShiroBot.SDK.Models;
using ShiroBot.SDK.Plugin;
using Shirobot.Plugin.MyList.Webdav.Diagnostics;
using Shirobot.Plugin.MyList.Webdav.Mapping;
using Shirobot.Plugin.MyList.Webdav.Server;

[assembly: RequiresShiroBotPackage("shirobot.model.qq", MinimumVersion = "0.9.8")]

[assembly: ShiroBotApiCompatibility("0.9.2", "0.9.2")]

namespace Shirobot.Plugin.MyList;

[BotPlugin(
    "MyList",
    Name = "MyList",
    Author = "PVPGOOD",
    Category = PluginCategory.Utility,
    Description = "将 QQ 群文件映射为 WebDAV 网盘，支持浏览、下载和上传。",
    Version = "1.4.2",
    GithubRepo = "PVPGOOD/Shirobot.Plugin.MyList",
    IsPluginSingleFile = true,
    SharedAssemblies = "ShiroBot.Model.QQ")]
public sealed class MyListPlugin : PluginBase<MyListConfig>
{
    private MyListConfig _config = new();
    private VirtualWebDavServer? _server;
    private GroupFileWebDavMapper? _webDavMapper;
    private WebDavDiagnostics? _diagnostics;

    public override string Name => "MyList";

    protected override Task OnConfigChangedAsync(MyListConfig previous, MyListConfig current, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { RebuildWebDav(current); }
        catch (Exception applyError)
        {
            // A failed bind must be reported to the host; restore the previous listener when possible.
            try { RebuildWebDav(previous); }
            catch (Exception restoreError)
            {
                throw new AggregateException("WebDAV 配置应用失败，旧服务也未能恢复。", applyError, restoreError);
            }
            throw;
        }
        return Task.CompletedTask;
    }

    private void RebuildWebDav(MyListConfig config)
    {
        var groups = Context.GetAdapterExtension<IQGroupApi>();
        var files = Context.GetAdapterExtension<IQFileApi>();
        if (groups is null || (groups.Capabilities & QGroupCapabilities.GroupList) == 0 || files is null)
            throw new NotSupportedException("当前适配器不提供 QQ 群文件能力，无法应用 WebDAV 配置。");
        _server?.Dispose();
        _server = null;
        _config = config;
        _webDavMapper = new GroupFileWebDavMapper(groups, files, config);
        _diagnostics = new WebDavDiagnostics(files, _webDavMapper);
        if (!config.Enabled) return;
        var server = new VirtualWebDavServer(config, _webDavMapper);
        try { server.Start(); }
        catch { server.Dispose(); throw; }
        _server = server;
    }

    protected override Task LoadAsync()
    {
        BotLog.Info($"Shirobot.Plugin.MyList 开始初始化 config = {Context.Config.ConfigPath}");

        _config = Settings;
        Context.Config.Save(_config);
        BotLog.Info($"Shirobot.Plugin.MyList 配置已加载: enabled={_config.Enabled}, listen={string.Join(", ", _config.GetListenPrefixes())}, upload_mode={_config.GetNormalizedUploadMode()}, file_transfer_mode={_config.GetNormalizedFileTransferMode()}, upload_base64_threshold_mb={_config.UploadBase64ThresholdMb}, verbose_logging={_config.VerboseLogging}");

        var qqGroups = Context.GetAdapterExtension<IQGroupApi>();
        var qqFile = Context.GetAdapterExtension<IQFileApi>();
        if (qqGroups is null || (qqGroups.Capabilities & QGroupCapabilities.GroupList) == 0 || qqFile is null)
        {
            BotLog.Warning("当前适配器不提供 QQ 群文件能力(IQGroupApi(GroupList)/IQFileApi),MyList 插件已跳过初始化。");
            return Task.CompletedTask;
        }

        _webDavMapper = new GroupFileWebDavMapper(qqGroups, qqFile, _config);
        _diagnostics = new WebDavDiagnostics(qqFile, _webDavMapper);

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

    private async Task HandleGroupFileProbeAsync(MessageEvent message)
    {
        if (message.Channel.Type != ChannelType.Group || !string.Equals(message.Platform, "qq", StringComparison.OrdinalIgnoreCase))
        {
            await Context.Message.ReplyAsync(message, "当前消息不是有效的 QQ 群消息。");
            return;
        }

        var result = _diagnostics is null
            ? "WebDAV 诊断器未初始化。"
            : await _diagnostics.BuildGroupFileProbeTextAsync(message.Channel.Id);
        await Context.Message.ReplyAsync(message, result);
    }

    private async Task HandleFilesAsync(MessageEvent message) =>
        await Context.Message.ReplyAsync(
            message,
            _diagnostics is null ? "WebDAV 诊断器未初始化。" : await _diagnostics.BuildFileListTextAsync());
}
