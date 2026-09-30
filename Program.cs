// Tadka — Sharded Orders Demo: real Postgres-backed sharding, with simplified
// resharding mechanics (see README.md's "Framing and honest limits" section —
// this is NOT a copy of how a production sharding platform operates).
//
// Replaces samples/sharding-demo (the old pure in-memory simulation) with real
// separate Postgres databases as shards: real INSERT routing, a real cross-shard
// scatter-gather query, and — the centerpiece — actually adding a new shard live,
// watching a real query break because of it, then actually fixing it with a
// real, measurable data migration.
//
// Run: docker compose -f samples/sharded-orders-demo/docker-compose.yml up -d
//      dotnet run --project samples/sharded-orders-demo -- <command> [options]
// See README.md for the full command list and the two-act walkthrough.

using Tadka.Samples.ShardedOrdersDemo;
using Tadka.Samples.ShardedOrdersDemo.Commands;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

string command = args[0];
var cmdArgs = new Args(args[1..]);
var state = ClusterState.Load();

try
{
    switch (command)
    {
        case "topology": await TopologyCommand.RunAsync(cmdArgs, state); break;
        case "init": await InitCommand.RunAsync(cmdArgs, state); break;
        case "seed": await SeedCommand.RunAsync(cmdArgs, state); break;
        case "insert": await InsertCommand.RunAsync(cmdArgs, state); break;
        case "get": await GetCommand.RunAsync(cmdArgs, state); break;
        case "report": await ReportCommand.RunAsync(cmdArgs, state); break;
        case "add-shard": await AddShardCommand.RunAsync(cmdArgs, state); break;
        case "watch": await WatchCommand.RunAsync(cmdArgs, state); break;
        case "stream": await StreamCommand.RunAsync(cmdArgs, state); break;
        case "reshard":
            if (args.Length < 2) { Console.WriteLine("Usage: reshard plan|apply --to consistent-vnodes [--vnodes N] [--resume]"); break; }
            var reshardArgs = new Args(args[2..]);
            if (args[1] == "plan") await ReshardPlanCommand.RunAsync(reshardArgs, state);
            else if (args[1] == "apply") await ReshardApplyCommand.RunAsync(reshardArgs, state);
            else Console.WriteLine("Usage: reshard plan|apply --to consistent-vnodes [--vnodes N] [--resume]");
            break;
        case "reset": await ResetCommand.RunAsync(cmdArgs, state); break;
        default:
            PrintUsage();
            return 1;
    }
}
catch (Npgsql.NpgsqlException ex)
{
    Console.WriteLine($"Database error: {ex.Message}");
    Console.WriteLine("Is docker compose up? docker compose -f samples/sharded-orders-demo/docker-compose.yml up -d");
    return 1;
}

return 0;

static void PrintUsage()
{
    Console.WriteLine("""
        Tadka Sharded Orders Demo — real Postgres-backed sharding, simplified resharding mechanics.

        Usage: dotnet run --project samples/sharded-orders-demo -- <command> [options]

        Commands:
          topology                                          show routing mode + live per-shard row counts
          init                                               create the orders schema on every shard
          seed --count 20000 --shard-key order_id|customer_id|restaurant_id|city
          insert --customer-id X --city Y --amount Z [--restaurant-id R]
          get --order-id X [--verify]
          report [--top N]                                  scatter-gather across all shards
          add-shard --id 5                                   register + init a new shard (routing unchanged)
          reshard plan --to consistent-vnodes [--vnodes 150]  dry run, writes nothing
          reshard apply --to consistent-vnodes [--vnodes 150] [--resume]
          reset                                               truncate all shards, clear cluster state

          watch --shard N [--interval-ms 500]               live-tail ONE shard - run one per terminal
          topology --watch [--interval-ms 1000]             live dashboard of ALL shards in one terminal
          stream [--shard-key K] [--interval-ms 1000] [--count N]
                                                              continuously insert new orders (0 = forever);
                                                              run alongside `watch`/`topology --watch` to see
                                                              live routing across terminals

        See README.md for the full two-act "add a shard, watch it break, then fix it" walkthrough,
        and the "Live, multi-terminal demo" section for the watch/stream setup.
        """);
}
