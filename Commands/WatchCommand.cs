namespace Tadka.Samples.ShardedOrdersDemo.Commands;

/// <summary>
/// Tails ONE shard live — run this in its own terminal (one per shard) alongside
/// `stream` or `insert` running in another terminal, and watch rows land on the
/// correct shard the moment they're inserted. Read-only, safe to Ctrl+C anytime.
/// </summary>
static class WatchCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        string? shardArg = args.Get("shard");
        if (shardArg is null || !int.TryParse(shardArg, out int shardId))
        {
            Console.WriteLine("Usage: watch --shard N [--interval-ms 500]");
            return;
        }
        int intervalMs = args.GetInt("interval-ms", 500);

        Console.WriteLine($"=== Watching shard {shardId} ({ShardCatalog.HostLabel(shardId)}) ===");
        Console.WriteLine("Ctrl+C to stop.\n");

        var seen = new HashSet<string>();
        bool baselineTaken = false;

        while (true)
        {
            try
            {
                long count = await ShardStore.CountAsync(shardId);
                var recent = await ShardStore.RecentAsync(shardId, 200);

                if (!baselineTaken)
                {
                    foreach (var o in recent) seen.Add(o.OrderId);
                    Console.WriteLine($"[baseline] {count:N0} existing rows on shard {shardId}. Watching for new arrivals...\n");
                    baselineTaken = true;
                }
                else
                {
                    // `recent` is newest-first; walk it oldest-first so new rows print in arrival order.
                    for (int i = recent.Count - 1; i >= 0; i--)
                    {
                        var o = recent[i];
                        if (seen.Add(o.OrderId))
                        {
                            Console.WriteLine(
                                $"  + {o.OrderId,-24} customer={o.CustomerId,-10} restaurant={o.RestaurantId,-9} city={o.City,-10} amount={o.Amount,8:N2}   (shard {shardId} now {count:N0} rows)");
                        }
                    }
                }
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
            {
                Console.WriteLine($"shard {shardId}: no schema yet (run `init`)");
            }
            catch (Npgsql.NpgsqlException)
            {
                Console.WriteLine($"shard {shardId}: unreachable, retrying...");
            }

            await Task.Delay(intervalMs);
        }
    }
}
