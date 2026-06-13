using System.Data.Common;

namespace TinyEvents.Sample.PostgreSql.AdoNet.Infrastructure;

internal static class DbCommandExtensions
{
    public static void AddParameter(
        this DbCommand command,
        string name,
        object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
