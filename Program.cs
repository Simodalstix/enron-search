using System;
using System.Threading.Tasks;

namespace EnronSearch;

class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage:");
            Console.WriteLine("  enron-search index <path>");
            Console.WriteLine("  enron-search search <query>");
            return 1;
        }

        try
        {
            return args[0] switch
            {
                "index" => await HandleIndex(args),
                "search" => await HandleSearch(args),
                _ => ShowUsage()
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> HandleIndex(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: enron-search index <csv-file>");
            return 1;
        }

        var indexer = new EmailIndexer();
        await indexer.IndexCsvAsync(args[1]);
        return 0;
    }

    static async Task<int> HandleSearch(string[] args)
    {
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: enron-search search <query>");
            return 1;
        }

        var searcher = new EmailSearcher();
        await searcher.SearchAsync(args[1]);
        return 0;
    }

    static int ShowUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  enron-search index <csv-file>");
        Console.WriteLine("  enron-search search <query>");
        return 1;
    }
}