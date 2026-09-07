// LanHost.cs — local static Unity player plus authenticated native WebSocket room.
// Default to loopback in the companion CLI; LAN binding is an explicit option.
// Credentials are never HTTP resources. ASP.NET owns HTTP/WebSocket frame parsing.
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AshenSpire.Transport;

public static class LanHost
{
    public const string Subprotocol = "ashenspire.native.v1";
    public static WebApplication Build(LanOptions options,ILanGameFactory factory)
    {
        var webRoot = Path.GetFullPath(options.WebRoot);
        if (!Directory.Exists(webRoot) || !File.Exists(Path.Combine(webRoot,"index.html"))) throw new ArgumentException("Web root must contain a compiled player's index.html.");
        if (options.StatePath != null && (Path.GetFullPath(options.StatePath).StartsWith(webRoot.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(options.StatePath).Equals(webRoot,StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("The private host state must live outside the public Web folder.");
        if (options.JoinToken.Length < 32 || options.HostToken.Length < 32 || options.JoinToken == options.HostToken) throw new ArgumentException("Use distinct cryptographically random host and join tokens.");
        if (options.MaximumSeats < 2 || options.MaximumSeats > 4 || options.MaximumConnections < options.MaximumSeats || options.MaximumInboundBytes < 1024) throw new ArgumentException("Invalid room limits.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = webRoot });
        var room = new LanRoom(options,factory);
        builder.Logging.ClearProviders(); builder.Services.AddSingleton(room); var app = builder.Build(); app.Urls.Add(options.ListenUrl);
        app.Lifetime.ApplicationStopped.Register(room.Dispose);
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.MapGet("/api/lan/info",() => Results.Json(new { protocol = Subprotocol, version = 1, maximumSeats = options.MaximumSeats, connections = room.ConnectionCount, websocketPath = "/lan" }));
        app.Map("/lan",async context =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin,UriKind.Absolute,out var address) || address.Authority != context.Request.Host.Value || address.Scheme != context.Request.Scheme)) { context.Response.StatusCode = 403; return; }
            if (!context.WebSockets.IsWebSocketRequest || !context.WebSockets.WebSocketRequestedProtocols.Contains(Subprotocol)) { context.Response.StatusCode = 400; return; }
            if (!room.TryConnect()) { context.Response.StatusCode = 429; return; }
            LanConnection? connection = null;
            try
            {
                var socket = await context.WebSockets.AcceptWebSocketAsync(Subprotocol); connection = new LanConnection(socket,options); connection.StartWriter();
                while (true) { var message = await connection.Receive(context.RequestAborted); if (message == null) break; await room.Process(connection,message); }
            }
            catch (ProtocolException error)
            {
                if (connection != null) { connection.Send(new JObject { ["v"] = 1, ["type"] = "error", ["payload"] = new JObject { ["code"] = error.Message, ["message"] = error.Message } }); await connection.CompleteAsync(); }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or OverflowException)
            {
                if (connection != null) { connection.Send(new JObject { ["v"] = 1, ["type"] = "error", ["payload"] = new JObject { ["code"] = "invalid_setup", ["message"] = "The character setup or protocol fields were invalid." } }); await connection.CompleteAsync(); }
            }
            catch (Exception error) when (error is WebSocketException or OperationCanceledException or JsonException or DecoderFallbackException or IOException) { /* Closing ends this connection; tokens/payloads are never logged. */ }
            finally
            {
                if (connection != null) { try { await room.Disconnected(connection); } finally { await connection.DisposeAsync(); } }
                // Handshake acceptance can fail before a connection is constructed.
                else await room.Disconnected(null);
            }
        });
        var files = new PhysicalFileProvider(webRoot); app.Lifetime.ApplicationStopped.Register(files.Dispose);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        var types = new FileExtensionContentTypeProvider(); types.Mappings[".wasm"] = "application/wasm"; types.Mappings[".data"] = "application/octet-stream"; types.Mappings[".unityweb"] = "application/octet-stream"; types.Mappings[".gz"] = "application/octet-stream"; types.Mappings[".br"] = "application/octet-stream";
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, ContentTypeProvider = types, OnPrepareResponse = context =>
        {
            context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            var extension = Path.GetExtension(context.File.Name);
            if (extension == ".gz" || extension == ".br")
            {
                context.Context.Response.Headers.ContentEncoding = extension == ".gz" ? "gzip" : "br";
                if (types.TryGetContentType(Path.GetFileNameWithoutExtension(context.File.Name),out var originalType)) context.Context.Response.ContentType = originalType;
            }
            if (context.File.Name.EndsWith(".html",StringComparison.OrdinalIgnoreCase)) context.Context.Response.Headers.CacheControl = "no-store";
        }});
        return app;
    }
}


