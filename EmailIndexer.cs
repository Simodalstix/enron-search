using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EnronSearch;

public class EmailIndexer
{
    private readonly DatabaseManager _dbManager = new();

    public async Task IndexCsvAsync(string csvPath)
    {
        Console.WriteLine("Initializing database...");
        await _dbManager.InitializeAsync();

        Console.WriteLine($"Starting CSV indexing from: {csvPath}");
        
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();

        int processed = 0;
        using var reader = new StreamReader(csvPath);
        
        // Skip header
        await reader.ReadLineAsync();
        
        string line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            try
            {
                var email = ParseCsvLine(line);
                var emailId = await InsertEmail(connection, email);
                if (emailId > 0)
                {
                    await IndexEmailTerms(connection, emailId, email);
                }
                processed++;
                
                if (processed % 1000 == 0)
                    Console.WriteLine($"Processed {processed} emails");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing line {processed + 1}: {ex.Message}");
            }
        }

        Console.WriteLine($"CSV indexing complete. Processed {processed} emails.");
    }

    private Email ParseCsvLine(string csvLine)
    {
        // Parse: "file","message"
        var firstQuote = csvLine.IndexOf('"');
        var secondQuote = csvLine.IndexOf("\",\"");
        
        var filePath = csvLine.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
        var message = csvLine.Substring(secondQuote + 3, csvLine.Length - secondQuote - 4);
        
        // Extract basic fields from message
        var email = new Email { FilePath = filePath };
        
        var lines = message.Split(new[] { "\\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.StartsWith("Subject: "))
                email.Subject = line[9..].Trim();
            else if (line.StartsWith("From: "))
                email.Sender = line[6..].Trim();
            else if (line.StartsWith("To: "))
                email.Recipients = line[4..].Trim();
            else if (line.StartsWith("Date: "))
                email.DateSent = line[6..].Trim();
        }
        
        // Body is everything after headers (simplified)
        var bodyStart = message.IndexOf("\\n\\n");
        email.Body = bodyStart > 0 ? message.Substring(bodyStart + 4) : message;
        
        return email;
    }

    private async Task<int> InsertEmail(SqliteConnection connection, Email email)
    {
        var sql = @"INSERT OR IGNORE INTO emails (file_path, subject, sender, recipients, date_sent, body) 
                   VALUES (@filePath, @subject, @sender, @recipients, @dateSent, @body);
                   SELECT last_insert_rowid();";

        using var command = new SqliteCommand(sql, connection);
        command.Parameters.AddWithValue("@filePath", email.FilePath);
        command.Parameters.AddWithValue("@subject", email.Subject);
        command.Parameters.AddWithValue("@sender", email.Sender);
        command.Parameters.AddWithValue("@recipients", email.Recipients);
        command.Parameters.AddWithValue("@dateSent", email.DateSent);
        command.Parameters.AddWithValue("@body", email.Body);

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task IndexEmailTerms(SqliteConnection connection, int emailId, Email email)
    {
        var text = $"{email.Subject} {email.Body}".ToLowerInvariant();
        var terms = Regex.Split(text, @"\W+")
            .Where(t => t.Length > 2)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (term, frequency) in terms)
        {
            var sql = "INSERT OR REPLACE INTO term_index (term, email_id, frequency) VALUES (@term, @emailId, @frequency)";
            using var command = new SqliteCommand(sql, connection);
            command.Parameters.AddWithValue("@term", term);
            command.Parameters.AddWithValue("@emailId", emailId);
            command.Parameters.AddWithValue("@frequency", frequency);
            await command.ExecuteNonQueryAsync();
        }
    }
}