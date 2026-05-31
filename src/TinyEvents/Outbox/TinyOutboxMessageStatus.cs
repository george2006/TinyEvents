namespace TinyEvents;

public enum TinyOutboxMessageStatus
{
    Pending,
    Processing,
    Processed,
    Failed
}

