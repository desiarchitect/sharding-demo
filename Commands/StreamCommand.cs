namespace Tadka.Samples.ShardedOrdersDemo.Commands;

/// <summary>
/// Continuously inserts one new live order every interval — the "instructor
/// drives new data" counterpart to `watch`. Run this in one terminal while
/// `watch --shard N` runs in others (one per shard) to see, live, which shard
/// each new order lands on. Ctrl+C to stop.
/// </summary>
static class StreamCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        int intervalMs = args.GetInt("interval-ms", 1000);
        int count = args.GetInt("count", 0); // 0 = run forever
        string shardKey = args.GetOrDefault("shard-key", state.ShardKey ?? "order_id");

        Console.WriteLine($"Streaming new orders every {intervalMs}ms, shard key = {shardKey}, routing = {Router.Describe(state)}.");
        Console.WriteLine("Run `watch --shard N` in other terminals to see them land live. Ctrl+C to stop.\n");

        // A fresh, time-seeded RNG on purpose - this is live/improvised demo data,
        // not the reproducible dataset `seed` produces (different orderId prefix
        // too, so the two never collide).
        var rng = new Random();
        int i = 0;

        while (count <= 0 || i < count)
        {
            i++;
            string orderId = $"order-live-{DateTimeOffset.UtcNow:HHmmss}-{i:D4}";
            var generated = SeedGenerator.Generate(i, rng);
            var order = generated with { OrderId = orderId };

            string keyValue = Order.ShardKeyValue(order, shardKey);
            int shardId = Router.Route(state, keyValue);
            await ShardStore.InsertOrderAsync(shardId, order);

            Console.WriteLine($"  -> {orderId}  {shardKey}={keyValue,-10} -> shard {shardId}");
            await Task.Delay(intervalMs);
        }
    }
}
