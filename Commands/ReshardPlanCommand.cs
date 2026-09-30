using Tadka.Samples.ShardedOrdersDemo.Migration;

namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class ReshardPlanCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        string to = args.GetOrDefault("to", "consistent-vnodes");
        if (to != "consistent-vnodes")
        {
            Console.WriteLine("Only --to consistent-vnodes is supported.");
            return;
        }
        int vnodes = args.GetInt("vnodes", state.Vnodes);

        var moves = await ReshardPlanner.ComputePlanAsync(state, vnodes);

        long total = 0;
        var stayPerShard = new Dictionary<int, int>();
        var outPerShard = new Dictionary<int, int>();
        foreach (var id in state.ShardIds) { stayPerShard[id] = 0; outPerShard[id] = 0; }

        // Re-count totals per shard for the "N stay, M move out" table.
        var rowsPerShard = new Dictionary<int, long>();
        foreach (var id in state.ShardIds) rowsPerShard[id] = await ShardStore.CountAsync(id);
        foreach (var count in rowsPerShard.Values) total += count;

        foreach (var move in moves) outPerShard[move.From]++;
        foreach (var id in state.ShardIds) stayPerShard[id] = (int)rowsPerShard[id] - outPerShard[id];

        int priorShardCount = state.ShardIds.Count - 1;
        string beforeLabel = state.Mode == RoutingMode.NaiveModulo
            ? $"naive hash % {priorShardCount} (physical placement)"
            : $"consistent-hash + {vnodes} vnodes/shard, {priorShardCount} shards (physical placement)";
        Console.WriteLine($"Reshard plan: {beforeLabel} -> consistent-hash + {vnodes} vnodes/shard, {state.ShardIds.Count} shards");
        foreach (var id in state.ShardIds)
            Console.WriteLine($"  shard {id}: {rowsPerShard[id],6:N0} rows -> {stayPerShard[id],6:N0} stay, {outPerShard[id],6:N0} move out");

        Console.WriteLine("  --------------------------------------------------");
        double pct = total == 0 ? 0 : 100.0 * moves.Count / total;
        Console.WriteLine($"  Rows to migrate: {moves.Count:N0} of {total:N0} ({pct:F1}% — roughly one shard's share of the");
        Console.WriteLine("  keyspace, not a universal constant; the exact figure depends on ring topology, vnode");
        Console.WriteLine("  count, and key distribution, which is why this is measured live rather than assumed).");
        Console.WriteLine("  Nothing has moved yet — this is a dry run. Run `reshard apply` to execute it.");
    }
}
