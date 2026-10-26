using System.Diagnostics;
using System.Threading;
using System.IO.Pipes;
using System.Text;
using System.IO;
using BepInEx;
using System;

// For some reason using a module for the RPC did not work and doing it like this was the only way it would work :sob:

[BepInPlugin("com.proxy.gunsawrpc", "Gunsaw RPC", "0.0.1")]
public class Plugin : BaseUnityPlugin
{
    private const string AppId = "1564169576902697011";
    private const string Version = "0.0.1";

    private Thread thread;
    private volatile bool running = true;
    private long startTime;

    private void Awake()
    {
        startTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        thread = new Thread(Run) { IsBackground = true };
        thread.Start();
        Logger.LogInfo("Discord RPC thread started");
    }

    private void OnDestroy()
    {
        running = false;
    }

    private void Run()
    {
        while (running)
        {
            try
            {
                using (NamedPipeClientStream pipe = Connect())
                {
                    if (pipe == null)
                    {
                        Logger.LogWarning("No Discord pipe found, retrying");
                        Thread.Sleep(5000);
                        continue;
                    }

                    Logger.LogInfo("Connected to Discord pipe");

                    int op;
                    string json;

                    Send(pipe, 0, "{\"v\":1,\"client_id\":\"" + AppId + "\"}");
                    ReadFrame(pipe, out op, out json);
                    Logger.LogInfo("Handshake reply: " + json);

                    Send(pipe, 1, BuildActivity());

                    while (running)
                    {
                        ReadFrame(pipe, out op, out json);
                        Logger.LogDebug("Discord frame " + op + ": " + json);
                        if (op == 3) Send(pipe, 4, json);
                        if (op == 2) break;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning("RPC error: " + e.Message);
            }

            Thread.Sleep(5000);
        }
    }

    private NamedPipeClientStream Connect()
    {
        for (int i = 0; i < 10; i++)
        {
            NamedPipeClientStream p = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut);
            try
            {
                p.Connect(500);
                return p;
            }
            catch
            {
                p.Dispose();
            }
        }
        return null;
    }

    private string BuildActivity()
    {
        int pid = Process.GetCurrentProcess().Id;
        return "{\"cmd\":\"SET_ACTIVITY\",\"args\":{\"pid\":" + pid + ",\"activity\":{"
            + "\"details\":\"" + Esc("Shooting furries probably") + "\","
            + "\"timestamps\":{\"start\":" + startTime + "},"
            + "\"assets\":{\"large_image\":\"https://raw.githubusercontent.com/hxproxy/GunsawRPC/refs/heads/main/logo512.png\",\"large_text\":\"GunsawRPC v" + Version + "\",\"large_url\":\"https://github.com/hxproxy/GunsawRPC\"}"
            + "}},\"nonce\":\"" + Guid.NewGuid() + "\"}";
    }

    private static string Esc(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private static void Send(Stream s, int op, string json)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        byte[] buf = new byte[8 + body.Length];
        BitConverter.GetBytes(op).CopyTo(buf, 0);
        BitConverter.GetBytes(body.Length).CopyTo(buf, 4);
        body.CopyTo(buf, 8);
        s.Write(buf, 0, buf.Length);
        s.Flush();
    }

    private static void ReadFrame(Stream s, out int op, out string json)
    {
        byte[] head = ReadExact(s, 8);
        op = BitConverter.ToInt32(head, 0);
        int len = BitConverter.ToInt32(head, 4);
        json = Encoding.UTF8.GetString(ReadExact(s, len));
    }

    private static byte[] ReadExact(Stream s, int n)
    {
        byte[] b = new byte[n];
        int off = 0;
        while (off < n)
        {
            int r = s.Read(b, off, n - off);
            if (r <= 0) throw new EndOfStreamException();
            off += r;
        }
        return b;
    }
}