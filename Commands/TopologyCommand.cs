namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class TopologyCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        if (!args.Has("watch"))
        {
            await PrintOnce(state);
            return;
        }

        // Single-terminal live dashboard - a lighter alternative to running one
        // `watch --shard N` per shard: all shards' counts ticking up together,
        // refreshed in place. Run `stream` (or `insert`/`seed`) in another
        // terminal to feed it.
        int intervalMs = args.GetInt("interval-ms", 1000);
        Console.WriteLine("Watching all shards. Ctrl+C to stop.\n");

        bool canRedraw = !Console.IsOutputRedirected;
        int top = canRedraw ? Console.CursorTop : 0;

        while (true)
        {
            if (canRedraw)
            {
                try { Console.SetCursorPosition(0, top); }
                catch { canRedraw = false; } // e.g. terminal doesn't support it - fall back to plain scrolling output
            }
            await PrintOnce(state, clearLine: canRedraw);
            if (!canRedraw) Console.WriteLine();
            await Task.Delay(intervalMs);
        }
    }

    static async Task PrintOnce(ClusterState state, bool clearLine = false)
    {
        var lines = new List<string>
        {
            "=== Cluster Topology ===",
            $"Routing mode : {Router.Describe(state)}",
            $"Shard key    : {state.ShardKey ?? "(not seeded yet)"}",
        };

        long total = 0;
        foreach (var id in state.ShardIds)
        {
            if (!await ShardStore.PingAsync(id))
            {
                lines.Add($"  shard {id}  {ShardCatalog.HostLabel(id),-32}  UNREACHABLE");
                continue;
            }
            try
            {
                long count = await ShardStore.CountAsync(id);
                total += count;
                lines.Add($"  shard {id}  {ShardCatalog.HostLabel(id),-32}  rows: {count,7:N0}");
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
            {
                lines.Add($"  shard {id}  {ShardCatalog.HostLabel(id),-32}  no schema yet (run `init`)");
            }
        }
        lines.Add($"Total rows: {total:N0}");

        if (clearLine)
        {
            // Overwrite the previous frame in place rather than scrolling the
            // terminal - pad each line so a shorter new line erases a longer old one.
            foreach (var line in lines)
                Console.WriteLine(line.PadRight(Math.Max(line.Length, 80)));
        }
        else
        {
            foreach (var line in lines) Console.WriteLine(line);
        }
    }
}
