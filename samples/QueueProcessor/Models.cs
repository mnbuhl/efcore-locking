namespace QueueProcessor;

public class Job
{
    public int Id { get; set; }
    public string Payload { get; set; } = "";
    public JobStatus Status { get; set; } = JobStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public string? WorkerId { get; set; }
}

public enum JobStatus { Pending, Processing, Done }
