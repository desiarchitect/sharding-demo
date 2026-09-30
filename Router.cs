namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>Combines cluster state (shard list, mode, vnodes) with the ring math
/// to answer the one question every command needs: which shard owns this key?</summary>
static class Router
{
    public static int Route(ClusterState state, string shardKeyValue) => state.Mode switch
    {
        RoutingMode.NaiveModulo => ShardRing.NaiveModulo(shardKeyValue, state.ShardIds.Count),
        RoutingMode.ConsistentHashVnodes => ShardRing.Locate(
            ShardRing.BuildRing(state.ShardIds, state.Vnodes), ShardRing.Hash(shardKeyValue)),
        _ => throw new InvalidOperationException($"Unknown routing mode: {state.Mode}"),
    };

    public static string Describe(ClusterState state) => state.Mode switch
    {
        RoutingMode.NaiveModulo => $"naive hash % N   (N = {state.ShardIds.Count})",
        RoutingMode.ConsistentHashVnodes => $"consistent-hash + {state.Vnodes} vnodes/shard ({state.ShardIds.Count} shards)",
        _ => "unknown",
    };
}
