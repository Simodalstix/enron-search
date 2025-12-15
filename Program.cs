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
            Console.WriteLine("  enron-search index <csv-file> [--time <minutes>]");
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
        if (args.Length < 2 || args.Length > 4)
        {
            Console.WriteLine("Usage: enron-search index <csv-file> [--time <minutes>]");
            return 1;
        }

        var csvFile = args[1];
        int? timeLimit = null;
        
        // Parse optional --time argument
        for (int i = 2; i < args.Length; i += 2)
        {
            if (args[i] == "--time" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out int minutes) && minutes > 0)
                {
                    timeLimit = minutes;
                }
                else
                {
                    Console.WriteLine("Error: --time must be a positive integer (minutes)");
                    return 1;
                }
            }
        }

        var indexer = new EmailIndexer();
        await indexer.IndexCsvAsync(csvFile, timeLimit);
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
        Console.WriteLine("  enron-search index <csv-file> [--time <minutes>]");
        Console.WriteLine("  enron-search search <query>");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  enron-search index emails.csv --time 5    # Index for 5 minutes");
        Console.WriteLine("  enron-search index emails.csv             # Index full dataset");
        Console.WriteLine("  enron-search search \"fraud and investigation\"");
        return 1;
    }
}