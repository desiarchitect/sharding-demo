# Sharded Orders Demo

Real Postgres sharding in .NET, with simplified resharding.

This runs against **four separate Postgres databases**. You get real `INSERT` routing, a real cross-shard scatter-gather query, and a live sequence where you add a fifth shard, watch a lookup break, and then migrate the data to fix it.

> This is real Postgres-backed sharding with simplified resharding mechanics. It is not how production sharding works end to end. Read [Honest limits](#honest-limits) before you teach it as more than it is.

## Why this exists

You don't need sharding at small scale, and that is the right call. But shard-key design, routing, cross-shard reads and what happens when you add capacity are things you don't feel from a slide. This demo lets you break it first and then fix it, on real databases.

## Run it

```bash
docker compose up -d
dotnet run --project . -c Release -- init
dotnet run --project . -c Release -- seed --count 20000 --shard-key order_id
dotnet run --project . -c Release -- topology
```

## Commands

| Command | What it does | Safe to rerun? |
|---|---|---|
| `topology` | Shows routing mode, shard key and a live `SELECT count(*)` per shard | read-only |
| `init` | Creates the `orders` schema on every registered shard | yes (`IF NOT EXISTS`) |
| `seed --count 20000 --shard-key order_id\|customer_id\|restaurant_id\|city [--mode naive\|consistent-vnodes] [--vnodes 150]` | Inserts N synthetic orders, prints per-shard distribution and a skew warning | yes, reruns insert 0 new rows |
| `insert --customer-id X --city Y --amount Z [--restaurant-id R]` | Inserts one order and prints the routing decision | adds a new row each call |
| `get --order-id X [--verify]` | Routes by `order_id` and fetches from that one shard. `--verify` scans every shard to show where the row really lives | read-only |
| `report [--top N]` | Scatter-gather: queries every shard concurrently and merges the results in the client | read-only |
| `add-shard --id 5` | Checks the shard is reachable, creates its schema, registers it. **Routing does not change** | yes, no-op if already registered |
| `reshard plan --to consistent-vnodes [--vnodes 150]` | Dry run. Compares each row's ring owner with where it is stored and prints a move/stay table. Writes nothing | read-only |
| `reshard apply --to consistent-vnodes [--vnodes 150] [--resume]` | Moves the rows for real, then switches the routing mode | resumable |
| `reset` | Truncates `orders` on every shard and resets local state | yes |
| `watch --shard N [--interval-ms 500]` | Tails one shard and prints each new row as it lands | read-only, Ctrl+C to stop |
| `topology --watch [--interval-ms 1000]` | One-terminal live dashboard of all shard row counts | read-only, Ctrl+C to stop |
| `stream [--shard-key K] [--interval-ms 1000] [--count N]` | Keeps inserting new orders (0 = forever) so `watch` has something to show | adds new rows each call |

The fifth shard is off by default, so adding it is a real, live event:

```bash
docker compose --profile shard5 up -d shard-db-5
```

## Live multi-terminal demo

The best way to show this is several terminals side by side. You can watch each row land on the correct shard the moment it is inserted.

```bash
# Terminal 1: feeds new data
dotnet run --project . -c Release -- stream --interval-ms 500

# Terminals 2-5: one per shard
dotnet run --project . -c Release -- watch --shard 1
dotnet run --project . -c Release -- watch --shard 2
dotnet run --project . -c Release -- watch --shard 3
dotnet run --project . -c Release -- watch --shard 4
```

Each `watch` terminal prints a `[baseline]` line for what is already there, then only new arrivals. Only one terminal lights up per insert, and which one depends on the routing formula you are using (naive `hash % N` or the consistent-hash ring). The formula decides where a row goes, not luck.

If you don't have room for five terminals, `topology --watch` shows the same thing in one terminal while `stream` runs elsewhere.

## Shard key choice, four ways

Same 20,000 synthetic orders, reseeded with a different `--shard-key` each time (run `reset` in between):

| Shard key | Biggest shard's share | What it shows |
|---|---:|---|
| `order_id` | 25.3% | High cardinality and even. The right choice |
| `customer_id` | 25.4% | Also high cardinality and even |
| `restaurant_id` | 33.1% | About 200 restaurants, with a few popular ones taking more. A moderate hot spot |
| `city` | 80.2% | Only 5 cities and Bangalore has 80% of the orders. Hashing balances keys, not traffic, so one shard gets hot and no hash can fix that |

`restaurant_id` is the middle case. It is worse than a uniform key and nowhere near as bad as `city`. Real shard keys usually land somewhere on this range.

## The main part: two scenarios

Going from 4 shards to 5 tells two different stories, depending on what you started from.

### Scenario 1: naive `hash % N` breaks

```bash
dotnet run --project . -c Release -- reset
dotnet run --project . -c Release -- seed --count 20000 --shard-key order_id
dotnet run --project . -c Release -- get --order-id order-0012345
#   hash/route(order-0012345) via naive hash % N (N = 4) -> shard 1
#   SELECT ... FROM shard 1 -> HIT (1 row)

docker compose --profile shard5 up -d shard-db-5
dotnet run --project . -c Release -- add-shard --id 5
#   Measured impact of hash % 4 -> hash % 5 across all 20,000 seeded keys
#   (recomputed live, not assumed): 15,987 keys now hash to a DIFFERENT shard than
#   the one they are stored on = 79.9% reshuffled just by changing the modulus.

dotnet run --project . -c Release -- get --order-id order-0012345
#   hash/route(order-0012345) via naive hash % N (N = 5) -> shard 5
#   SELECT ... FROM shard 5 -> MISS (0 rows)

dotnet run --project . -c Release -- get --order-id order-0012345 --verify
#   Scanning all 5 shards... shard 1: FOUND. Everywhere else: not found.
#   This is a real wrong-shard miss, not a bug in the demo.
```

**Captured live:** 79.9% of keys now hash to a different shard just because the modulus changed. Nothing moved physically, so most lookups point at the wrong place. A `report` right after this still shows the correct total (20,000). So an aggregate scatter-gather query can hide a per-key routing break.

### Scenario 2: consistent hashing with vnodes, grown correctly from the start

You cannot cheaply convert an existing naive-hash dataset onto a consistent-hash ring. Doing that changes two things at once (the algorithm and the shard count) and moves about as much data as a full rehash, around 80% in this demo and not the ~20% consistent hashing promises. The cheap resize only works when you grow a cluster that is already on consistent hashing. It is not a way to fix a naive one later. The demo shows this instead of just claiming it:

```bash
dotnet run --project . -c Release -- reset
dotnet run --project . -c Release -- seed --count 20000 --shard-key order_id --mode consistent-vnodes --vnodes 150
dotnet run --project . -c Release -- add-shard --id 5
#   (shard 5 container is already up from Scenario 1, or bring it up again)

dotnet run --project . -c Release -- reshard plan --to consistent-vnodes --vnodes 150
#   Reshard plan: consistent-hash + 150 vnodes/shard, 4 shards (physical placement)
#     -> consistent-hash + 150 vnodes/shard, 5 shards
#   Rows to migrate: 3,453 of 20,000 (17.3%, roughly one shard's share of the keyspace)
#   Contrast: growing FROM naive hashing (Scenario 1) moved 79.9% of the same-sized dataset.

dotnet run --project . -c Release -- reshard apply --to consistent-vnodes --vnodes 150
#   Migrating 3,453 rows...
#     shard 1 -> shard 5: 1,148 rows migrated
#     shard 2 -> shard 5: 736 rows migrated
#     shard 3 -> shard 5: 685 rows migrated
#     shard 4 -> shard 5: 884 rows migrated
#   Migration complete: 3,453 rows moved.
#   Total: 20,000 -> 20,000 (no rows lost, no duplicates)

dotnet run --project . -c Release -- get --order-id order-0000001
#   Ring route: shard 3 -> HIT (1 row). Correct, whether or not this key moved.
```

**Captured live: 17.3% of rows moved.** That is close to the one-fifth a balanced 5-shard ring predicts, and far below Scenario 1's 79.9%. Don't take away "consistent hashing always moves 20%". It doesn't. The number depends on ring layout, vnode count and key spread, which is why the demo measures it every run. The real lesson is that growing a cluster that is already on consistent hashing costs roughly the new shard's share, while converting a naive one costs almost a full rehash.

## Migration safety

A local JSONL ledger (`.state/migration-log.jsonl`) records `InsertedOnTarget` and then `Completed` for each row. A row is deleted from the source shard only after the insert on the target is confirmed. Every insert uses `ON CONFLICT DO NOTHING`. So if you kill a `reshard apply` halfway, you can just run it again. `reshard plan` and `apply` always work out the move list from where the data is right now, not from history, so a row that already moved won't show up in the next plan. `--resume` uses the ledger to skip rows already known to be complete. That keeps a retry fast and gives you an audit trail, but it is not the only thing that makes a retry safe.

Verified live by killing a migration in flight and rerunning it. The `report` total read one row high for a moment right after the kill, because the row existed on both its old and new shard. It went back to the true total once the resume finished deleting it from the source. No row was ever lost. So the behaviour is at-least-once in the middle and exactly-once at the end.

## Honest limits

This demo is simpler than a production sharding platform in three ways.

**1. Consistent hashing is one strategy, not the only answer.**

| Approach | Cost to resize | Notes |
|---|---|---|
| Naive `hash % N` | About 80% of keys move | Don't use it in production |
| Consistent hashing with vnodes (used here) | About 1/N of keys | No directory to maintain |
| Range-based partitioning (Vitess-style keyspace ranges) | Split or merge a range, easy range scans | A directory tracks which shard owns which range |
| Directory-based | Most flexible, any key to any shard | The directory becomes a dependency and a bottleneck |

Many real systems use range-based partitioning with explicit split and merge steps instead of a hash ring. Vitess is the well-documented example. Neither is more "real" than the other. They are different trade-offs for the same problem.

**2. The moved-row percentage is measured, not a fixed rule.** The demo prints what it actually measured on each run, never a hardcoded "~20%".

**3. The biggest gap: the migration assumes no writes during the copy.** `copy, verify, delete` is a single pass. Nothing keeps the target in sync while the source keeps taking live traffic. Vitess's documented workflow is closer to this: create the target shards, start the copy, keep the target caught up with continuous change replication, validate, cut traffic over, and clean up the source. The source keeps serving writes the whole time.

> **Failure mode:** if an order on the source shard is updated between this demo's copy step and its delete step, that update is silently lost. The source row (with the update) is deleted, and the target only has the value from when it was copied.
>
> **Revisit when:** you do this in any real deployment. Production resharding needs continuous replication and a controlled cutover. The demo stops short of that on purpose, so you can ask the question: what happens to an order that changes while it is being migrated?

## Out of scope

No shard auto-discovery or service registry, no 2PC, no UI, no simulation of concurrent writers during migration, no cross-shard JOINs, secondary indexes or read replicas, and no support for shard counts beyond the 4 to 5 story. The ring math is generic, but seeding and reporting are tuned for this story.

## Where this fits in a real system

Most systems stay on a single Postgres for a long time, and that is correct. When you hit a real single-writer ceiling, sharding the orders table by a high-cardinality key is the next move. Prefer growing a cluster that is already on consistent hashing over converting a naive one later. That is what this demo shows. Real-world options in the same space are Citus for Postgres, Vitess for MySQL, or application-level routing like `ShardCatalog` and `Router` here, just hardened.
