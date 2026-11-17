namespace Hearthcast.Data.Tests.Spending;

internal static class SpendingSetupTestText
{
    internal static string Key(int length)
    {
        var random = new Random(1729);
        const string alphabet = "abcdefghijklmnopqrstuvwxyz0123456789";
        return new string(Enumerable.Range(0, length).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
    }
}
