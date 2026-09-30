using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tadka.Samples.ShardedOrdersDemo;

[JsonConverter(typeof(JsonStringEnumConverter))]
enum RoutingMode { NaiveModulo, ConsistentHashVnodes }

/// <summary>
/// The single source of truth for "what does the cluster look like right now" —
/// shard list, routing mode, shard key, vnode count. Persisted as JSON next to
/// the project so every command sees the same state, and a student can inspect
/// or delete it directly (`reset`, or just `rm -rf .state`).
/// </summary>
class ClusterState
{
    public List<int> ShardIds { get; set; } = new() { 1, 2, 3, 4 };
    public RoutingMode Mode { get; set; } = RoutingMode.NaiveModulo;
    public int Vnodes { get; set; } = 150;
    public string? ShardKey { get; set; }

    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ClusterState Load()
    {
        if (!File.Exists(Paths.ClusterStateFile))
        {
            var fresh = new ClusterState();
            fresh.Save();
            return fresh;
        }
        var json = File.ReadAllText(Paths.ClusterStateFile);
        return JsonSerializer.Deserialize<ClusterState>(json, JsonOptions) ?? new ClusterState();
    }

    public void Save()
    {
        Paths.EnsureStateDir();
        File.WriteAllText(Paths.ClusterStateFile, JsonSerializer.Serialize(this, JsonOptions));
    }

    public static void ResetToDefaults()
    {
        if (File.Exists(Paths.ClusterStateFile)) File.Delete(Paths.ClusterStateFile);
        if (File.Exists(Paths.MigrationLedgerFile)) File.Delete(Paths.MigrationLedgerFile);
    }
}
