using System.Text.Json;
using Hearthcast.Data.Persistence;
using Hearthcast.HistoryImport;
using Microsoft.EntityFrameworkCore;
using Npgsql;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: Hearthcast.HistoryImport <history-v2.json> <summary-totals.json>\nSet HEARTHCAST_IMPORT_POSTGRES to the target connection string. Requires the existing schema and an empty application database.");
    return 2;
}
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_,e) => { e.Cancel=true; cancellation.Cancel(); };
try
{
    var connection = Environment.GetEnvironmentVariable("HEARTHCAST_IMPORT_POSTGRES")
        ?? throw new InvalidOperationException("Set HEARTHCAST_IMPORT_POSTGRES.");
    var target = new NpgsqlConnectionStringBuilder(connection);
    Console.WriteLine($"Import target: {target.Host}:{target.Port}/{target.Database}");
    var input = await HistoryFile.ReadAsync<HistoryFile>(args[0],cancellation.Token);
    var totals = await HistoryFile.ReadAsync<ExpectedTotals[]>(args[1],cancellation.Token);
    await using var context = new HearthcastDbContext(new DbContextOptionsBuilder<HearthcastDbContext>().UseNpgsql(connection).Options);
    var result = await HistoryLoader.ImportAsync(context,input,totals,cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions { WriteIndented=true }));
    Console.WriteLine("Import committed. Historical registry entries retain the checklist settings specified in the input.");
    return 0;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Import cancelled; the transaction was not committed.");
    return 1;
}
catch (Exception ex)
{
    // Connection strings and PostgreSQL detail fields can contain private data.
    Console.Error.WriteLine(ex is NpgsqlException || ex is DbUpdateException
        ? "Database import failed. No successful commit was acknowledged; check the database before retrying."
        : ex.Message);
    return 1;
}
