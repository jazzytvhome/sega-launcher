using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace SegaLauncher;

/// <summary>
/// Serves the in-memory game files over http://127.0.0.1:&lt;port&gt; for "Play" mode.
/// The decrypted bytes live only in this process; closing the launcher discards them.
/// </summary>
public sealed class LocalServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly IReadOnlyDictionary<string, byte[]> _files;
    public string BaseUrl { get; }

    public LocalServer(IReadOnlyDictionary<string, byte[]> files, int port)
    {
        _files = files;
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);
    }

    public static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public void Start()
    {
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { break; }
            _ = Task.Run(() => Serve(ctx));
        }
    }

    private void Serve(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url!.AbsolutePath.TrimStart('/');
            if (path.Length == 0) path = "index.html";

            if (!_files.TryGetValue(path, out var bytes))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }
            ctx.Response.ContentType = ContentType(path);
            ctx.Response.ContentLength64 = bytes.Length;
            ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
            ctx.Response.OutputStream.Close();
        }
        catch
        {
            try { ctx.Response.Abort(); } catch { /* client went away */ }
        }
    }

    private static string ContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".ico" => "image/x-icon",
            ".ttf" => "font/ttf",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".mp3" => "audio/mpeg",
            ".ogg" => "audio/ogg",
            ".wav" => "audio/wav",
            ".obj" => "text/plain",
            ".glb" => "model/gltf-binary",
            ".wasm" => "application/wasm",
            ".webmanifest" or ".manifest" => "application/manifest+json",
            _ => "application/octet-stream",
        };

    public void Dispose()
    {
        try { _listener.Stop(); _listener.Close(); } catch { /* already down */ }
    }
}
