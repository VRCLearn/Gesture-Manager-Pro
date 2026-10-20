#if VRC_SDK_VRCSDK3
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using UnityEditor;

namespace VRCLearn.GestureManagerPro.Editor.Modules.Vrc3.OpenSoundControl
{
    [InitializeOnLoad]
    public sealed class UdpListener : IDisposable
    {
        internal int Port { get; }
        private static readonly object Gate = new object();
        private static readonly Dictionary<int, SharedSocket> Sockets = new Dictionary<int, SharedSocket>();
        private readonly Action<byte[]> _onData;
        private volatile bool _closed;

        static UdpListener()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CloseAll;
            EditorApplication.quitting += CloseAll;
        }

        public UdpListener(int port, Action<byte[]> callback)
        {
            if (port < 1 || port > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            Port = port;
            _onData = callback ?? throw new ArgumentNullException(nameof(callback));
            lock (Gate)
            {
                if (!Sockets.TryGetValue(port, out var socket))
                {
                    socket = new SharedSocket(port);
                    Sockets.Add(port, socket);
                }
                socket.Subscribers.Add(this);
            }
        }

        public void Close() => Dispose();

        public void Dispose()
        {
            lock (Gate)
            {
                if (_closed) return;
                _closed = true;
                if (!Sockets.TryGetValue(Port, out var socket)) return;
                socket.Subscribers.Remove(this);
                if (socket.Subscribers.Count != 0) return;
                Sockets.Remove(Port);
                socket.Close();
            }
        }

        private static void CloseAll()
        {
            lock (Gate)
            {
                foreach (var socket in Sockets.Values)
                {
                    foreach (var subscriber in socket.Subscribers) subscriber._closed = true;
                    socket.Close();
                }
                Sockets.Clear();
            }
        }

        /// <summary>One socket per port, with independent lifetime for Debug and tracking subscribers.</summary>
        private sealed class SharedSocket
        {
            internal readonly List<UdpListener> Subscribers = new List<UdpListener>();
            private readonly UdpClient _client;
            private bool _closing;

            internal SharedSocket(int port)
            {
                _client = new UdpClient(port);
                _client.BeginReceive(Receive, null);
            }

            private void Receive(IAsyncResult result)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var bytes = _client.EndReceive(result, ref remote);
                    UdpListener[] subscribers;
                    lock (Gate) subscribers = Subscribers.ToArray();
                    foreach (var subscriber in subscribers)
                    {
                        if (subscriber._closed) continue;
                        try { subscriber._onData(bytes); }
                        catch (Exception) { /* One subscriber must not stop other OSC consumers. */ }
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (SocketException) { if (_closing) return; }

                lock (Gate)
                {
                    if (_closing) return;
                    try { _client.BeginReceive(Receive, null); }
                    catch (ObjectDisposedException) { }
                    catch (SocketException) { }
                }
            }

            internal void Close()
            {
                _closing = true;
                _client.Close();
            }
        }
    }
}
#endif
