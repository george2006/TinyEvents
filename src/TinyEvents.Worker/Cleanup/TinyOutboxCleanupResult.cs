namespace TinyEvents.Worker;

internal readonly record struct TinyOutboxCleanupResult(
    DateTimeOffset CutoffUtc,
    int DeletedCount);
