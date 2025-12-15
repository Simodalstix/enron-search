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
        // Delete existing database for fresh start
        if (File.Exists("enron_search.db"))
        {
            File.Delete("enron_search.db");
            Console.WriteLine("Deleted existing database");
        }
        
        Console.WriteLine("Initializing database...");
        await _dbManager.InitializeAsync();

        Console.WriteLine($"Starting CSV indexing from: {csvPath}");
        
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();
        
        // Disable foreign key constraints for bulk import performance
        using var pragmaCommand = new SqliteCommand("PRAGMA foreign_keys = OFF", connection);
        await pragmaCommand.ExecuteNonQueryAsync();

        // Prepare statements once for reuse
        var emailInsertSql = @"INSERT INTO emails (file_path, subject, sender, recipients, date_sent, body) 
                              VALUES (@filePath, @subject, @sender, @recipients, @dateSent, @body)";
        var termInsertSql = "INSERT OR REPLACE INTO term_index (term, email_id, frequency) VALUES (@term, @emailId, @frequency)";
        
        using var emailCommand = new SqliteCommand(emailInsertSql, connection);
        using var termCommand = new SqliteCommand(termInsertSql, connection);
        
        // Add parameters to prepared statements
        emailCommand.Parameters.Add("@filePath", SqliteType.Text);
        emailCommand.Parameters.Add("@subject", SqliteType.Text);
        emailCommand.Parameters.Add("@sender", SqliteType.Text);
        emailCommand.Parameters.Add("@recipients", SqliteType.Text);
        emailCommand.Parameters.Add("@dateSent", SqliteType.Text);
        emailCommand.Parameters.Add("@body", SqliteType.Text);
        
        termCommand.Parameters.Add("@term", SqliteType.Text);
        termCommand.Parameters.Add("@emailId", SqliteType.Integer);
        termCommand.Parameters.Add("@frequency", SqliteType.Integer);

        int processed = 0;
        using var reader = new StreamReader(csvPath);
        
        // Skip header
        await reader.ReadLineAsync();
        
        var transaction = connection.BeginTransaction();
        
        string? line;
        while ((line = await reader.ReadLineAsync()) != null)
        {
            try
            {
                var email = ParseCsvLine(line);
                var emailId = await InsertEmailBatch(emailCommand, email);
                if (emailId > 0)
                {
                    await IndexEmailTermsBatch(termCommand, emailId, email);
                }
                processed++;
                
                // Commit transaction every 1000 emails
                if (processed % 1000 == 0)
                {
                    await transaction.CommitAsync();
                    transaction.Dispose();
                    transaction = connection.BeginTransaction();
                    Console.WriteLine($"Processed {processed} emails");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing line {processed + 1}: {ex.Message}");
            }
        }
        
        // Commit final batch
        await transaction.CommitAsync();
        transaction.Dispose();

        Console.WriteLine($"CSV indexing complete. Processed {processed} emails.");
    }

    private Email ParseCsvLine(string csvLine)
    {
        // Handle CSV with potential embedded quotes and newlines
        if (string.IsNullOrEmpty(csvLine) || !csvLine.StartsWith("\""))
        {
            return new Email { FilePath = "unknown", Body = csvLine };
        }

        // Find the end of the first quoted field (file path)
        int filePathEnd = csvLine.IndexOf("\",\"");
        if (filePathEnd == -1)
        {
            return new Email { FilePath = "unknown", Body = csvLine };
        }

        var filePath = csvLine.Substring(1, filePathEnd - 1);
        
        // Message starts after "," and ends with final quote
        var messageStart = filePathEnd + 3;
        var message = csvLine.Length > messageStart + 1 
            ? csvLine.Substring(messageStart, csvLine.Length - messageStart - 1)
            : "";
        
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

    private async Task<int> InsertEmailBatch(SqliteCommand command, Email email)
    {
        command.Parameters["@filePath"].Value = email.FilePath;
        command.Parameters["@subject"].Value = email.Subject;
        command.Parameters["@sender"].Value = email.Sender;
        command.Parameters["@recipients"].Value = email.Recipients;
        command.Parameters["@dateSent"].Value = email.DateSent;
        command.Parameters["@body"].Value = email.Body;

        await command.ExecuteNonQueryAsync();
        
        // Get the last inserted row ID
        using var idCommand = new SqliteCommand("SELECT last_insert_rowid()", command.Connection, command.Transaction);
        var result = await idCommand.ExecuteScalarAsync();
        return Convert.ToInt32(result);
    }

    private async Task IndexEmailTermsBatch(SqliteCommand command, int emailId, Email email)
    {
        var text = $"{email.Subject} {email.Body}".ToLowerInvariant();
        var terms = Regex.Split(text, @"\W+")
            .Where(t => t.Length > 2)
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var (term, frequency) in terms)
        {
            command.Parameters["@term"].Value = term;
            command.Parameters["@emailId"].Value = emailId;
            command.Parameters["@frequency"].Value = frequency;
            await command.ExecuteNonQueryAsync();
        }
    }
}