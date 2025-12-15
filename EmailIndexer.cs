using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CsvHelper;
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
        var startTime = DateTime.Now;
        
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();
        
        // Disable foreign key constraints for bulk import performance
        using var pragmaCommand = new SqliteCommand("PRAGMA foreign_keys = OFF", connection);
        await pragmaCommand.ExecuteNonQueryAsync();

        // Prepare statements once for reuse
        var emailInsertSql = @"INSERT OR IGNORE INTO emails (file_path, subject, sender, recipients, date_sent, body, content_hash) 
                              VALUES (@filePath, @subject, @sender, @recipients, @dateSent, @body, @contentHash)";
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
        emailCommand.Parameters.Add("@contentHash", SqliteType.Text);
        
        termCommand.Parameters.Add("@term", SqliteType.Text);
        termCommand.Parameters.Add("@emailId", SqliteType.Integer);
        termCommand.Parameters.Add("@frequency", SqliteType.Integer);

        int processed = 0;
        int skipped = 0;
        
        using var reader = new StreamReader(csvPath);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        
        var transaction = connection.BeginTransaction();
        emailCommand.Transaction = transaction;
        termCommand.Transaction = transaction;
        
        await foreach (var record in csv.GetRecordsAsync<CsvRecord>())
        {
            if (processed >= 500_000) break;
            
            try
            {
                var email = ParseCsvRecord(record);
                
                // Apply heuristics
                if (!ShouldIndexEmail(email))
                {
                    skipped++;
                    continue;
                }
                
                var emailId = await InsertEmailBatch(emailCommand, email);
                if (emailId > 0)
                {
                    await IndexEmailTermsBatch(termCommand, emailId, email);
                }
                processed++;
                
                // Commit transaction every 10000 records
                if (processed % 10000 == 0)
                {
                    await transaction.CommitAsync();
                    transaction.Dispose();
                    transaction = connection.BeginTransaction();
                    emailCommand.Transaction = transaction;
                    termCommand.Transaction = transaction;
                    Console.WriteLine($"Processed {processed:N0} records, skipped {skipped:N0}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing record {processed + 1}: {ex.Message}");
            }
        }
        
        // Commit final batch
        await transaction.CommitAsync();
        transaction.Dispose();

        var elapsed = DateTime.Now - startTime;
        Console.WriteLine($"CSV indexing complete. Processed {processed:N0} records, skipped {skipped:N0}.");
        Console.WriteLine($"Time elapsed: {elapsed.TotalMinutes:F1} minutes ({processed/elapsed.TotalSeconds:F0} records/sec)");
    }

    private Email ParseCsvRecord(CsvRecord csvRecord)
    {
        var email = new Email { FilePath = csvRecord.file };
        
        // Parse email headers from message content
        var lines = csvRecord.message.Split('\n');
        
        bool inHeaders = true;
        var bodyLines = new List<string>();
        
        foreach (var line in lines)
        {
            if (inHeaders && string.IsNullOrWhiteSpace(line))
            {
                inHeaders = false;
                continue;
            }
            
            if (inHeaders)
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
            else
            {
                bodyLines.Add(line);
            }
        }
        
        email.Body = string.Join(" ", bodyLines).Trim();
        return email;
    }

    private bool ShouldIndexEmail(Email email)
    {
        // B. Minimum Body Length Filter
        var normalizedBody = NormalizeText(email.Body);
        if (normalizedBody.Length < 50)
        {
            return false;
        }
        
        return true;
    }

    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        
        // A. Basic Text Normalization
        return Regex.Replace(text.ToLowerInvariant(), @"[.,:;!?""']", "")
                   .Trim()
                   .Replace("  ", " "); // Collapse multiple spaces
    }

    private string ComputeContentHash(Email email)
    {
        // C. Exact Duplicate Detection
        var content = NormalizeText(email.Subject) + "|" + NormalizeText(email.Body);
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes);
    }

    private async Task<int> InsertEmailBatch(SqliteCommand command, Email email)
    {
        var contentHash = ComputeContentHash(email);
        
        command.Parameters["@filePath"].Value = email.FilePath;
        command.Parameters["@subject"].Value = email.Subject;
        command.Parameters["@sender"].Value = email.Sender;
        command.Parameters["@recipients"].Value = email.Recipients;
        command.Parameters["@dateSent"].Value = email.DateSent;
        command.Parameters["@body"].Value = email.Body;
        command.Parameters["@contentHash"].Value = contentHash;

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