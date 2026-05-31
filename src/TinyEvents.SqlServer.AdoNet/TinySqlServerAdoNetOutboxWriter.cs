namespace TinyEvents.SqlServer.AdoNet;

public sealed class TinySqlServerAdoNetOutboxWriter : ITinyOutboxWriter
{
    private readonly TinyEventsSqlServerAdoNetOptions options;
    private readonly IServiceProvider serviceProvider;
    private readonly TinySqlServerAdoNetTableName tableName;

    public TinySqlServerAdoNetOutboxWriter(
        TinyEventsSqlServerAdoNetOptions options,
        IServiceProvider serviceProvider)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (serviceProvider is null)
        {
            throw new ArgumentNullException(nameof(serviceProvider));
        }

        this.options = options;
        this.serviceProvider = serviceProvider;
        tableName = TinySqlServerAdoNetTableName.Parse(options.TableName);
    }

    public async ValueTask AddAsync(
        TinyOutboxMessage message,
        CancellationToken cancellationToken)
    {
        if (message is null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var context = GetCurrentTransactionContext();

        await AddUsingTransactionAsync(message, context, cancellationToken);
    }

    private async ValueTask AddUsingTransactionAsync(
        TinyOutboxMessage message,
        ITinyAdoNetTransactionContext context,
        CancellationToken cancellationToken)
    {
        await using var command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = TinySqlServerAdoNetSql.Insert(tableName);

        TinySqlServerAdoNetCommandParameters.AddOutboxParameters(command, message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private ITinyAdoNetTransactionContext GetCurrentTransactionContext()
    {
        var context = options.GetCurrentTransaction(serviceProvider);

        if (context is not null)
        {
            return context;
        }

        throw new InvalidOperationException(
            "ADO.NET outbox publishing requires an application-owned DbConnection and DbTransaction. Configure UseCurrentTransaction(...) and call PublishAsync inside your application transaction.");
    }
}
