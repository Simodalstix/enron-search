using Microsoft.Data.Sqlite;
using System.Threading.Tasks;

namespace EnronSearch;

public class DatabaseManager
{
    private const string DatabasePath = "enron_search.db";
    private readonly string _connectionString;

    public DatabaseManager()
    {
        _connectionString = $"Data Source={DatabasePath}";
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var createTables = @"
            CREATE TABLE IF NOT EXISTS emails (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                file_path TEXT NOT NULL UNIQUE,
                subject TEXT,
                sender TEXT,
                recipients TEXT,
                date_sent TEXT,
                body TEXT
            );

            CREATE TABLE IF NOT EXISTS term_index (
                term TEXT NOT NULL,
                email_id INTEGER NOT NULL,
                frequency INTEGER NOT NULL,
                PRIMARY KEY (term, email_id),
                FOREIGN KEY (email_id) REFERENCES emails(id)
            );

            CREATE INDEX IF NOT EXISTS idx_term ON term_index(term);";

        using var command = new SqliteCommand(createTables, connection);
        await command.ExecuteNonQueryAsync();
    }

    public SqliteConnection CreateConnection()
    {
        return new SqliteConnection(_connectionString);
    }
}