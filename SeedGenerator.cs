namespace Tadka.Samples.ShardedOrdersDemo;

/// <summary>
/// Deterministic synthetic order generation. Every row carries all four possible
/// shard-key fields (order_id, customer_id, restaurant_id, city) regardless of
/// which one you're routing by — so re-seeding with a different --shard-key
/// still produces thematically real Tadka-shaped data, just routed differently.
/// </summary>
static class SeedGenerator
{
    const int CustomerPoolSize = 5000;
    const int RestaurantPoolSize = 200;

    // Same skew as the old in-memory demo's DEMO 4, for continuity: Bangalore
    // dominates, four other cities split the long tail.
    static readonly (string City, double Share)[] CityShares =
    {
        ("Bangalore", 0.80), ("Hyderabad", 0.08), ("Pune", 0.06), ("Chennai", 0.04), ("Delhi", 0.02),
    };

    // Zipf-ish: restaurant rank r gets weight 1/r, normalized. A handful of
    // popular restaurants take a disproportionate share; most take a long tail.
    // Deliberately less extreme than the city skew above - the "realistic middle
    // case" the plan calls for.
    static readonly double[] RestaurantCumulative = BuildZipfCumulative(RestaurantPoolSize);

    public static Order Generate(int index, Random rng)
    {
        string orderId = $"order-{index:D7}";
        string customerId = $"cust-{rng.Next(CustomerPoolSize):D5}";
        string restaurantId = $"rest-{PickZipfIndex(rng):D3}";
        string city = PickWeightedCity(rng);
        decimal amount = Math.Round(99m + (decimal)rng.NextDouble() * 900m, 2);
        return new Order(orderId, customerId, restaurantId, city, amount, "created", DateTimeOffset.UtcNow);
    }

    static string PickWeightedCity(Random rng)
    {
        double r = rng.NextDouble();
        double cumulative = 0;
        foreach (var (city, share) in CityShares)
        {
            cumulative += share;
            if (r <= cumulative) return city;
        }
        return CityShares[^1].City;
    }

    static int PickZipfIndex(Random rng)
    {
        double r = rng.NextDouble();
        int idx = Array.BinarySearch(RestaurantCumulative, r);
        if (idx < 0) idx = ~idx;
        return Math.Min(idx, RestaurantCumulative.Length - 1);
    }

    static double[] BuildZipfCumulative(int n)
    {
        var weights = new double[n];
        double total = 0;
        for (int i = 1; i <= n; i++) { weights[i - 1] = 1.0 / i; total += weights[i - 1]; }
        var cumulative = new double[n];
        double running = 0;
        for (int i = 0; i < n; i++) { running += weights[i] / total; cumulative[i] = running; }
        return cumulative;
    }
}
