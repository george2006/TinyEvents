CREATE SCHEMA IF NOT EXISTS "public";

CREATE TABLE IF NOT EXISTS "public"."TinyOutbox"
(
    "Id" uuid NOT NULL CONSTRAINT "PK_TinyOutbox" PRIMARY KEY,
    "EventType" text NOT NULL,
    "Payload" text NOT NULL,
    "Status" integer NOT NULL,
    "AttemptCount" integer NOT NULL,
    "ClaimedBy" text NULL,
    "ClaimedAtUtc" timestamp with time zone NULL,
    "ClaimExpiresAtUtc" timestamp with time zone NULL,
    "CreatedAtUtc" timestamp with time zone NOT NULL,
    "NextAttemptAtUtc" timestamp with time zone NULL,
    "ProcessedAtUtc" timestamp with time zone NULL,
    "LastError" text NULL
);

CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_Pending"
ON "public"."TinyOutbox"
(
    "Status",
    "NextAttemptAtUtc",
    "CreatedAtUtc"
);

CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_ExpiredProcessing"
ON "public"."TinyOutbox"
(
    "Status",
    "ClaimExpiresAtUtc"
);

CREATE INDEX IF NOT EXISTS "IX_TinyOutbox_ClaimedBy"
ON "public"."TinyOutbox"
(
    "ClaimedBy",
    "Status"
);
