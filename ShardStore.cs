using Npgsql;

namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>
/// Thin, direct Npgsql helpers, no EF Core. Plain multi-connection SQL
/// is deliberate, so the fan-out
/// itself stays visible instead of hiding behind a DbContext per shard.
/// </summary>
static class ShardStore
{
    const string Ddl = """
        CREATE TABLE IF NOT EXISTS orders (
            order_id      TEXT PRIMARY KEY,
            customer_id   TEXT NOT NULL,
            restaurant_id TEXT NOT NULL,
            city          TEXT NOT NULL,
            amount        NUMERIC(10,2) NOT NULL,
            status        TEXT NOT NULL DEFAULT 'created',
            created_at    TIMESTAMPTZ NOT NULL DEFAULT now()
        );
        CREATE INDEX IF NOT EXISTS ix_orders_customer_id   ON orders (customer_id);
        CREATE INDEX IF NOT EXISTS ix_orders_restaurant_id ON orders (restaurant_id);
        """;

    public static async Task<bool> PingAsync(int shardId)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
            await conn.OpenAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task EnsureSchemaAsync(int shardId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(Ddl, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<bool> InsertOrderAsync(int shardId, Order o)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("""
            INSERT INTO orders (order_id, customer_id, restaurant_id, city, amount, status, created_at)
            VALUES (@order_id, @customer_id, @restaurant_id, @city, @amount, @status, @created_at)
            ON CONFLICT (order_id) DO NOTHING
            """, conn);
        cmd.Parameters.AddWithValue("order_id", o.OrderId);
        cmd.Parameters.AddWithValue("customer_id", o.CustomerId);
        cmd.Parameters.AddWithValue("restaurant_id", o.RestaurantId);
        cmd.Parameters.AddWithValue("city", o.City);
        cmd.Parameters.AddWithValue("amount", o.Amount);
        cmd.Parameters.AddWithValue("status", o.Status);
        cmd.Parameters.AddWithValue("created_at", o.CreatedAt);
        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0;
    }

    public static async Task<Order?> GetOrderAsync(int shardId, string orderId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT order_id, customer_id, restaurant_id, city, amount, status, created_at FROM orders WHERE order_id = @id",
            conn);
        cmd.Parameters.AddWithValue("id", orderId);
        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Order(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetDecimal(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6));
    }

    public static async Task DeleteOrderAsync(int shardId, string orderId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("DELETE FROM orders WHERE order_id = @id", conn);
        cmd.Parameters.AddWithValue("id", orderId);
        await cmd.ExecuteNonQueryAsync();
    }

    public static async Task<long> CountAsync(int shardId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*) FROM orders", conn);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    public static async Task<(long Count, decimal Sum)> AggregateAsync(int shardId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("SELECT count(*), coalesce(sum(amount), 0) FROM orders", conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt64(0), reader.GetDecimal(1));
    }

    /// <summary>Most recently created rows on this shard, newest first — what
    /// `watch` polls to show new arrivals live.</summary>
    public static async Task<List<Order>> RecentAsync(int shardId, int n)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT order_id, customer_id, restaurant_id, city, amount, status, created_at FROM orders ORDER BY created_at DESC LIMIT @n",
            conn);
        cmd.Parameters.AddWithValue("n", n);
        var result = new List<Order>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new Order(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return result;
    }

    public static async Task<List<Order>> TopByAmountAsync(int shardId, int n)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT order_id, customer_id, restaurant_id, city, amount, status, created_at FROM orders ORDER BY amount DESC LIMIT @n",
            conn);
        cmd.Parameters.AddWithValue("n", n);
        var result = new List<Order>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add(new Order(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetDecimal(4), reader.GetString(5), reader.GetFieldValue<DateTimeOffset>(6)));
        return result;
    }

    static readonly HashSet<string> ShardKeyColumns = new() { "order_id", "customer_id", "restaurant_id", "city" };

    /// <summary>order_id + the current shard-key column's value, for every row on this
    /// shard — used by `reshard plan` to compute ring ownership per row.</summary>
    public static async Task<List<(string OrderId, string KeyValue)>> ListShardKeysAsync(int shardId, string shardKeyColumn)
    {
        if (!ShardKeyColumns.Contains(shardKeyColumn))
            throw new ArgumentException($"Unknown shard key column: {shardKeyColumn}");

        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"SELECT order_id, {shardKeyColumn} FROM orders", conn);
        var result = new List<(string, string)>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            result.Add((reader.GetString(0), reader.GetString(1)));
        return result;
    }

    public static async Task TruncateAsync(int shardId)
    {
        await using var conn = new NpgsqlConnection(ShardCatalog.ConnectionString(shardId));
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("TRUNCATE orders", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
