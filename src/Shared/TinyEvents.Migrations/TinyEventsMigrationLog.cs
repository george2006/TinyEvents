using Microsoft.Extensions.Logging;

namespace TinyEvents.Migrations;

internal static partial class TinyEventsMigrationLog
{
    [LoggerMessage(
        EventId = 1400,
        EventName = "MigrationStarted",
        Level = LogLevel.Information,
        Message = "TinyEvents migration started for {Provider} outbox {Schema}.{Table}.")]
    internal static partial void Started(
        ILogger logger,
        string provider,
        string schema,
        string table);

    [LoggerMessage(
        EventId = 1401,
        EventName = "MigrationApplied",
        Level = LogLevel.Information,
        Message = "TinyEvents migration {MigrationVersion} ({MigrationName}) applied for {Provider} outbox {Schema}.{Table}. Baseline: {IsBaseline}.")]
    internal static partial void Applied(
        ILogger logger,
        string provider,
        string schema,
        string table,
        long migrationVersion,
        string migrationName,
        bool isBaseline);

    [LoggerMessage(
        EventId = 1402,
        EventName = "MigrationSchemaCurrent",
        Level = LogLevel.Information,
        Message = "TinyEvents schema is current at version {CurrentVersion} of {TargetVersion} for {Provider} outbox {Schema}.{Table}.")]
    internal static partial void SchemaCurrent(
        ILogger logger,
        string provider,
        string schema,
        string table,
        long currentVersion,
        long targetVersion);

    [LoggerMessage(
        EventId = 1403,
        EventName = "MigrationCompleted",
        Level = LogLevel.Information,
        Message = "TinyEvents migration completed for {Provider} outbox {Schema}.{Table} from version {PreviousVersion} to {TargetVersion}. Applied {AppliedCount} migrations in {ElapsedMilliseconds} ms.")]
    internal static partial void Completed(
        ILogger logger,
        string provider,
        string schema,
        string table,
        long previousVersion,
        long targetVersion,
        int appliedCount,
        double elapsedMilliseconds);

    [LoggerMessage(
        EventId = 1404,
        EventName = "MigrationFailed",
        Level = LogLevel.Error,
        Message = "TinyEvents migration failed for {Provider} outbox {Schema}.{Table} after {ElapsedMilliseconds} ms.")]
    internal static partial void Failed(
        ILogger logger,
        string provider,
        string schema,
        string table,
        double elapsedMilliseconds,
        Exception exception);
}
