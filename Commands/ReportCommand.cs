namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class ReportCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        Console.WriteLine($"Scatter-gather across {state.ShardIds.Count} shards...");

        // Fan out concurrently - this IS the scatter-gather pattern: a sharded
        // read that isn't a single-shard lookup has to ask every shard and
        // merge the answers here, in the application, because no single shard
        // knows the whole answer.
        var tasks = state.ShardIds.ToDictionary(id => id, id => ShardStore.AggregateAsync(id));
        await Task.WhenAll(tasks.Values);

        long totalCount = 0;
        decimal totalSum = 0;
        foreach (var (id, task) in tasks)
        {
            var (count, sum) = await task;
            totalCount += count;
            totalSum += sum;
            Console.WriteLine($"  shard {id}: {count,7:N0} orders, sum {sum,12:N2}");
        }
        Console.WriteLine($"  ------------------------------------------");
        Console.WriteLine($"  TOTAL : {totalCount,7:N0} orders, sum {totalSum,12:N2}  (merged client-side from {state.ShardIds.Count} shards)");

        int topN = args.GetInt("top", 0);
        if (topN <= 0) return;

        Console.WriteLine($"\nTop {topN} orders by amount (per-shard top-{topN}, merged client-side):");
        var topPerShard = new List<Order>();
        foreach (var id in state.ShardIds)
            topPerShard.AddRange(await ShardStore.TopByAmountAsync(id, topN));

        foreach (var o in topPerShard.OrderByDescending(o => o.Amount).Take(topN))
            Console.WriteLine($"  {o.OrderId}  {o.Amount,10:N2}  customer={o.CustomerId} city={o.City}");
    }
}
