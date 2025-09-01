using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BullyAlgorithm
{
    internal class ProcessNode
    {
        public int Id { get; private set; }
        public int Port { get; private set; }
        public int CoordinatorId { get; private set; }
        public bool IsAlive { get; private set; }
        public Dictionary<int, int> Nodes { get; set; }

        private UdpClient _udpClient;
        private Thread _listenThread;

        private List<int> _nodesSent = new();

        public ProcessNode(int id, int port, Dictionary<int, int> nodes)
        {
            Id = id;
            Port = port;
            Nodes = nodes;
            CoordinatorId = nodes.Keys.Max();
        }

        public void Start()
        {
            _udpClient = new UdpClient(Port);
            _listenThread = new Thread(ThreadProcess);
            _listenThread.Start();

            Console.WriteLine($"[P{Id}] Escutando na porta {Port} | Coordenador atual: {CoordinatorId}");

            while (true)
            {
                Console.Write("Comando (e = eleição, q = sair): ");

                var cmd = Console.ReadLine();
                
                if (cmd == "e") 
                    StartElection();
                else if (cmd == "q") 
                    break;
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
                        Console.WriteLine($"[P{Id}] Recebeu eleição de P{senderId}");

                        if (Id > senderId)
                        {
                            Send(senderId, $"OK|{Id}");
                            StartElection();
                        }
                        break;
                    case "OK":
                        Console.WriteLine($"[P{Id}] Recebeu OK de P{senderId}");
                        _nodesSent.Remove(senderId);
                        break;
                    case "COORDINATOR":
                        CoordinatorId = senderId;
                        Console.WriteLine($"[P{Id}] Novo coordenador é P{senderId}");
                        break;
                }
            }
        }

        public void StartElection()
        {
            Console.WriteLine($"[P{Id}] Iniciando eleição...");

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
