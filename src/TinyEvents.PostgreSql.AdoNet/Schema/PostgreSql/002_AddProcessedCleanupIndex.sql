CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_ProcessedCleanup"
ON "public"."TinyOutbox"
(
    "Status",
    "ProcessedAtUtc",
    "Id"
);
