using System.Data.Common;

namespace TinyEvents.PostgreSql.AdoNet;

internal static class TinyPostgreSqlAdoNetCommandParameters
{
    public static void AddOutboxParameters(DbCommand command, TinyOutboxMessage message)
    {
        Add(command, "@Id", message.Id);
        Add(command, "@EventType", message.EventType);
        Add(command, "@Payload", message.Payload);
        Add(command, "@Status", (int)message.Status);
        Add(command, "@AttemptCount", message.AttemptCount);
        Add(command, "@ClaimedBy", message.ClaimedBy);
        Add(command, "@ClaimedAtUtc", message.ClaimedAtUtc);
        Add(command, "@ClaimExpiresAtUtc", message.ClaimExpiresAtUtc);
        Add(command, "@CreatedAtUtc", message.CreatedAtUtc);
        Add(command, "@NextAttemptAtUtc", message.NextAttemptAtUtc);
        Add(command, "@ProcessedAtUtc", message.ProcessedAtUtc);
        Add(command, "@LastError", message.LastError);
    }

    public static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
