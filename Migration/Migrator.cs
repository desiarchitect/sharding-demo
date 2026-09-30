namespace Tadka.Samples.ShardedOrdersDemo.Migration;

/// <summary>
/// The insert-verify-delete state machine. The one invariant that matters:
/// NEVER delete from the source until the target insert is confirmed. That's
/// what makes a Ctrl-C mid-migration self-healing rather than data-losing —
/// worst case is a transient duplicate (row exists on both shards briefly),
/// never a permanently lost row.
///
/// This is deliberately NOT 2PC and NOT safe against concurrent writes to the
/// row being migrated — see the README's "what real systems add that this
/// demo doesn't" section. A crash-safe two-step copy is the teaching point;
/// continuous replication + a cutover window is the production answer.
/// </summary>
static class Migrator
{
    public record Result(int Migrated, int AlreadyDone);

    public static async Task<Result> MigrateAsync(IReadOnlyList<Move> moves, bool resume, Action<Move, string>? onStep = null)
    {
        var latest = resume ? MigrationLedger.LatestByOrderId() : new Dictionary<string, LedgerEntry>();
        int migrated = 0, alreadyDone = 0;

        MigrationLedger.OpenForAppend();
        try
        {
            foreach (var move in moves)
            {
                if (latest.TryGetValue(move.OrderId, out var last)
                    && last.Phase == LedgerPhase.Completed && last.To == move.To)
                {
                    alreadyDone++;
                    onStep?.Invoke(move, "already migrated (resume)");
                    continue;
                }

                var sourceRow = await ShardStore.GetOrderAsync(move.From, move.OrderId);
                if (sourceRow is null)
                {
                    // Source already gone - a prior run got at least as far as deleting it.
                    // Confirm the target really has it before calling this row done.
                    var onTarget = await ShardStore.GetOrderAsync(move.To, move.OrderId);
                    if (onTarget is not null)
                    {
                        MigrationLedger.Append(new LedgerEntry(move.OrderId, move.From, move.To, LedgerPhase.Completed, DateTimeOffset.UtcNow));
                        migrated++;
                        onStep?.Invoke(move, "resumed: source already clear, target confirmed");
                    }
                    continue;
                }

                await ShardStore.InsertOrderAsync(move.To, sourceRow); // ON CONFLICT DO NOTHING - safe to repeat
                MigrationLedger.Append(new LedgerEntry(move.OrderId, move.From, move.To, LedgerPhase.InsertedOnTarget, DateTimeOffset.UtcNow));

                var confirmed = await ShardStore.GetOrderAsync(move.To, move.OrderId);
                if (confirmed is null)
                    throw new InvalidOperationException($"Migration invariant violated: {move.OrderId} not found on target shard {move.To} after insert.");

                await ShardStore.DeleteOrderAsync(move.From, move.OrderId);
                MigrationLedger.Append(new LedgerEntry(move.OrderId, move.From, move.To, LedgerPhase.Completed, DateTimeOffset.UtcNow));
                migrated++;
                onStep?.Invoke(move, "migrated");
            }
        }
        finally
        {
            MigrationLedger.Close();
        }

        return new Result(migrated, alreadyDone);
    }
}
