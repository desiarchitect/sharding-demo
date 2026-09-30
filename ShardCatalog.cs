namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>
/// Maps a shard id to a real Postgres connection — matches docker-compose.yml
/// exactly: shard N listens on host port 5500+N, database tadka_shardN.
/// Override user/password via SHARD_DB_USER / SHARD_DB_PASSWORD if you changed
/// the compose file's defaults.
/// </summary>
static class ShardCatalog
{
    public static string ConnectionString(int shardId)
    {
        var user = Environment.GetEnvironmentVariable("SHARD_DB_USER") ?? "tadka";
        var pass = Environment.GetEnvironmentVariable("SHARD_DB_PASSWORD") ?? "tadka_local";
        int port = 5500 + shardId;
        string db = $"tadka_shard{shardId}";
        return $"Host=localhost;Port={port};Database={db};Username={user};Password={pass};Timeout=5;Command Timeout=30";
    }

    public static string HostLabel(int shardId) => $"localhost:{5500 + shardId}/tadka_shard{shardId}";
}
