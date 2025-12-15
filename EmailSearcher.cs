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
        var (terms, operation) = ParseQuery(query);
        Console.WriteLine($"Searching for: {string.Join($" {operation} ", terms)}");
        
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();

        var results = await SearchEmails(connection, terms, operation);
        
        if (!results.Any())
        {
            Console.WriteLine("No results found.");
            return;
        }

        Console.WriteLine($"Found {results.Count} results:\n");
        
        var topResults = results.Take(10).ToList();
        foreach (var (email, score) in topResults)
        {
            Console.WriteLine($"Score: {score:F2}");
            Console.WriteLine($"From: {email.Sender}");
            Console.WriteLine($"Subject: {email.Subject}");
            Console.WriteLine($"File: {Path.GetFileName(email.FilePath)}");
            
            var snippet = CreateSnippet(email.Body, terms);
            Console.WriteLine($"Snippet: {snippet}");
            Console.WriteLine(new string('-', 50));
        }
        
        // Bonus: Show related emails (simplified for performance)
        if (topResults.Any() && topResults.Count >= 3)
        {
            Console.WriteLine("\n=== RELATED EMAILS (by sender) ===");
            var senders = topResults.Take(3).Select(r => r.email.Sender).Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
            if (senders.Any())
            {
                var related = await FindRelatedBySender(connection, senders, topResults.Select(r => r.email.Id).ToList());
                Console.WriteLine($"Found {related.Count} related emails");
                foreach (var (email, score) in related.Take(5))
                {
                    Console.WriteLine($"From: {email.Sender}");
                    Console.WriteLine($"Subject: {email.Subject}");
                    Console.WriteLine($"File: {Path.GetFileName(email.FilePath)}");
                    Console.WriteLine(new string('-', 30));
                }
            }
        }
    }

    private async Task<List<(Email email, double score)>> SearchEmails(SqliteConnection connection, string[] terms, string operation)
    {
        if (terms.Length == 0) return new List<(Email, double)>();
        
        string sql;
        if (terms.Length == 1)
        {
            // Single term - simple query
            sql = @"
                SELECT e.*, ti.frequency as total_score
                FROM emails e
                JOIN term_index ti ON e.id = ti.email_id
                WHERE ti.term = @term0
                ORDER BY total_score DESC";
        }
        else
        {
            // Multi-term query with AND/OR logic
            var termQueries = terms.Select((_, i) => $"SELECT email_id FROM term_index WHERE term = @term{i}").ToArray();
            var combinedQuery = string.Join(operation == "AND" ? " INTERSECT " : " UNION ", termQueries);
            
            sql = $@"
                SELECT e.*, SUM(ti.frequency) as total_score
                FROM emails e
                JOIN term_index ti ON e.id = ti.email_id
                WHERE e.id IN ({combinedQuery})
                AND ti.term IN (" + string.Join(",", terms.Select((_, i) => $"@term{i}")) + @")
                GROUP BY e.id
                ORDER BY total_score DESC";
        }

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
    
    private async Task<List<(Email email, double score)>> FindRelatedEmails(SqliteConnection connection, List<Email> seedEmails)
    {
        if (!seedEmails.Any()) return new List<(Email, double)>();
        
        // Find emails that share terms with the top results but weren't in original results
        var seedIds = seedEmails.Select(e => e.Id).ToList();
        
        var sql = @"
            SELECT e.*, COUNT(DISTINCT ti.term) as shared_terms
            FROM emails e
            JOIN term_index ti ON e.id = ti.email_id
            WHERE ti.term IN (
                SELECT DISTINCT ti2.term 
                FROM term_index ti2 
                WHERE ti2.email_id IN (" + string.Join(",", seedIds) + @")
            )
            AND e.id NOT IN (" + string.Join(",", seedIds) + @")
            GROUP BY e.id
            HAVING shared_terms >= 2
            ORDER BY shared_terms DESC
            LIMIT 20";
            
        using var command = new SqliteCommand(sql, connection);
        var results = new List<(Email, double)>();
        
        using var reader = await command.ExecuteReaderAsync();
        var idOrd = reader.GetOrdinal("id");
        var filePathOrd = reader.GetOrdinal("file_path");
        var subjectOrd = reader.GetOrdinal("subject");
        var senderOrd = reader.GetOrdinal("sender");
        var recipientsOrd = reader.GetOrdinal("recipients");
        var dateSentOrd = reader.GetOrdinal("date_sent");
        var bodyOrd = reader.GetOrdinal("body");
        var scoreOrd = reader.GetOrdinal("shared_terms");
        
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
    
    private async Task<List<(Email email, double score)>> FindRelatedBySender(SqliteConnection connection, List<string> senders, List<int> excludeIds)
    {
        var sql = @"
            SELECT e.*, 1.0 as score
            FROM emails e
            WHERE e.sender IN (" + string.Join(",", senders.Select((_, i) => $"@sender{i}")) + @")
            AND e.id NOT IN (" + string.Join(",", excludeIds) + @")
            ORDER BY e.id DESC
            LIMIT 10";
            
        using var command = new SqliteCommand(sql, connection);
        for (int i = 0; i < senders.Count; i++)
        {
            command.Parameters.AddWithValue($"@sender{i}", senders[i]);
        }
        
        var results = new List<(Email, double)>();
        using var reader = await command.ExecuteReaderAsync();
        
        var idOrd = reader.GetOrdinal("id");
        var filePathOrd = reader.GetOrdinal("file_path");
        var subjectOrd = reader.GetOrdinal("subject");
        var senderOrd = reader.GetOrdinal("sender");
        var recipientsOrd = reader.GetOrdinal("recipients");
        var dateSentOrd = reader.GetOrdinal("date_sent");
        var bodyOrd = reader.GetOrdinal("body");
        
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
            
            results.Add((email, 1.0));
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

    private (string[] terms, string operation) ParseQuery(string query)
    {
        var normalized = query.ToLowerInvariant();
        
        if (normalized.Contains(" and "))
        {
            var terms = normalized.Split(new[] { " and" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'')).Where(t => t.Length > 2).ToArray();
            return (terms, "AND");
        }
        
        if (normalized.Contains(" or "))
        {
            var terms = normalized.Split(new[] { " or" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'')).Where(t => t.Length > 2).ToArray();
            return (terms, "OR");
        }
        
        // Default: space-separated terms with OR logic
        var defaultTerms = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim('.', ',', ';', ':', '!', '?', '"', '\''))
            .Where(t => t.Length > 2).ToArray();
        return (defaultTerms, "OR");
    }
}