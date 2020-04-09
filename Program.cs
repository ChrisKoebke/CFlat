using CFlat.Backends;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CFlat
{
    class Program
    {
        static IBackend _backend;
        static Parser _parser = new Parser();
        static StringBuilder _input = new StringBuilder();

        static string _directory;
        static Queue<string> _sourceFileQueue = new Queue<string>();
        static AutoResetEvent _waitHandle = new AutoResetEvent(false);

        static DateTime _earliestReloadTime;

        static void Main(string[] args)
        {
            Console.WriteLine("Loading backend...");

            ArenaAllocator.Init();
            _backend = Backend.Create<CSharpBackend>();
            _directory = args[0];

            var watcher = new FileSystemWatcher(_directory, "*.cb");
            watcher.Changed += OnFileChanged;
            watcher.IncludeSubdirectories = true;
            watcher.EnableRaisingEvents = true;

            PrintHeader("[LIVE]");
            Console.WriteLine("Waiting for input...");
            Console.WriteLine("Refresh a file in the live directory to trigger compilation.");
            
            while (true)
            {
                _waitHandle.WaitOne();

                while (_sourceFileQueue.Count > 0)
                {
                    ArenaAllocator.Free();
                    try
                    {
                        Run(_sourceFileQueue.Dequeue());
                    }
                    catch (OutOfMemoryException ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }

        static void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (DateTime.Now < _earliestReloadTime || _sourceFileQueue.Contains(e.FullPath))
                return;

            Thread.Sleep(150);

            _earliestReloadTime = DateTime.Now + TimeSpan.FromSeconds(1);
            _sourceFileQueue.Enqueue(e.FullPath);
            _waitHandle.Set();
        }

        static void PrintHeader(string title)
        {
            Console.Clear();
            Console.WriteLine("---------------------------");
            Console.WriteLine("| " + title.PadRight(23) + " |");
            Console.WriteLine("---------------------------");
            Console.WriteLine();
        }

        static void Run(string fileName)
        {
            _input.Clear();

            try
            {
                _input.Append(File.ReadAllText(fileName));
            }
            catch
            {
                Thread.Sleep(250);
                Run(fileName);
            }

            var tokens = Tokenizer.Tokenize(_input, 0, _input.Length, fileName);

            var tree = _parser.Parse(tokens, out var errors);
            var program = _backend.Compile(tree, errors);

            PrintHeader(Path.GetFileName(fileName));

            if (errors.Length == 0)
            {
                var main = program?.GetMethod("main");
                main?.Invoke(null, null);
            }
            else
            {
                Console.WriteLine(errors);
            }
        }
    }
}
