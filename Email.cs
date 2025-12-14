namespace EnronSearch;

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