using System;

namespace EnronSearch;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  enron-search index <path>");
            Console.WriteLine("  enron-search search <query>");
            return 1;
        }

        Console.WriteLine($"Command: {args[0]}");
        Console.WriteLine("Tool is working!");
        return 0;
    }
}