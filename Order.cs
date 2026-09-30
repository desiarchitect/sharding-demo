namespace Tadka.Samples.ShardedOrdersDemo;

record Order(
    string OrderId,
    string CustomerId,
    string RestaurantId,
    string City,
    decimal Amount,
    string Status,
    DateTimeOffset CreatedAt)
{
    public static string ShardKeyValue(Order o, string shardKey) => shardKey switch
    {
        "customer_id" => o.CustomerId,
        "restaurant_id" => o.RestaurantId,
        "city" => o.City,
        _ => o.OrderId,
    };
}
