using ShiroBot.SDK.Abstractions;
using Shirobot.Plugin.MyList.Webdav;

namespace Shirobot.Plugin.MyList.Webdav.Diagnostics;

internal sealed class WebDavLog
{
    private readonly bool _verboseLogging;

    public WebDavLog(VirtualWebDavConfig config)
    {
        _verboseLogging = config.VerboseLogging;
    }

    public void Trace(string message)
    {
        if (_verboseLogging)
        {
            BotLog.Info(message);
        }
    }

    public void Info(string message) => BotLog.Info(message);

    public void Warning(string message) => BotLog.Warning(message);

    public void Error(string message) => BotLog.Error(message);

    public void Success(string message) => BotLog.Success(message);
}
