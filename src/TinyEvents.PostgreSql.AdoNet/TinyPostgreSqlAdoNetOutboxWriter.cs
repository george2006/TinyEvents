namespace TinyEvents.PostgreSql.AdoNet;

public sealed class TinyPostgreSqlAdoNetOutboxWriter : ITinyOutboxWriter
{
    private readonly TinyEventsPostgreSqlAdoNetOptions options;
    private readonly IServiceProvider serviceProvider;
    private readonly TinyPostgreSqlAdoNetTableName tableName;

    public TinyPostgreSqlAdoNetOutboxWriter(
        TinyEventsPostgreSqlAdoNetOptions options,
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
        tableName = TinyPostgreSqlAdoNetTableName.Parse(options.TableName);
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
        ITinyPostgreSqlAdoNetTransactionContext context,
        CancellationToken cancellationToken)
    {
        await using var command = context.Connection.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = TinyPostgreSqlAdoNetSql.Insert(tableName);

        TinyPostgreSqlAdoNetCommandParameters.AddOutboxParameters(command, message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private ITinyPostgreSqlAdoNetTransactionContext GetCurrentTransactionContext()
    {
        var context = options.GetCurrentTransaction(serviceProvider);

        if (context is not null)
        {
            return context;
        }

        throw new InvalidOperationException(
            "PostgreSQL ADO.NET outbox publishing requires an application-owned DbConnection and DbTransaction. Configure UseCurrentTransaction(...) and call PublishAsync inside your application transaction.");
    }
}
