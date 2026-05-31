namespace TinyEvents.Sample.AdoNet.Consumers;

public sealed class WelcomeEmailLog
{
    private readonly List<string> emails = new List<string>();

    public int Count => emails.Count;

    public IReadOnlyList<string> Snapshot()
    {
        return emails.ToArray();
    }

    public void Record(string email)
    {
        emails.Add(email);
    }
}
