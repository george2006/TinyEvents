using TinyEvents.PackageSmoke;

var connectionString = PackageSmokeSettings.GetConnectionString();

await PackageSmokeDatabase.EnsureCreatedAsync(connectionString);
await EfCorePackageSmoke.RunAsync(connectionString);
await AdoNetPackageSmoke.RunAsync(connectionString);

Console.WriteLine("TinyEvents package smoke test passed.");
