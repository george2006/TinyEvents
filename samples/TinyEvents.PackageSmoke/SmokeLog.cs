namespace TinyEvents.PackageSmoke;

public sealed class SmokeLog
{
    public int EfCoreCount { get; private set; }

    public int AdoNetCount { get; private set; }

    public void RecordEfCore()
    {
        EfCoreCount++;
    }

    public void RecordAdoNet()
    {
        AdoNetCount++;
    }
}
