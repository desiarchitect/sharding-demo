using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tadka.Samples.ShardedOrdersDemo.Migration;

[JsonConverter(typeof(JsonStringEnumConverter))]
enum LedgerPhase { InsertedOnTarget, Completed }

record LedgerEntry(string OrderId, int From, int To, LedgerPhase Phase, DateTimeOffset At);

/// <summary>
/// A local, append-only JSONL write-ahead log for the migration. Not a database
/// table on purpose — keeps the schema story to just `orders`, and it's the thing
/// that makes `reshard apply --resume` safe to run after a Ctrl-C: read the last
/// phase recorded per order, and only redo what wasn't finished.
///
/// Opens ONE file handle for the whole migration run (not per line) — both for
/// performance and because reopening thousands of times is what made a just-killed
/// prior run's handle occasionally still contend for the file on Windows.
/// </summary>
static class MigrationLedger
{
    static StreamWriter? _writer;

    public static void OpenForAppend()
    {
        Paths.EnsureStateDir();
        // A prior run that was just killed may hold this handle for a brief moment
        // longer on Windows — retry a few times rather than fail immediately.
        IOException? last = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var stream = new FileStream(Paths.MigrationLedgerFile, FileMode.Append, FileAccess.Write, FileShare.Read);
                _writer = new StreamWriter(stream) { AutoFlush = true };
                return;
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }
        throw last!;
    }

    public static void Close()
    {
        _writer?.Dispose();
        _writer = null;
    }

    public static void Append(LedgerEntry entry)
    {
        if (_writer is null) throw new InvalidOperationException("Ledger not open — call MigrationLedger.OpenForAppend() first.");
        _writer.WriteLine(JsonSerializer.Serialize(entry));
    }

    public static List<LedgerEntry> ReadAll()
    {
        if (!File.Exists(Paths.MigrationLedgerFile)) return new();
        var entries = new List<LedgerEntry>();
        // Read-only open, shared - safe to call even while a migration has the
        // file open for append (used by `report`/inspection while apply runs).
        using var stream = new FileStream(Paths.MigrationLedgerFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var entry = JsonSerializer.Deserialize<LedgerEntry>(line);
            if (entry is not null) entries.Add(entry);
        }
        return entries;
    }

    /// <summary>The most recent ledger entry per order id — what `--resume` reads
    /// to decide what's already done.</summary>
    public static Dictionary<string, LedgerEntry> LatestByOrderId()
    {
        var latest = new Dictionary<string, LedgerEntry>();
        foreach (var entry in ReadAll())
            latest[entry.OrderId] = entry; // JSONL is append-only in order, so last write wins
        return latest;
    }
}
