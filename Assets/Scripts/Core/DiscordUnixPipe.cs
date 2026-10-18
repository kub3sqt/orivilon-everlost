#if (UNITY_EDITOR_OSX || UNITY_EDITOR_LINUX || UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX) && !DISABLE_DISCORD
using System;
using System.IO;
using System.Net.Sockets;
using DiscordRPC.IO;
using DiscordRPC.Logging;

namespace Orivilon.Core
{
    /// <summary>
    /// Lokální Unix socket pro Discord na macOS/Linuxu, včetně Apple Silicon.
    /// Nahrazuje pouze transport knihovny; nevyžaduje nativní x86 plugin ani síťový server.
    /// Metody volá pracovní vlákno DiscordRpcClient, nikoliv Unity Update.
    /// </summary>
    internal sealed class DiscordUnixPipe : INamedPipeClient
    {
        private Socket socket;
        private readonly byte[] buffer = new byte[PipeFrame.MAX_SIZE + 8];
        private int buffered;
        public ILogger Logger { get; set; } = new NullLogger();
        public bool IsConnected => socket != null && socket.Connected;
        public int ConnectedPipe { get; private set; } = -1;

        public bool Connect(int pipe)
        {
            Close();
            string[] roots = { Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"),
                Environment.GetEnvironmentVariable("TMPDIR"), Environment.GetEnvironmentVariable("TMP"),
                Environment.GetEnvironmentVariable("TEMP"), "/tmp" };
            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root)) continue;
                for (int i = pipe < 0 ? 0 : pipe; i <= (pipe < 0 ? 9 : pipe); i++)
                {
                    foreach (string prefix in new[] { "", "snap.discord/", "app/com.discordapp.Discord/" })
                    {
                        string path = Path.Combine(root, prefix + "discord-ipc-" + i);
                        try
                        {
                            socket = new Socket(AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, ProtocolType.Unspecified);
                            socket.SendTimeout = 1000;
                            socket.Connect(new UnixDomainSocketEndPoint(path));
                            ConnectedPipe = i;
                            return true;
                        }
                        catch (SocketException) { Close(); }
                    }
                }
            }
            return false;
        }

        public bool ReadFrame(out PipeFrame frame)
        {
            frame = default;
            if (!IsConnected) return false;
            try
            {
                if (buffered < buffer.Length && socket.Poll(0, SelectMode.SelectRead))
                {
                    int read = socket.Receive(buffer, buffered, buffer.Length - buffered, SocketFlags.None);
                    if (read == 0) { Close(); return false; }
                    buffered += read;
                }
                if (buffered < 8) return false;
                // Discord používá little-endian hlavičku. Částečné rámce podržíme do dalšího čtení.
                uint length = (uint)(buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24);
                if (length > PipeFrame.MAX_SIZE) { Close(); return false; }
                int total = 8 + (int)length;
                if (buffered < total) return false;
                byte[] data = new byte[length];
                Buffer.BlockCopy(buffer, 8, data, 0, (int)length);
                frame = new PipeFrame { Opcode = (Opcode)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24), Data = data };
                buffered -= total;
                Buffer.BlockCopy(buffer, total, buffer, 0, buffered);
                return true;
            }
            catch (SocketException) { Close(); return false; }
        }

        public bool WriteFrame(PipeFrame frame)
        {
            if (!IsConnected) return false;
            try
            {
                using (var stream = new MemoryStream())
                {
                    frame.WriteStream(stream);
                    byte[] data = stream.ToArray();
                    int sent = 0;
                    while (sent < data.Length)
                    {
                        int count = socket.Send(data, sent, data.Length - sent, SocketFlags.None);
                        if (count == 0) { Close(); return false; }
                        sent += count;
                    }
                }
                return true;
            }
            catch (SocketException) { Close(); return false; }
        }

        public void Close()
        {
            socket?.Dispose();
            socket = null;
            buffered = 0;
            ConnectedPipe = -1;
        }
        public void Dispose() => Close();
    }
}
#endif
