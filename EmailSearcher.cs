using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EnronSearch;

public class EmailSearcher
{
    private readonly DatabaseManager _dbManager = new();

    public async Task SearchAsync(string query)
    {
        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();

        var results = await SearchEmails(connection, terms);
        
        if (!results.Any())
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine($"Found {results.Count} results:\n");
        
        foreach (var (email, score) in results.Take(10))
        {
            Console.WriteLine($"Score: {score:F2}");
            Console.WriteLine($"From: {email.Sender}");
            Console.WriteLine($"Subject: {email.Subject}");
            Console.WriteLine($"File: {Path.GetFileName(email.FilePath)}");
            
            var snippet = CreateSnippet(email.Body, terms);
            Console.WriteLine($"Snippet: {snippet}");
            Console.WriteLine(new string('-', 50));
        }
    }

    private async Task<List<(Email email, double score)>> SearchEmails(SqliteConnection connection, string[] terms)
    {
        var sql = @"
            SELECT e.*, SUM(ti.frequency) as total_score
            FROM emails e
            JOIN term_index ti ON e.id = ti.email_id
            WHERE ti.term IN (" + string.Join(",", terms.Select((_, i) => $"@term{i}")) + @")
            GROUP BY e.id
            ORDER BY total_score DESC";

        using var command = new SqliteCommand(sql, connection);
        for (int i = 0; i < terms.Length; i++)
        {
            command.Parameters.AddWithValue($"@term{i}", terms[i]);
        }

        var results = new List<(Email, double)>();

using var reader = await command.ExecuteReaderAsync();

// Resolve column ordinals once
var idOrd = reader.GetOrdinal("id");
var filePathOrd = reader.GetOrdinal("file_path");
var subjectOrd = reader.GetOrdinal("subject");
var senderOrd = reader.GetOrdinal("sender");
var recipientsOrd = reader.GetOrdinal("recipients");
var dateSentOrd = reader.GetOrdinal("date_sent");
var bodyOrd = reader.GetOrdinal("body");
var scoreOrd = reader.GetOrdinal("total_score");

while (await reader.ReadAsync())
{
    var email = new Email
    {
        Id = reader.GetInt32(idOrd),
        FilePath = reader.GetString(filePathOrd),
        Subject = reader.IsDBNull(subjectOrd) ? string.Empty : reader.GetString(subjectOrd),
        Sender = reader.IsDBNull(senderOrd) ? string.Empty : reader.GetString(senderOrd),
        Recipients = reader.IsDBNull(recipientsOrd) ? string.Empty : reader.GetString(recipientsOrd),
        DateSent = reader.IsDBNull(dateSentOrd) ? string.Empty : reader.GetString(dateSentOrd),
        Body = reader.IsDBNull(bodyOrd) ? string.Empty : reader.GetString(bodyOrd)
    };

    var score = reader.GetDouble(scoreOrd);
    results.Add((email, score));
}

return results;

    }

    private string CreateSnippet(string body, string[] terms)
    {
        var words = body.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        for (int i = 0; i < words.Length; i++)
        {
            if (terms.Any(term => words[i].ToLowerInvariant().Contains(term)))
            {
                var start = Math.Max(0, i - 10);
                var end = Math.Min(words.Length, i + 10);
                return string.Join(" ", words[start..end]) + "...";
            }
        }
        
        return words.Length > 20 ? string.Join(" ", words[..20]) + "..." : body;
    }
}