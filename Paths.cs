using System.Runtime.CompilerServices;

namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>
/// Resolves .state/ next to THIS source file's folder, not the current working
/// directory — so it works the same whether you run `dotnet run` from the repo
/// root or from inside samples/sharded-orders-demo/.
/// </summary>
static class Paths
{
    public static readonly string ProjectDir = GetDir();
    public static string StateDir => Path.Combine(ProjectDir, ".state");
    public static string ClusterStateFile => Path.Combine(StateDir, "cluster-state.json");
    public static string MigrationLedgerFile => Path.Combine(StateDir, "migration-log.jsonl");

    static string GetDir([CallerFilePath] string here = "") => Path.GetDirectoryName(here)!;

    public static void EnsureStateDir() => Directory.CreateDirectory(StateDir);
}
