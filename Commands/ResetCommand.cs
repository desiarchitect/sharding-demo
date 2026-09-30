namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class ResetCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        foreach (var id in state.ShardIds)
        {
            if (!await ShardStore.PingAsync(id))
            {
                Console.WriteLine($"shard {id}: unreachable, skipping.");
                continue;
            }
            try
            {
                await ShardStore.TruncateAsync(id);
                Console.WriteLine($"shard {id}: truncated.");
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
            {
                Console.WriteLine($"shard {id}: no schema yet, nothing to truncate.");
            }
        }

        ClusterState.ResetToDefaults();
        Console.WriteLine("Cluster state reset to defaults: shards [1,2,3,4], naive hash % N routing, no shard key.");
        Console.WriteLine("(Containers/volumes untouched — use `docker compose down -v` for a full teardown.)");
    }
}
