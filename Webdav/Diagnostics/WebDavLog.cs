using ShiroBot.SDK.Abstractions;
using Shirobot.Plugin.MyList.Webdav;

namespace Shirobot.Plugin.MyList.Webdav.Diagnostics;

internal sealed class WebDavLog(MyListConfig config)
{
    private readonly bool _verboseLogging = config.VerboseLogging;

    public void Trace(string message)
    {
        if (_verboseLogging)
        {
            BotLog.Info(message);
        }
    }

    public static void Info(string message) => BotLog.Info(message);

    public static void Warning(string message) => BotLog.Warning(message);

    public static void Error(string message) => BotLog.Error(message);

    public static void Success(string message) => BotLog.Success(message);
}
