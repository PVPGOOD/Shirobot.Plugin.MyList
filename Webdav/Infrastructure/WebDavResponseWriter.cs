using System.Net;
using System.Xml.Linq;

using Shirobot.Plugin.MyList.Webdav.Models;

namespace Shirobot.Plugin.MyList.Webdav.Infrastructure;

internal static class WebDavResponseWriter
{
    private static readonly XNamespace DavNs = "DAV:";

    public static Task WriteOptionsAsync(HttpListenerResponse response)
    {
        ApplyNoCacheHeaders(response);
        response.StatusCode = (int)HttpStatusCode.OK;
        response.AddHeader("DAV", "1");
        response.AddHeader("Allow", "OPTIONS, PROPFIND, GET, HEAD, PUT, DELETE, MKCOL");
        response.AddHeader("MS-Author-Via", "DAV");
        response.Close();
        return Task.CompletedTask;
    }

    public static async Task WritePropFindAsync(HttpListenerResponse response, IReadOnlyList<WebDavItem> items)
    {
        ApplyNoCacheHeaders(response);
        var multistatus = new XElement(DavNs + "multistatus",
            new XAttribute(XNamespace.Xmlns + "d", DavNs),
            items.Select(item => new XElement(DavNs + "response",
                new XElement(DavNs + "href", item.IsDirectory && !item.Path.EndsWith('/') ? item.Path + "/" : item.Path),
                new XElement(DavNs + "propstat",
                    new XElement(DavNs + "prop",
                        new XElement(DavNs + "displayname", item.Name),
                        new XElement(DavNs + "resourcetype", item.IsDirectory ? new XElement(DavNs + "collection") : null),
                        new XElement(DavNs + "getcontentlength", item.ContentLength),
                        new XElement(DavNs + "getcontenttype", item.ContentType),
                        new XElement(DavNs + "creationdate", item.LastModified.ToString("O")),
                        new XElement(DavNs + "getlastmodified", item.LastModified.ToUniversalTime().ToString("R"))),
                    new XElement(DavNs + "status", "HTTP/1.1 200 OK")))));

        response.StatusCode = 207;
        await WriteXmlAsync(response, new XDocument(multistatus));
    }

    public static void WriteRedirect(HttpListenerResponse response, WebDavFileContent content)
    {
        ApplyNoCacheHeaders(response);
        response.StatusCode = (int)HttpStatusCode.Found;
        response.RedirectLocation = content.DownloadUrl;
        response.ContentType = content.ContentType;
        response.Close();
    }

    public static void WriteWriteResult(HttpListenerResponse response, WebDavWriteResult result, int successStatus, int alreadyExistsStatus)
    {
        ApplyNoCacheHeaders(response);
        if (result.Succeeded)
        {
            response.StatusCode = successStatus;
            response.Close();
            return;
        }

        response.StatusCode = result.AlreadyExists
            ? alreadyExistsStatus
            : result.NotFound
                ? (int)HttpStatusCode.NotFound
                : result.Conflict
                    ? (int)HttpStatusCode.Conflict
                    : (int)HttpStatusCode.BadRequest;
        response.Close();
    }

    public static void WriteStatus(HttpListenerResponse response, HttpStatusCode statusCode)
    {
        ApplyNoCacheHeaders(response);
        response.StatusCode = (int)statusCode;
        response.Close();
    }

    public static async Task WriteInternalServerErrorAsync(HttpListenerResponse response)
    {
        if (!response.OutputStream.CanWrite)
        {
            return;
        }

        ApplyNoCacheHeaders(response);
        response.StatusCode = (int)HttpStatusCode.InternalServerError;
        await WriteTextAsync(response, "internal server error", "text/plain; charset=utf-8");
    }

    private static void ApplyNoCacheHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["Expires"] = "0";
    }

    private static async Task WriteXmlAsync(HttpListenerResponse response, XDocument document)
    {
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + document;
        await WriteTextAsync(response, xml, "application/xml; charset=utf-8");
    }

    private static async Task WriteTextAsync(HttpListenerResponse response, string text, string contentType)
    {
        response.ContentType = contentType;
        var buffer = System.Text.Encoding.UTF8.GetBytes(text);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer);
        response.Close();
    }
}
