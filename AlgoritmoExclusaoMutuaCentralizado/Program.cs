using AlgoritmoExclusaoMutuaCentralizado;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;

namespace BullyAlgorithm
{
    internal class Program
    {
        static void Main(string[] args)
        {
            int id, port;
            string configPath;


            if (args.Length > 0)
            {
                id = int.Parse(args[0]);
                port = int.Parse(args[1]);
                configPath = args[2];
            }
            else
            {
                id = 5001;
                port = 5001;
                configPath = "nodes.txt";
                File.WriteAllLines(configPath, [string.Empty]);
            }

            var lines = File.ReadAllLines(configPath).ToList();
            var allNodes = new Dictionary<int, int>();

            lines.Add($"{id},{port}");

            File.WriteAllLines(configPath, lines);

            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    var parts = line.Split(',');
                    allNodes[int.Parse(parts[0])] = int.Parse(parts[1]);
                }
            }

            _ = Task.Run(async () =>
            {
                var random = new Random();
                int? newId = null;

                while (newId == null)
                {
                    newId = random.Next(5000, 5300);
                    var actualLines = File.ReadAllLines(configPath).ToList();

                    if (actualLines.Contains(newId.ToString()!))
                        newId = null;
                }

                await Task.Delay(40 * 1000);

                var path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var dllPath = Path.Combine(path, "AlgoritmoExclusaoMutuaCentralizado.exe");

                using Process process = new Process();
                process.StartInfo.FileName = dllPath;
                process.StartInfo.Arguments = $"{newId} {newId} {configPath}";
                process.StartInfo.UseShellExecute = true;
                process.Start();
            });

            var node = new ProcessNode(id, port, allNodes);
            node.Start();
        }
    }
}
