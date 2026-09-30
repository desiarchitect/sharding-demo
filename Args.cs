namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>Minimal --flag value parser. No external dependency, matching the
/// sample's zero-dependency ethos (Npgsql is the only real one, and that's the point).</summary>
class Args
{
    readonly Dictionary<string, string> _values = new();
    readonly HashSet<string> _flags = new();

    public Args(string[] argv)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) continue;
            string key = argv[i][2..];
            if (i + 1 < argv.Length && !argv[i + 1].StartsWith("--"))
            {
                _values[key] = argv[i + 1];
                i++;
            }
            else
            {
                _flags.Add(key);
            }
        }
    }

    public string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;
    public string GetOrDefault(string key, string fallback) => Get(key) ?? fallback;
    public int GetInt(string key, int fallback) => int.TryParse(Get(key), out var v) ? v : fallback;
    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);
}
