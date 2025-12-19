namespace EnronSearch;

// Lightweight Data Transfer Object used only during CSV ingestion.
// Maps directly to the CSV column structure for parsing with CsvHelper,
// then converted into the domain Email model. This class exists solely
// to isolate CSV-specific concerns from the rest of the application.

public class CsvRecord
{
    public string file { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
}