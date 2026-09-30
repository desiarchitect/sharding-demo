namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class SeedCommand
{
    static readonly string[] ValidShardKeys = { "order_id", "customer_id", "restaurant_id", "city" };

    public static async Task RunAsync(Args args, ClusterState state)
    {
        int count = args.GetInt("count", 20_000);
        string shardKey = args.GetOrDefault("shard-key", state.ShardKey ?? "order_id");
        if (!ValidShardKeys.Contains(shardKey))
        {
            Console.WriteLine($"Unknown --shard-key '{shardKey}'. Valid: {string.Join(", ", ValidShardKeys)}");
            return;
        }

        // --mode lets you seed DIRECTLY into a consistent-hash ring (skipping naive
        // entirely). This matters: the "only ~1/N moves" promise of consistent
        // hashing is a property of GROWING a cluster that's already using it - it
        // does NOT apply to converting an existing naive-hash dataset onto a ring
        // (that's two topology changes at once and costs about as much as a full
        // rehash). See README "Framing and honest limits" for the real numbers.
        string mode = args.GetOrDefault("mode", "naive");
        state.Mode = mode switch
        {
            "naive" => RoutingMode.NaiveModulo,
            "consistent-vnodes" => RoutingMode.ConsistentHashVnodes,
            _ => throw new ArgumentException($"Unknown --mode '{mode}'. Valid: naive, consistent-vnodes"),
        };
        if (state.Mode == RoutingMode.ConsistentHashVnodes)
            state.Vnodes = args.GetInt("vnodes", state.Vnodes);

        state.ShardKey = shardKey;
        state.Save();

        Console.WriteLine($"Shard key: {shardKey}");
        Console.WriteLine($"Routing with: {Router.Describe(state)}");
        Console.WriteLine($"Generating {count:N0} orders...");

        var rng = new Random(20260913); // deterministic - same seed run always produces the same data
        int[] insertedPerShard = new int[state.ShardIds.Max() + 1];
        int inserted = 0, alreadyPresent = 0;

        for (int i = 0; i < count; i++)
        {
            var order = SeedGenerator.Generate(i, rng);
            string keyValue = Order.ShardKeyValue(order, shardKey);
            int shardId = Router.Route(state, keyValue);
            bool wasNew = await ShardStore.InsertOrderAsync(shardId, order);
            if (wasNew) { inserted++; insertedPerShard[shardId]++; }
            else alreadyPresent++;
        }

        Console.WriteLine();
        if (alreadyPresent > 0)
            Console.WriteLine($"{inserted:N0} new rows inserted, {alreadyPresent:N0} already present (safe re-run).");

        long total = 0;
        var counts = new Dictionary<int, long>();
        foreach (var id in state.ShardIds)
        {
            long c = await ShardStore.CountAsync(id);
            counts[id] = c;
            total += c;
        }

        Console.WriteLine();
        foreach (var id in state.ShardIds)
        {
            double pct = total == 0 ? 0 : 100.0 * counts[id] / total;
            string bar = new string('#', (int)Math.Round(pct / 2));
            Console.WriteLine($"  shard {id}: {counts[id],7:N0}  {pct,5:F1}%  {bar}");
        }

        double maxPct = total == 0 ? 0 : 100.0 * counts.Values.Max() / total;
        if (maxPct >= 60)
        {
            Console.WriteLine();
            Console.WriteLine($"WARNING: shard key '{shardKey}' is low-cardinality/skewed — one shard holds {maxPct:F1}% of rows.");
            Console.WriteLine("Hashing balances KEYS, not TRAFFIC. Pick a high-cardinality key (order_id / customer_id) instead.");
        }
        else if (maxPct >= 30)
        {
            Console.WriteLine();
            Console.WriteLine($"NOTE: shard key '{shardKey}' has a moderate hot spot — one shard holds {maxPct:F1}% of rows.");
            Console.WriteLine("Better than a severely skewed key (e.g. city), but order_id/customer_id would be more even.");
        }
    }
}
