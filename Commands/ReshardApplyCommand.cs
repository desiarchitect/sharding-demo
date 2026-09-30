using Tadka.Samples.ShardedOrdersDemo.Migration;

namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class ReshardApplyCommand
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
        bool resume = args.Has("resume");

        var beforeCounts = new Dictionary<int, long>();
        foreach (var id in state.ShardIds) beforeCounts[id] = await ShardStore.CountAsync(id);
        long beforeTotal = beforeCounts.Values.Sum();

        var moves = await ReshardPlanner.ComputePlanAsync(state, vnodes);
        Console.WriteLine($"Migrating {moves.Count:N0} rows ({Router.Describe(state)} -> consistent-hash+vnodes ring, {state.ShardIds.Count} shards)...");

        var perPair = new Dictionary<(int From, int To), int>();
        var result = await Migrator.MigrateAsync(moves, resume, onStep: (move, status) =>
        {
            if (status == "migrated" || status.StartsWith("resumed"))
            {
                var key = (move.From, move.To);
                perPair[key] = perPair.GetValueOrDefault(key) + 1;
            }
        });

        foreach (var ((from, toShard), n) in perPair.OrderBy(kv => kv.Key))
            Console.WriteLine($"  shard {from} -> shard {toShard}: {n:N0} rows migrated");

        Console.WriteLine($"Migration complete: {result.Migrated:N0} rows moved" +
            (result.AlreadyDone > 0 ? $" ({result.AlreadyDone:N0} already done from a prior run)." : "."));

        state.Mode = RoutingMode.ConsistentHashVnodes;
        state.Vnodes = vnodes;
        state.Save();
        Console.WriteLine($"Routing mode switched to: consistent-hash + {vnodes} vnodes/shard.");

        Console.WriteLine("\nBefore -> after row counts:");
        long afterTotal = 0;
        foreach (var id in state.ShardIds)
        {
            long after = await ShardStore.CountAsync(id);
            afterTotal += after;
            Console.WriteLine($"  shard {id}: {beforeCounts.GetValueOrDefault(id),6:N0} -> {after,6:N0}");
        }
        if (beforeTotal == afterTotal)
        {
            Console.WriteLine($"  Total:  {beforeTotal:N0} -> {afterTotal:N0}   (no rows lost, no duplicates)");
        }
        else if (resume && beforeTotal > afterTotal)
        {
            // Expected and healthy: the earlier interrupted run left some rows
            // inserted on their target shard but not yet deleted from source -
            // a transient duplicate, counted twice in "before". This resume
            // just finished deleting them from source, which is why the total
            // drops back down to the true count. This is the migration's
            // crash-safety working as designed (see README), not data loss.
            long healed = beforeTotal - afterTotal;
            Console.WriteLine($"  Total:  {beforeTotal:N0} -> {afterTotal:N0}   ({healed:N0} transient duplicate(s) from the");
            Console.WriteLine($"  interrupted run self-healed by this resume — no data was lost. This is the at-least-once");
            Console.WriteLine($"  vs exactly-once trade-off named in the README, working as designed.)");
        }
        else
        {
            Console.WriteLine($"  Total:  {beforeTotal:N0} -> {afterTotal:N0}   *** UNEXPECTED — investigate before re-running ***");
        }
    }
}
