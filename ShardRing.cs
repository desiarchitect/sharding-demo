using System.Text;

namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>
/// The routing math — ported byte-for-byte from samples/sharding-demo/Program.cs
/// (the in-memory simulation this project replaces), so the same hash and the
/// same ring algorithm that proved the concept now drive real Postgres shards
/// instead of an in-memory array index.
/// </summary>
static class ShardRing
{
    /// <summary>Naive hash % N. Assumes shard ids are exactly 1..count (true for
    /// this demo's 4-then-5 narrative — see README's "explicitly out of scope").</summary>
    public static int NaiveModulo(string key, int shardCount)
        => (int)(Hash(key) % (ulong)shardCount) + 1;

    public static (ulong point, int shardId)[] BuildRing(IReadOnlyList<int> shardIds, int vnodesPerShard)
    {
        var points = new List<(ulong point, int shardId)>(shardIds.Count * vnodesPerShard);
        foreach (var id in shardIds)
            for (int v = 0; v < vnodesPerShard; v++)
                points.Add((Hash($"shard-{id}#vnode-{v}"), id));
        points.Sort((a, b) => a.point.CompareTo(b.point));
        return points.ToArray();
    }

    /// <summary>First ring point clockwise from the key's hash (wraps around).</summary>
    public static int Locate((ulong point, int shardId)[] ring, ulong keyHash)
    {
        int lo = 0, hi = ring.Length - 1, ans = 0;
        while (lo <= hi)
        {
            int mid = (lo + hi) / 2;
            if (ring[mid].point >= keyHash) { ans = mid; hi = mid - 1; }
            else lo = mid + 1;
        }
        if (lo == ring.Length) ans = 0; // past the last point -> wrap to first
        return ring[ans].shardId;
    }

    /// <summary>Stable 64-bit hash — FNV-1a then a splitmix64 avalanche finalizer,
    /// deterministic across runs/machines. Identical to the in-memory demo's Hash().</summary>
    public static ulong Hash(string s)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong h = offset;
        foreach (byte b in Encoding.UTF8.GetBytes(s)) { h ^= b; h *= prime; }
        h ^= h >> 30; h *= 0xbf58476d1ce4e5b9UL;
        h ^= h >> 27; h *= 0x94d049bb133111ebUL;
        h ^= h >> 31;
        return h;
    }
}
