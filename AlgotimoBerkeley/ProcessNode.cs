using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AlgoritmoBerkeley
{
    internal class ProcessNode
    {
        public int Id { get; private set; }
        public int Port { get; private set; }
        public int CoordinatorId { get; private set; } = 0;
        public bool IsAlive { get; private set; }
        public Dictionary<int, int> Nodes { get; set; }

        private UdpClient _udpClient;
        private Thread _listenThread;
        private Thread _listenThreadCoordinatorAlive;

        private List<int> _nodesSent = new();

        private bool? _coordVerified = null;

        private bool isCoordinatorAlive = false;

        public ProcessNode(int id, int port, Dictionary<int, int> nodes)
        {
            Id = id;
            Port = port;
            Nodes = nodes;
        }

        public void Start()
        {
            _udpClient = new UdpClient(Port);
            _listenThread = new Thread(ThreadProcess);
            _listenThread.Start();

            Console.WriteLine($"[P{Id}] Escutando na porta {Port}");
            VerifyCoordinator();
        }

        private void InitThreads()
        {
            _listenThreadCoordinatorAlive = new Thread(ThreadAlive);
            _listenThreadCoordinatorAlive.Start();
        }

        private void ThreadAlive()
        {
            while (true)
            {
                isCoordinatorAlive = false;
                Send(CoordinatorId, $"ISALIVE|{Id}");

                Task.Delay(5 * 1000).GetAwaiter().GetResult();

                if (!isCoordinatorAlive)
                    StartElection();
            }
        }

        private void ThreadProcess()
        {
            IsAlive = true;

            while (true)
            {
                IPEndPoint remote = null;
                var data = _udpClient.Receive(ref remote);
                var message = Encoding.UTF8.GetString(data);

                var parts = message.Split('|');
                var type = parts[0];
                var senderId = int.Parse(parts[1]);

                var nodesIds = Nodes.Keys.Where(key => key != Id);

                switch (type)
                {
                    case "ELECTION":
                        Console.WriteLine($"[P{Id}] Recebeu eleição de P{senderId}.");

                        if (Id > senderId)
                        {
                            Send(senderId, $"OK|{Id}");
                            StartElection();
                        }
                        break;
                    case "ISALIVE":
                        Send(senderId, $"ALIVE|{Id}");
                        break;
                    case "ALIVE":
                        isCoordinatorAlive = true;
                        break;
                    case "OK":
                        Console.WriteLine($"[P{Id}] Recebeu OK de P{senderId}.");
                        _nodesSent.Remove(senderId);
                        break;
                    case "COORDINATOR":
                        CoordinatorId = senderId;
                        Console.WriteLine($"[P{Id}] Novo coordenador é P{senderId}.");
                        break;
                    case "VERIFY":
                        Console.WriteLine($"[P{Id}] Recebeu sinal de P{senderId} para informar o coordenador.");
                        Send(senderId, $"INFORM|{CoordinatorId}");
                        break;
                    case "INFORM":
                        CoordinatorId = int.Parse(parts[1]);
                        _coordVerified = true;
                        Console.WriteLine($"Coordenador atual: {CoordinatorId}");
                        InitThreads();
                        break;
                }
            }
        }

        public void VerifyCoordinator()
        {
            Console.WriteLine($"[P{Id}] Verificando Coordenador...");

            var nodes = Nodes.Where(w => w.Key != Id).ToList();

            if (nodes.Count == 0)
                AnnounceCoordinator();

            foreach (var node in nodes)
            {
                _coordVerified = false;
                Console.WriteLine("Perguntando quem é o coordenador...");
                Send(node.Key, $"VERIFY|{Id}");

                var waitingTask = Task.Run(() =>
                {
                    while (_coordVerified.HasValue && _coordVerified.Value)
                    {

                    }

                    return true;
                });

                waitingTask.Wait(20 * 1000);

                var result = waitingTask.Result;

                _coordVerified = null;
            }

        }

        public void StartElection()
        {
            Console.WriteLine($"[P{Id}] Iniciando eleição...");

            var lines = File.ReadAllLines("nodes.txt").ToList();
            var allNodes = new Dictionary<int, int>();

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var parts = line.Split(',');

                    if (int.Parse(parts[0]) != Id)
                        allNodes[int.Parse(parts[0])] = int.Parse(parts[1]);
                }
            }

            var higherNodes = Nodes.Where(item => item.Key > Id);

            _nodesSent = [.. higherNodes.ToDictionary().Keys];

            if (!higherNodes.Any())
            {
                AnnounceCoordinator();
                return;
            }

            foreach (var node in higherNodes)
                Send(node.Key, $"ELECTION|{Id}");

            var waitingTask = Task.Run(() =>
            {
                while (_nodesSent.Count != 0)
                {

                }

                return true;
            });

            waitingTask.Wait(5 * 1000);

            var result = waitingTask.Result;
        }

        private void AnnounceCoordinator()
        {
            CoordinatorId = Id;

            foreach (var node in Nodes.Keys.Where(key => key != Id))
                Send(node, $"COORDINATOR|{Id}");

            Console.WriteLine($"[P{Id}] Eu sou o novo coordenador!");

            _ = Task.Run(async () =>
            {
                await Task.Delay(1 * 60 * 1000);
                Process.GetCurrentProcess().Kill();
            });
        }

        private void Send(int targetId, string message)
        {
            if (!Nodes.ContainsKey(targetId))
                return;

            int targetPort = Nodes[targetId];

            using (UdpClient client = new UdpClient())
            {
                byte[] data = Encoding.UTF8.GetBytes(message);
                client.Send(data, data.Length, "127.0.0.1", targetPort);
            }
        }
    }
}
