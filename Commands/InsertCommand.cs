namespace Tadka.Samples.ShardedOrdersDemo.Commands;

static class InsertCommand
{
    public static async Task RunAsync(Args args, ClusterState state)
    {
        string? customerId = args.Get("customer-id");
        string? city = args.Get("city");
        string? restaurantId = args.Get("restaurant-id");
        string? amountRaw = args.Get("amount");
        string? orderId = args.Get("order-id");

        if (customerId is null || city is null || amountRaw is null || !decimal.TryParse(amountRaw, out var amount))
        {
            Console.WriteLine("Usage: insert --customer-id X --city Y --amount Z [--restaurant-id R] [--order-id ID]");
            return;
        }
        restaurantId ??= "rest-000";
        orderId ??= $"order-live-{Guid.NewGuid():N}"[..24];

        var shardKey = state.ShardKey ?? "order_id";
        var order = new Order(orderId, customerId, restaurantId, city, amount, "created", DateTimeOffset.UtcNow);
        string keyValue = Order.ShardKeyValue(order, shardKey);
        int shardId = Router.Route(state, keyValue);

        Console.WriteLine($"Routing on {shardKey}='{keyValue}' -> {Router.Describe(state)} -> shard {shardId}");
        bool inserted = await ShardStore.InsertOrderAsync(shardId, order);
        Console.WriteLine(inserted
            ? $"Inserted {orderId} on shard {shardId} ({ShardCatalog.HostLabel(shardId)})."
            : $"{orderId} already exists on shard {shardId} — nothing changed.");
    }
}
