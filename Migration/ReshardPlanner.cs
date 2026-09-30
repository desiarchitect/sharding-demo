namespace Tadka.Samples.ShardedOrdersDemo.Migration;

record Move(string OrderId, int From, int To);

/// <summary>
/// Reads every row on every current shard and asks the ring "who owns this key
/// NOW?" — anywhere the answer differs from where the row actually lives is a
/// move. Read-only: computing a plan never writes anything.
/// </summary>
static class ReshardPlanner
{
    public static async Task<List<Move>> ComputePlanAsync(ClusterState state, int vnodesPerShard)
    {
        var targetShardIds = state.ShardIds; // the FULL list, including any just-added shard
        var ring = ShardRing.BuildRing(targetShardIds, vnodesPerShard);
        string shardKeyColumn = state.ShardKey ?? "order_id";

        var moves = new List<Move>();
        foreach (var shardId in targetShardIds)
        {
            var rows = await ShardStore.ListShardKeysAsync(shardId, shardKeyColumn);
            foreach (var (orderId, keyValue) in rows)
            {
                int owner = ShardRing.Locate(ring, ShardRing.Hash(keyValue));
                if (owner != shardId) moves.Add(new Move(orderId, shardId, owner));
            }
        }
        return moves;
    }
}
