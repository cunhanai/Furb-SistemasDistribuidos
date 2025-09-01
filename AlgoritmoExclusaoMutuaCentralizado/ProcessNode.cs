using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AlgoritmoExclusaoMutuaCentralizado
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
        private Thread _listenThreadRequestResource;
        private Thread _listenThreadConsumoResource;
        private Thread _listenThreadQueue;
        private Thread _listenThreadCoordinatorAlive;

        private List<int> _nodesSent = new();

        private bool? _coordVerified = null;

        private List<int> _accessQueue = new();
        private int resourceBeingAccessedById = 0;
        private bool? ConsumingResource = null;

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

            VerifyCoordinator();
            Console.WriteLine($"[P{Id}] Escutando na porta {Port}");
        }

        private void InitThreads()
        {
            _listenThreadRequestResource = new Thread(ThreadResource);
            _listenThreadRequestResource.Start();
            _listenThreadConsumoResource = new Thread(ThreadConsumo);
            _listenThreadConsumoResource.Start();
            _listenThreadCoordinatorAlive = new Thread(ThreadAlive);
            _listenThreadCoordinatorAlive.Start();
        }

        private void ThreadResource()
        {
            while (true)
            {
                if (!ConsumingResource.HasValue)
                {
                    var random = new Random();
                    var waitTime = random.Next(10 * 1000, 25 * 1000);

                    Console.WriteLine($"Tempo de espera para consumo: {waitTime}ms");
                    Task.Delay(waitTime).Wait();
                    ConsumingResource = false;
                    Console.WriteLine("Enviando requisição de consumo");
                    Send(CoordinatorId, $"REQUEST|{Id}");
                }
            }
        }

        private void ThreadConsumo()
        {
            while (true)
            {
                if (ConsumingResource.HasValue && ConsumingResource.Value)
                {
                    Console.WriteLine("Usando o recurso...");
                    var random = new Random();
                    var waitTime = random.Next(5 * 1000, 15 * 1000);

                    Task.Delay(waitTime).Wait();
                    ConsumingResource = null;
                    Send(CoordinatorId, $"FREE|{Id}");
                    Console.WriteLine($"Liberando o recurso... Tempo total de consumo: {waitTime}ms");
                }
            }
        }

        private void ThreadFila()
        {
            while (true)
            {
                if (_accessQueue.Count != 0 && resourceBeingAccessedById == 0)
                {
                    resourceBeingAccessedById = _accessQueue.First();
                    _accessQueue.Remove(resourceBeingAccessedById);

                    Send(resourceBeingAccessedById, $"USE|{Id}");
                    Console.WriteLine($"Processo P{resourceBeingAccessedById} está acessando o recurso");
                }
            }
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
                        Nodes.Add(senderId, senderId);
                        Console.WriteLine($"[P{Id}] Recebeu sinal de P{senderId} para informar o coordenador.");
                        Send(senderId, $"INFORM|{CoordinatorId}");
                        break;
                    case "INFORM":
                        CoordinatorId = int.Parse(parts[1]);
                        _coordVerified = true;
                        Console.WriteLine($"Coordenador atual: {CoordinatorId}");
                        InitThreads();
                        break;
                    case "REQUEST":
                        _accessQueue.Add(senderId);
                        Console.WriteLine($"[P{Id}] Adicionado a fila de espera para consumo do recurso.");
                        break;
                    case "USE":
                        ConsumingResource = true;
                        break;
                    case "FREE":
                        Console.WriteLine("Consumo liberado.");
                        resourceBeingAccessedById = 0;
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

            _listenThreadQueue = new Thread(ThreadFila);
            _listenThreadQueue.Start();

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
