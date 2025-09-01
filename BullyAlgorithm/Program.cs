namespace BullyAlgorithm
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Uso: dotnet run <id> <porta> <arquivoConfig>");
                return;
            }

            var id = int.Parse(args[0]);
            var port = int.Parse(args[1]);
            var configPath = args[2];

            var lines = File.ReadAllLines(configPath);
            var allNodes = new Dictionary<int, int>();

            foreach (var line in lines)
            {
                var parts = line.Split(',');
                allNodes[int.Parse(parts[0])] = int.Parse(parts[1]);
            }

            var node = new ProcessNode(id, port, allNodes);
            node.Start();
        }
    }
}
