namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class InitCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        foreach (var id in state.ShardIds)
        {
            if (!await ShardStore.PingAsync(id))
            {
                Console.WriteLine($"shard {id} ({ShardCatalog.HostLabel(id)}) is unreachable — is docker compose up?");
                continue;
            }
            await ShardStore.EnsureSchemaAsync(id);
            Console.WriteLine($"shard {id} ({ShardCatalog.HostLabel(id)}): schema ready.");
        }
    }
}
