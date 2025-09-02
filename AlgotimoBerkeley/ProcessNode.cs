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
        public int MasterId { get; private set; } = 0;
        public Dictionary<int, int> Nodes { get; set; }
        public DateTime HoraAtual { get; set; }

        private UdpClient _udpClient;
        private Thread _listenThread;
        private Thread _listenThreadSync;
        private Thread _listenThreadMasterAlive;

        private List<int> _nodesSentElection = [];
        private Dictionary<int, long?> _syncNodes = [];

        private bool? _masterVerified = null;

        private bool isMasterAlive = false;

        public ProcessNode(int id, int port, Dictionary<int, int> nodes, DateTime horaAtual)
        {
            Id = id;
            Port = port;
            Nodes = nodes;
            HoraAtual = horaAtual;
        }

        public void Start()
        {
            _udpClient = new UdpClient(Port);
            _listenThread = new Thread(ThreadProcess);
            _listenThread.Start();

            Console.WriteLine($"[P{Id}] Escutando na porta {Port} | Hora atual: {HoraAtual}");

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

        private void InitThreads()
        {
            _listenThreadMasterAlive = new Thread(ThreadAlive);
            _listenThreadMasterAlive.Start();
        }

        private void ThreadAlive()
        {
            while (true)
            {
                if (MasterId > 0)
                {
                    Task.Delay(60 * 1000).GetAwaiter().GetResult();

                    isMasterAlive = false;
                    Send(MasterId, $"ISALIVE|{Id}");

                    if (MasterId > 0 && !isMasterAlive)
                        StartElection();
                }
            }
        }

        private void ThreadSync()
        {
            while (_syncNodes.Count <= 0 || (_syncNodes.Count > 0 && _syncNodes.ContainsValue(null)))
            {
            }

            var media = _syncNodes.Values.Sum(s => s!.Value) / (_syncNodes.Count + 1);

            HoraAtual = HoraAtual.AddTicks(media);
            var minutos = TimeSpan.FromTicks(media).TotalMinutes;

            Console.WriteLine($"Atualizando hora em: {minutos} minutos | Hora Atual: {HoraAtual}");

            foreach (var node in _syncNodes)
            {
                var atualizarTicks = node.Value * (-1) + media;

                Send(node.Key, $"SYNC|{Id}|{atualizarTicks}");
            }

            _syncNodes.Clear();
        }

        private void ThreadProcess()
        {
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
                        ProcessarInicioEleicao(senderId);
                        break;
                    case "ISALIVE":
                        MasterInformarVivo(senderId);
                        break;
                    case "ALIVE":
                        SlaveRegistrarMasterVivo();
                        break;
                    case "OK":
                        ReceberOkEleicao(senderId);
                        break;
                    case "MASTER":
                        SlavesDefinindoMaster(senderId);
                        break;
                    case "VERIFY":
                        EnviandoIdDoMaster(senderId);
                        break;
                    case "INFORM":
                        SlaveRecebendoMasterAtual(parts);
                        break;
                    case "SYNCINF":
                        SlaveInformarHoraParaSincronizacao(parts, senderId);
                        break;
                    case "SYNCOUT":
                        MasterProcessarHorasParaSincronizacao(parts, senderId);
                        break;
                    case "SYNC":
                        SlaveSincronizarHora(parts);
                        break;
                }
            }
        }

        private void SlaveSincronizarHora(string[] parts)
        {
            var ticksSync = long.Parse(parts[2]);
            var minutos = TimeSpan.FromTicks(ticksSync).TotalMinutes;
            Console.WriteLine($"[P{Id}] Deve sincronizar a hora em: {minutos} minutos");
            HoraAtual = HoraAtual.AddTicks(ticksSync);
            Console.WriteLine($"[P{Id}] Nova hora: {HoraAtual}");
        }

        private void MasterProcessarHorasParaSincronizacao(string[] parts, int senderId)
        {
            var ticks = long.Parse(parts[2]);
            var minutos = TimeSpan.FromTicks(ticks).TotalMinutes;
            Console.WriteLine($"[P{Id}] P{senderId} - Diferença de: {minutos} minutos");

            _syncNodes[senderId] = ticks;
        }

        private void SlaveInformarHoraParaSincronizacao(string[] parts, int senderId)
        {
            var dateTime = DateTime.FromFileTimeUtc(long.Parse(parts[2]));
            Console.WriteLine($"[P{Id}] Recebeu solicitaçãod e P{senderId} para informar a hora");

            var diferenca = HoraAtual - dateTime;
            Send(senderId, $"SYNCOUT|{Id}|{diferenca.Ticks}");
        }

        private void SlaveRecebendoMasterAtual(string[] parts)
        {
            MasterId = int.Parse(parts[1]);
            _masterVerified = true;

            Console.WriteLine($"Master atual: {(MasterId > 0 ? MasterId : "Sem master definido")}");

            if (MasterId <= 0)
                StartElection();

            InitThreads();
        }

        private void EnviandoIdDoMaster(int senderId)
        {
            Console.WriteLine($"[P{Id}] Recebeu sinal de P{senderId} para informar o master.");
            Send(senderId, $"INFORM|{MasterId}");
        }

        private void SlavesDefinindoMaster(int senderId)
        {
            MasterId = senderId;
            Console.WriteLine($"[P{Id}] Novo master é P{senderId}.");
        }

        private void ReceberOkEleicao(int senderId)
        {
            Console.WriteLine($"[P{Id}] Recebeu OK de P{senderId}.");
            _nodesSentElection.Remove(senderId);
        }

        private void SlaveRegistrarMasterVivo()
        {
            isMasterAlive = true;
        }

        private void MasterInformarVivo(int senderId)
        {
            Send(senderId, $"ALIVE|{Id}");
        }

        private void ProcessarInicioEleicao(int senderId)
        {
            Console.WriteLine($"[P{Id}] Recebeu eleição de P{senderId}.");

            if (Id > senderId)
            {
                Send(senderId, $"OK|{Id}");
                StartElection();
            }
        }

        public void VerifyWhoIsMaster()
        {
            Console.WriteLine($"[P{Id}] Verificando Master...");

            var nodes = Nodes.Where(w => w.Key != Id).ToList();

            if (nodes.Count == 0)
                AnnounceMaster();

            foreach (var node in nodes)
            {
                _masterVerified = false;
                Console.WriteLine("Perguntando quem é o master...");
                Send(node.Key, $"VERIFY|{Id}");

                var waitingTask = Task.Run(() =>
                {
                    while (_masterVerified.HasValue && _masterVerified.Value)
                    {

                    }

                    return true;
                });

                waitingTask.Wait(20 * 1000);

                var result = waitingTask.Result;

                _masterVerified = null;
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

            _nodesSentElection = [.. higherNodes.ToDictionary().Keys];

            if (!higherNodes.Any())
            {
                AnnounceMaster();
                return;
            }

            foreach (var node in higherNodes)
                Send(node.Key, $"ELECTION|{Id}");

            var waitingTask = Task.Run(() =>
            {
                while (_nodesSentElection.Count != 0)
                {

                }

                return true;
            });

            waitingTask.Wait(5 * 1000);

            var result = waitingTask.Result;
        }

        private void AnnounceMaster()
        {
            MasterId = Id;

            _syncNodes.Clear();
            foreach (var node in Nodes.Keys.Where(key => key != Id))
            {
                Send(node, $"MASTER|{Id}");

                Console.WriteLine($"Iniciando sincronização das horas com o P{node}");
                _syncNodes.Add(node, null);
                Send(node, $"SYNCINF|{Id}|{HoraAtual.ToFileTimeUtc()}");
            }

            _listenThreadSync = new Thread(ThreadSync);
            _listenThreadSync.Start();

            Console.WriteLine($"[P{Id}] Eu sou o novo master!");
        }

        private void Send(int targetId, string message)
        {
            if (!Nodes.TryGetValue(targetId, out int targetPort))
                return;

            using UdpClient client = new();
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, "127.0.0.1", targetPort);
        }
    }
}
