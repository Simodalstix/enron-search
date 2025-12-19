namespace EnronSearch;

// Domain model representing an email inside the application.
// Used as the canonical structure for moving email data between layers
// (database, search, ranking, and presentation). This model is deliberately
// decoupled from CSV input and SQLite schema details to keep core logic stable
// and prevent raw storage formats from leaking into the rest of the system.

public class Email
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Sender { get; set; } = string.Empty;
    public string Recipients { get; set; } = string.Empty;
    public string DateSent { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}