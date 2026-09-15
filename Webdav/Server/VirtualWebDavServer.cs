using System.Net;
using Shirobot.Plugin.MyList.Webdav.Diagnostics;
using Shirobot.Plugin.MyList.Webdav.Infrastructure;
using Shirobot.Plugin.MyList.Webdav.Mapping;

namespace Shirobot.Plugin.MyList.Webdav.Server;

internal sealed class VirtualWebDavServer : IDisposable
{
    private readonly MyListConfig _config;
    private readonly GroupFileWebDavMapper _mapper;
    private readonly WebDavLog _log;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;

    public VirtualWebDavServer(MyListConfig config, GroupFileWebDavMapper mapper)
    {
        _config = config;
        _mapper = mapper;
        _log = new WebDavLog(config);

        foreach (var prefix in GetNormalizedListenPrefixes(config))
        {
            _listener.Prefixes.Add(prefix);
        }
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener.Start();
        Task.Run(() => RunAsync(_cts.Token));
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
            {
                _listener.Stop();
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            _listener.Close();
            _cts?.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            _log.Trace(
                $"WebDAV 请求进入: method={context.Request.HttpMethod}, path={DecodeRequestPath(context.Request)}, rawUrl={context.Request.RawUrl ?? "<null>"}, userAgent={context.Request.UserAgent ?? "<null>"}, contentLength={context.Request.ContentLength64}, contentType={context.Request.ContentType ?? "<null>"}");
            if (!IsAuthorized(context.Request))
            {
                WebDavLog.Warning(
                    $"WebDAV 认证失败: method={context.Request.HttpMethod}, path={DecodeRequestPath(context.Request)}, userAgent={context.Request.UserAgent ?? "<null>"}");
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                context.Response.AddHeader("WWW-Authenticate", $"Basic realm=\"{_config.Realm}\"");
                context.Response.Close();
                return;
            }

            switch (context.Request.HttpMethod.ToUpperInvariant())
            {
                case "OPTIONS":
                    await WebDavResponseWriter.WriteOptionsAsync(context.Response);
                    break;
                case "PROPFIND":
                    await HandlePropFindAsync(context);
                    break;
                case "GET":
                case "HEAD":
                    await HandleGetAsync(context, cancellationToken);
                    break;
                case "PUT":
                    await HandlePutAsync(context, cancellationToken);
                    break;
                case "DELETE":
                    await HandleDeleteAsync(context);
                    break;
                case "MKCOL":
                    await HandleMkColAsync(context);
                    break;
                case "MOVE":
                    await HandleMoveAsync(context);
                    break;
                default:
                    WebDavLog.Warning(
                        $"WebDAV 不支持的方法: method={context.Request.HttpMethod}, path={DecodeRequestPath(context.Request)}");
                    WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.MethodNotAllowed);
                    break;
            }
        }
        catch (Exception ex)
        {
            WebDavLog.Error($"WebDAV 请求处理失败: {ex.Message}");
            await WebDavResponseWriter.WriteInternalServerErrorAsync(context.Response);
        }
    }

    private async Task HandlePropFindAsync(HttpListenerContext context)
    {
        var requestPath = DecodeRequestPath(context.Request);
        var depthHeader = context.Request.Headers["Depth"];
        var includeChildren = !string.Equals(depthHeader, "0", StringComparison.OrdinalIgnoreCase);
        var isInfinityDepth = string.Equals(depthHeader, "infinity", StringComparison.OrdinalIgnoreCase);
        _log.Trace(
            $"WebDAV PROPFIND 请求: path={requestPath}, depth={depthHeader ?? "<null>"}, includeChildren={includeChildren}, infinity={isInfinityDepth}");
        var resolution = await _mapper.ResolveAsync(requestPath, includeChildren);

        if (!resolution.Exists || resolution.Item is null)
        {
            _log.Trace($"WebDAV PROPFIND 未命中: path={requestPath}");
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.NotFound);
            return;
        }

        var items = isInfinityDepth
            ? await _mapper.EnumerateSubtreeAsync(requestPath)
            : new[] { resolution.Item }.Concat(includeChildren ? resolution.Children : []).ToList();

        _log.Trace(
            $"WebDAV PROPFIND 返回: path={requestPath}, itemCount={items.Count}, self={resolution.Item.Path}, isDirectory={resolution.Item.IsDirectory}");

        await WebDavResponseWriter.WritePropFindAsync(context.Response, items.ToList());
    }

    private async Task HandleGetAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var requestPath = DecodeRequestPath(context.Request);
        var resolution = await _mapper.ResolveAsync(requestPath, includeChildren: false);

        if (!resolution.Exists || resolution.Item is null || resolution.Item.IsDirectory)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.NotFound);
            return;
        }

        var content = await _mapper.GetFileContentAsync(requestPath, cancellationToken);
        if (content is null)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.NotFound);
            return;
        }

        WebDavResponseWriter.WriteRedirect(context.Response, content);
    }

    private async Task HandlePutAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var requestPath = DecodeRequestPath(context.Request);
        _log.Trace($"WebDAV PUT 请求: path={requestPath}, contentLength={context.Request.ContentLength64}, contentType={context.Request.ContentType ?? "<null>"}");
        var result = await _mapper.PutFileAsync(requestPath, context.Request.InputStream, cancellationToken);
        WebDavLog.Info(
            $"WebDAV PUT 结果: path={requestPath}, succeeded={result.Succeeded}, created={result.Created}, alreadyExists={result.AlreadyExists}, notFound={result.NotFound}, conflict={result.Conflict}, error={result.ErrorMessage ?? "<null>"}");
        var successStatus = result.Created ? (int)HttpStatusCode.Created : (int)HttpStatusCode.NoContent;
        WebDavResponseWriter.WriteWriteResult(context.Response, result, successStatus, (int)HttpStatusCode.PreconditionFailed);
    }

    private async Task HandleMkColAsync(HttpListenerContext context)
    {
        var requestPath = DecodeRequestPath(context.Request);
        _log.Trace($"WebDAV MKCOL 请求: path={requestPath}");
        var result = await _mapper.CreateFolderAsync(requestPath);
        WebDavLog.Info(
            $"WebDAV MKCOL 结果: path={requestPath}, succeeded={result.Succeeded}, created={result.Created}, alreadyExists={result.AlreadyExists}, notFound={result.NotFound}, conflict={result.Conflict}, error={result.ErrorMessage ?? "<null>"}");
        WebDavResponseWriter.WriteWriteResult(context.Response, result, (int)HttpStatusCode.Created, (int)HttpStatusCode.MethodNotAllowed);
    }

    private async Task HandleDeleteAsync(HttpListenerContext context)
    {
        var requestPath = DecodeRequestPath(context.Request);
        _log.Trace($"WebDAV DELETE 请求: path={requestPath}");
        var result = await _mapper.DeleteAsync(requestPath);
        WebDavLog.Info(
            $"WebDAV DELETE 结果: path={requestPath}, succeeded={result.Succeeded}, created={result.Created}, alreadyExists={result.AlreadyExists}, notFound={result.NotFound}, conflict={result.Conflict}, error={result.ErrorMessage ?? "<null>"}");
        WebDavResponseWriter.WriteWriteResult(context.Response, result, (int)HttpStatusCode.NoContent, (int)HttpStatusCode.NoContent);
    }

    private async Task HandleMoveAsync(HttpListenerContext context)
    {
        var sourcePath = DecodeRequestPath(context.Request);
        var destinationHeader = context.Request.Headers["Destination"];
        if (string.IsNullOrWhiteSpace(destinationHeader))
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.BadRequest);
            return;
        }

        var destinationPath = DecodeDestinationPath(destinationHeader);
        var overwrite = !string.Equals(context.Request.Headers["Overwrite"], "F", StringComparison.OrdinalIgnoreCase);
        _log.Trace($"WebDAV MOVE 请求: source={sourcePath}, destination={destinationPath}, overwrite={overwrite}");
        var result = await _mapper.MoveAsync(sourcePath, destinationPath, overwrite);
        WebDavLog.Info(
            $"WebDAV MOVE 结果: source={sourcePath}, destination={destinationPath}, succeeded={result.Succeeded}, created={result.Created}, alreadyExists={result.AlreadyExists}, notFound={result.NotFound}, conflict={result.Conflict}, forbidden={result.Forbidden}, error={result.ErrorMessage ?? "<null>"}");

        if (result.Succeeded)
        {
            context.Response.StatusCode = result.Created ? (int)HttpStatusCode.Created : (int)HttpStatusCode.NoContent;
            context.Response.Close();
            return;
        }

        if (result.NotFound)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.NotFound);
            return;
        }

        if (result.AlreadyExists)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.PreconditionFailed);
            return;
        }

        if (result.Forbidden)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.Forbidden);
            return;
        }

        if (result.Conflict)
        {
            WebDavResponseWriter.WriteStatus(context.Response, HttpStatusCode.Conflict);
            return;
        }

        await WebDavResponseWriter.WriteInternalServerErrorAsync(context.Response);
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        if (!_config.RequireAuthentication)
        {
            return true;
        }

        var header = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(header) || !header.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var encoded = header["Basic ".Length..].Trim();
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            var separatorIndex = decoded.IndexOf(':');
            if (separatorIndex < 0)
            {
                return false;
            }

            var username = decoded[..separatorIndex];
            var password = decoded[(separatorIndex + 1)..];
            return string.Equals(username, _config.Username, StringComparison.Ordinal) &&
                   string.Equals(password, _config.Password, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static string DecodeRequestPath(HttpListenerRequest request) =>
        WebDavPathHelper.NormalizePath(Uri.UnescapeDataString(request.Url?.AbsolutePath ?? "/"));

    private static string DecodeDestinationPath(string destination)
    {
        if (Uri.TryCreate(destination, UriKind.Absolute, out var destinationUri))
        {
            return WebDavPathHelper.NormalizePath(Uri.UnescapeDataString(destinationUri.AbsolutePath));
        }

        return WebDavPathHelper.NormalizePath(Uri.UnescapeDataString(destination));
    }

    private static IReadOnlyList<string> GetNormalizedListenPrefixes(MyListConfig config) =>
        config.GetListenPrefixes()
            .Where(prefix => !string.IsNullOrWhiteSpace(prefix))
            .Select(NormalizePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizePrefix(string prefix) =>
        prefix.Trim().EndsWith('/') ? prefix.Trim() : prefix.Trim() + "/";
}
