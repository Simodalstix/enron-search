using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EnronSearch;

public class EmailIndexer
{
    private readonly DatabaseManager _dbManager = new();

    public async Task IndexAsync(string emailPath)
    {
        Console.WriteLine("Initializing database...");
        await _dbManager.InitializeAsync();

        Console.WriteLine($"Starting indexing from: {emailPath}");
        
        var files = Directory.GetFiles(emailPath, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith('.'))
            .ToArray();

        Console.WriteLine($"Found {files.Length} files to process");

        int processed = 0;
        using var connection = _dbManager.CreateConnection();
        await connection.OpenAsync();

        foreach (var file in files)
        {
            try
            {
                await ProcessEmailFile(connection, file);
                processed++;
                
                if (processed % 100 == 0)
                    Console.WriteLine($"Processed {processed}/{files.Length} files");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing {file}: {ex.Message}");
            }
        }

        Console.WriteLine($"Indexing complete. Processed {processed} files.");
    }

    private async Task ProcessEmailFile(SqliteConnection connection, string filePath)
    {
        var content = await File.ReadAllTextAsync(filePath);
        var email = ParseEmail(content, filePath);
        
        var emailId = await InsertEmail(connection, email);
        await IndexEmailTerms(connection, emailId, email);
    }

    private Email ParseEmail(string content, string filePath)
    {
        var lines = content.Split('\n');
        var email = new Email { FilePath = filePath };
        
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

        email.Body = string.Join('\n', bodyLines);
        return email;
    }

    private async Task<int> InsertEmail(SqliteConnection connection, Email email)
    {
        var sql = @"INSERT INTO emails (file_path, subject, sender, recipients, date_sent, body) 
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