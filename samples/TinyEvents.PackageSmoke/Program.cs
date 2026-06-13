using TinyEvents.PackageSmoke;

var sqlServerConnectionString = PackageSmokeSettings.GetSqlServerConnectionString();
var postgreSqlConnectionString = PackageSmokeSettings.GetPostgreSqlConnectionString();

await PackageSmokeDatabase.EnsureCreatedAsync(sqlServerConnectionString);
await EfCorePackageSmoke.RunAsync(sqlServerConnectionString);
await AdoNetPackageSmoke.RunAsync(sqlServerConnectionString);

await PostgreSqlPackageSmokeDatabase.EnsureCreatedAsync(postgreSqlConnectionString);
await PostgreSqlEfCorePackageSmoke.RunAsync(postgreSqlConnectionString);
await PostgreSqlAdoNetPackageSmoke.RunAsync(postgreSqlConnectionString);

Console.WriteLine("TinyEvents package smoke test passed.");
