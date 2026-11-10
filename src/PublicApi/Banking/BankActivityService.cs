using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record ActivityTransaction(string TransactionId, string ConnectionId, string? Institution, string AccountId,
    string Name, decimal Amount, string? CurrencyCode, string Date, bool Pending);
public record SyncWarning(string ConnectionId, string? Institution, string Reason);
public record BankActivityResponse(string Shopper, IReadOnlyList<ActivityTransaction> Transactions, IReadOnlyList<SyncWarning> Warnings);

public record LookupTransaction(string TransactionId, string ConnectionId, string? Institution, string AccountId,
    string Name, decimal Amount, string? CurrencyCode, string Date, bool Pending);
public record TransactionsLookupResponse(string Shopper, string From, string To, IReadOnlyList<LookupTransaction> Transactions, IReadOnlyList<SyncWarning> Warnings);

/// <summary>
/// Support's views of a shopper's bank accounts: the shop's own synced copy of the activity, and a live date-range lookup.
/// </summary>
public class BankActivityService
{
    private static readonly TimeSpan[] NotReadyDelays =
        { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(5) };

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;
    private readonly KeyedLocks _locks;

    public BankActivityService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector, KeyedLocks locks)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
        _locks = locks;
    }

    private async Task<List<BankConnection>> BankConnectionsOfAsync(string shopperId, CancellationToken ct)
    {
        var connections = await _db.BankConnections
            .Where(c => c.ShopperId == shopperId && c.Kind == ConnectionKind.Bank).OrderBy(c => c.Id).ToListAsync(ct);
        if (connections.Count == 0)
        {
            throw BankApiException.NotFound("This shopper has no connected bank accounts.");
        }
        return connections;
    }

    // ---- bank activity: local copy kept in step with Plaid via /transactions/sync ----

    public async Task<BankActivityResponse> GetActivityAsync(string shopperId, CancellationToken ct)
    {
        var connections = await BankConnectionsOfAsync(shopperId, ct);
        var warnings = new List<SyncWarning>();
        PlaidException? firstError = null;

        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection, ct);
            }
            catch (PlaidException ex)
            {
                firstError ??= ex;
                warnings.Add(new SyncWarning(connection.PublicId.ToString(), connection.InstitutionName, PlaidErrorMapper.Map(ex).Reason));
            }
        }

        if (warnings.Count == connections.Count && firstError is not null)
        {
            // nothing could be brought up to date; only answer if there is a previous copy to show
            var hasCopy = connections.Any(c => c.LastSyncedAt != null);
            if (!hasCopy) throw firstError;
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        var byId = connections.ToDictionary(c => c.Id);
        var transactions = rows
            .OrderByDescending(t => t.Date).ThenByDescending(t => t.Id)
            .Select(t => new ActivityTransaction(t.PlaidTransactionId, byId[t.ConnectionId].PublicId.ToString(),
                byId[t.ConnectionId].InstitutionName, t.AccountId, t.Name, t.Amount, t.IsoCurrencyCode,
                t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), t.Pending))
            .ToList();
        return new BankActivityResponse(shopperId, transactions, warnings);
    }

    private async Task SyncAsync(BankConnection connection, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync("conn:" + connection.Id, ct);
        await _db.Entry(connection).ReloadAsync(ct);
        var accessToken = _protector.Unprotect(connection.AccessTokenProtected);

        for (var restart = 0; ; restart++)
        {
            var startCursor = connection.TransactionsCursor;
            var cursor = startCursor;
            var added = new List<PlaidTransaction>();
            var modified = new List<PlaidTransaction>();
            var removed = new List<string>();

            try
            {
                bool hasMore;
                do
                {
                    var page = await WithNotReadyRetryAsync(() => _plaid.PostAsync<PlaidSyncResponse>("transactions/sync",
                        new { access_token = accessToken, cursor = string.IsNullOrEmpty(cursor) ? null : cursor, count = 500 }, ct), ct);
                    added.AddRange(page.Added);
                    modified.AddRange(page.Modified);
                    removed.AddRange(page.Removed.Select(r => r.TransactionId));
                    cursor = page.NextCursor;
                    hasMore = page.HasMore;
                } while (hasMore);
            }
            catch (PlaidException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && restart < 3)
            {
                continue; // the data changed under us: start over from the cursor we began with
            }

            await ApplyAsync(connection, added, modified, removed, cursor, ct);
            return;
        }
    }

    private async Task ApplyAsync(BankConnection connection, List<PlaidTransaction> added, List<PlaidTransaction> modified,
        List<string> removed, string? newCursor, CancellationToken ct)
    {
        var existing = await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id).ToListAsync(ct);
        var byPlaidId = existing.ToDictionary(t => t.PlaidTransactionId);

        foreach (var id in removed)
        {
            if (byPlaidId.Remove(id, out var row))
            {
                _db.BankTransactions.Remove(row);
            }
        }

        foreach (var tx in added.Concat(modified))
        {
            if (!byPlaidId.TryGetValue(tx.TransactionId, out var row))
            {
                row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = tx.TransactionId };
                _db.BankTransactions.Add(row);
                byPlaidId[tx.TransactionId] = row;
            }
            row.AccountId = tx.AccountId;
            row.Name = tx.Name ?? tx.MerchantName ?? string.Empty;
            row.Amount = tx.Amount;
            row.IsoCurrencyCode = tx.IsoCurrencyCode ?? tx.UnofficialCurrencyCode;
            row.Date = ParseDate(tx.Date);
            row.Pending = tx.Pending;
            row.PendingTransactionId = tx.PendingTransactionId;

            // A posted transaction replaces the pending one it settles.
            if (!string.IsNullOrEmpty(tx.PendingTransactionId) && byPlaidId.Remove(tx.PendingTransactionId, out var pendingRow))
            {
                _db.BankTransactions.Remove(pendingRow);
            }
        }

        if (!string.IsNullOrEmpty(newCursor))
        {
            connection.TransactionsCursor = newCursor;
        }
        connection.LastSyncedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    // ---- live lookup between two dates, straight from the bank ----

    public async Task<TransactionsLookupResponse> LookupAsync(string shopperId, string? from, string? to, CancellationToken ct)
    {
        if (!TryParseDay(from, out var start) || !TryParseDay(to, out var end))
        {
            throw BankApiException.BadRequest("from and to are required, as YYYY-MM-DD dates.");
        }
        if (start > end)
        {
            throw BankApiException.BadRequest("from must not be after to.");
        }

        var connections = await BankConnectionsOfAsync(shopperId, ct);
        var results = new List<LookupTransaction>();
        var warnings = new List<SyncWarning>();
        PlaidException? firstError = null;

        foreach (var connection in connections)
        {
            try
            {
                var accessToken = _protector.Unprotect(connection.AccessTokenProtected);
                foreach (var tx in await FetchRangeAsync(accessToken, start, end, ct))
                {
                    results.Add(new LookupTransaction(tx.TransactionId, connection.PublicId.ToString(), connection.InstitutionName,
                        tx.AccountId, tx.Name ?? tx.MerchantName ?? string.Empty, tx.Amount, tx.IsoCurrencyCode ?? tx.UnofficialCurrencyCode,
                        tx.Date, tx.Pending));
                }
            }
            catch (PlaidException ex)
            {
                firstError ??= ex;
                warnings.Add(new SyncWarning(connection.PublicId.ToString(), connection.InstitutionName, PlaidErrorMapper.Map(ex).Reason));
            }
        }

        if (warnings.Count == connections.Count && firstError is not null)
        {
            throw firstError;
        }

        results = results.OrderByDescending(t => t.Date, StringComparer.Ordinal).ToList();
        return new TransactionsLookupResponse(shopperId, from!.Trim(), to!.Trim(), results, warnings);
    }

    private async Task<List<PlaidTransaction>> FetchRangeAsync(string accessToken, DateTime start, DateTime end, CancellationToken ct)
    {
        const int pageSize = 500;
        var all = new List<PlaidTransaction>();
        var total = int.MaxValue;
        while (all.Count < total)
        {
            var offset = all.Count;
            // Banks connected before this lookup existed may not have the product yet; Plaid enables it on first use
            // and answers PRODUCT_NOT_READY for a short while.
            var page = await WithNotReadyRetryAsync(() => _plaid.PostAsync<PlaidTransactionsResponse>("transactions/get", new
            {
                access_token = accessToken,
                start_date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                end_date = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                options = new { count = pageSize, offset }
            }, ct), ct);
            total = page.TotalTransactions;
            if (page.Transactions.Count == 0) break;
            all.AddRange(page.Transactions);
        }
        return all;
    }

    private static async Task<T> WithNotReadyRetryAsync<T>(Func<Task<T>> call, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyDelays.Length)
            {
                await Task.Delay(NotReadyDelays[attempt], ct);
            }
        }
    }

    private static bool TryParseDay(string? value, out DateTime date) =>
        DateTime.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static DateTime ParseDate(string value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : DateTime.UtcNow.Date;
}
