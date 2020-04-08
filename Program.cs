using CFlat.Backends;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace CFlat
{
    class Program
    {
        public static int R()
        {
            return 33;
        }

        static void Main(string[] args)
        {
            while (true)
            {
                var file = new StringBuilder(File.ReadAllText(args[0]));

                var watch = Stopwatch.StartNew();
                var tokens = Tokenizer.Tokenize(file, 0, file.Length);
                tokens.FileName = args[0];
                watch.Stop();
                Console.WriteLine("LEX: {0}ms", watch.Elapsed.TotalMilliseconds);

                watch = Stopwatch.StartNew();
                var parser = new Parser();
                var backend = new CSharp();
                var ast = parser.Parse(tokens, out var errors);
                watch.Stop();
                Console.WriteLine("PARSE: {0}ms", watch.Elapsed.TotalMilliseconds);

                watch = Stopwatch.StartNew();
                backend.Compile(ast);
                watch.Stop();
                Console.WriteLine("COMPILE: {0}ms", watch.Elapsed.TotalMilliseconds);

                if (errors.Length > 0)
                {
                    Console.WriteLine(errors);
                    Console.ReadLine();
                    return;
                }

                Console.WriteLine("---------------------------");
                Console.WriteLine();

                backend.Run();

                Console.ReadLine();
                Console.Clear();
                Console.ReadLine();
            }
        }
    }
}
