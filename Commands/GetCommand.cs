namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class GetCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        string? orderId = args.Get("order-id");
        if (orderId is null)
        {
            Console.WriteLine("Usage: get --order-id X [--verify]");
            return;
        }

        // `get` always routes by order_id, regardless of the seeded shard key -
        // this is deliberate: it demonstrates that looking something up by
        // anything OTHER than the shard key means you don't actually know
        // which shard has it (see `report`'s scatter-gather for that case).
        int routedShard = Router.Route(state, orderId);
        Console.WriteLine($"hash/route({orderId}) via {Router.Describe(state)} -> shard {routedShard}");

        var row = await ShardStore.GetOrderAsync(routedShard, orderId);
        Console.WriteLine(row is not null
            ? $"SELECT ... FROM shard {routedShard} -> HIT (1 row)"
            : $"SELECT ... FROM shard {routedShard} -> MISS (0 rows)");

        if (!args.Has("verify")) return;

        Console.WriteLine($"\nScanning all {state.ShardIds.Count} shards for {orderId}...");
        bool foundAnywhere = false;
        foreach (var id in state.ShardIds)
        {
            var found = await ShardStore.GetOrderAsync(id, orderId);
            if (found is not null)
            {
                foundAnywhere = true;
                Console.WriteLine($"  shard {id}: FOUND (1 row)");
            }
            else
            {
                Console.WriteLine($"  shard {id}: not found");
            }
        }

        if (foundAnywhere && row is null)
        {
            Console.WriteLine();
            Console.WriteLine("This is a REAL wrong-shard miss, not a bug in the demo: the row genuinely lives");
            Console.WriteLine("elsewhere; the current routing formula genuinely points somewhere else. This is");
            Console.WriteLine("exactly what naive modulo sharding does on every resize.");
        }
        else if (!foundAnywhere)
        {
            Console.WriteLine();
            Console.WriteLine($"{orderId} does not exist on any shard.");
        }
    }
}
