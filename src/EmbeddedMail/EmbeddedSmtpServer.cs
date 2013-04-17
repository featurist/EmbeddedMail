using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace EmbeddedMail
{
    public interface ISmtpServer : IDisposable
    {
        IEnumerable<MailMessage> ReceivedMessages();
        void Start();
        void Stop();
    }

    public class EmbeddedSmtpServer : ISmtpServer
    {
        private readonly IList<MailMessage> _messages = new List<MailMessage>();
        private readonly ConcurrentBag<SmtpSession> _sessions = new ConcurrentBag<SmtpSession>();

        private bool _stopped;

        public EmbeddedSmtpServer(int port = 25)
            : this(IPAddress.Any, port)
        {
        }

        public EmbeddedSmtpServer(IPAddress address, int port = 25)
        {
            Address = address;
            Port = port;

            Listener = new TcpListener(Address, port);
        }

        public TcpListener Listener { get; private set; }
        public IPAddress Address { get; private set; }
        public int Port { get; private set; }

        public IEnumerable<MailMessage> ReceivedMessages()
        {
            return _messages;
        }

        public void Start()
        {
            Listener.Start();
            _stopped = false;
            SmtpLog.Info(string.Format("Server started at {0}", new IPEndPoint(Address, Port)));
            ListenForClients(OnClientConnect, e => SmtpLog.Error("Listener socket is closed", e));
        }

        public void Stop()
        {
            Dispose();
        }

        public void ListenForClients(Action<ISocket> callback, Action<Exception> error) {
            Listener.BeginAcceptSocket(ar => {
                if (_stopped)
                    return;
                SmtpLog.Info("accepted socket");
                ListenForClients(callback, error);
                Socket socket = Listener.EndAcceptSocket(ar);

                try {
                    callback(new SocketWrapper(socket));
                } catch (ThreadAbortException e) {
                    // ignore this one, we're just shutting down
                    SmtpLog.Debug("killing session thread");
                } catch (Exception e) {
                    error(e);
                }

                SmtpLog.Info("finished with socket");
            }, null);
        }

        public void OnClientConnect(ISocket clientSocket)
        {
            SmtpLog.Info("Client connected");

            var session = new SmtpSession(clientSocket)
            {
                OnMessage = (msg) => _messages.Add(msg)
            };
            _sessions.Add(session);
            session.Start();
        }

        public void Dispose()
        {
            SmtpLog.Info("stopping listener");
            _stopped = true;
            Listener.Stop();
            foreach (var session in _sessions) {
                session.Dispose();
            }
            SmtpLog.Info("stopped listener");
        }
    }
}