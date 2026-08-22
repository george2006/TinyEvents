IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes
    WHERE name = N'IX_TinyOutbox_ProcessedCleanup'
        AND object_id = OBJECT_ID(N'dbo.TinyOutbox')
)
BEGIN
    CREATE INDEX [IX_TinyOutbox_ProcessedCleanup]
    ON [dbo].[TinyOutbox]
    (
        Status,
        ProcessedAtUtc,
        Id
    );
END;
