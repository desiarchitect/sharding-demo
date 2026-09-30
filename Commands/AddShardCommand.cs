namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class AddShardCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        int id = args.GetInt("id", state.ShardIds.Count > 0 ? state.ShardIds.Max() + 1 : 1);

        if (state.ShardIds.Contains(id))
        {
            Console.WriteLine($"Shard {id} is already registered. Nothing to do.");
            return;
        }

        if (!await ShardStore.PingAsync(id))
        {
            Console.WriteLine($"Shard {id} ({ShardCatalog.HostLabel(id)}) is not reachable.");
            Console.WriteLine($"Bring it up first, e.g.: docker compose -f samples/sharded-orders-demo/docker-compose.yml --profile shard5 up -d shard-db-{id}");
            return;
        }

        await ShardStore.EnsureSchemaAsync(id);

        int oldCount = state.ShardIds.Count;
        int newCount = oldCount + 1;

        // Measure the naive-rehash impact for real, on the actual keys currently
        // seeded - only meaningful if we're still in naive mode (once you're on
        // consistent hashing, "reshard plan" is the right tool, not this).
        if (state.Mode == RoutingMode.NaiveModulo)
        {
            string shardKeyColumn = state.ShardKey ?? "order_id";
            long moved = 0, total = 0;
            foreach (var existingId in state.ShardIds)
            {
                var rows = await ShardStore.ListShardKeysAsync(existingId, shardKeyColumn);
                foreach (var (_, keyValue) in rows)
                {
                    total++;
                    ulong h = ShardRing.Hash(keyValue);
                    int oldOwner = (int)(h % (ulong)oldCount) + 1;
                    int newOwner = (int)(h % (ulong)newCount) + 1;
                    if (oldOwner != newOwner) moved++;
                }
            }

            state.ShardIds.Add(id);
            state.Save();

            Console.WriteLine($"Shard {id} ({ShardCatalog.HostLabel(id)}) reachable. Schema created. Cluster is now {newCount} shards.");
            if (total > 0)
            {
                double pct = 100.0 * moved / total;
                Console.WriteLine($"Measured impact of hash % {oldCount} -> hash % {newCount} across all {total:N0} seeded keys");
                Console.WriteLine($"(recomputed live, not assumed): {moved:N0} keys now hash to a DIFFERENT shard than");
                Console.WriteLine($"the one they're actually stored on = {pct:F1}% reshuffled by just changing the modulus.");
            }
            Console.WriteLine("WARNING: none of the existing rows were physically moved. Every future lookup now");
            Console.WriteLine($"uses hash % {newCount}, so most lookups are about to point at the wrong place.");
        }
        else
        {
            state.ShardIds.Add(id);
            state.Save();
            Console.WriteLine($"Shard {id} ({ShardCatalog.HostLabel(id)}) reachable. Schema created. Cluster is now {newCount} shards.");
            Console.WriteLine("Routing mode is consistent-hash+vnodes - existing rows are NOT yet reassigned to the");
            Console.WriteLine("new shard. Run `reshard plan` / `reshard apply` to migrate this shard's fair share in.");
        }
    }
}
