using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

public record ActivityTransactionDto(
    int ConnectionId,
    string? InstitutionName,
    string AccountId,
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Pending);

public record LookupTransactionDto(
    int ConnectionId,
    string? InstitutionName,
    string AccountId,
    string Name,
    decimal Amount,
    DateOnly Date);

/// <summary>Support's view of a shopper's bank accounts: the shop's synced copy, and live look-ups.</summary>
public class BankActivityService
{
    private const int PageSize = 500;
    private const int MaxSyncRestarts = 5;
    private const int MaxFirstSyncPolls = 10;

    private readonly CatalogContext _db;
    private readonly PlaidGateway _plaid;
    private readonly BankTokenProtector _tokens;

    public BankActivityService(CatalogContext db, PlaidGateway plaid, BankTokenProtector tokens)
    {
        _db = db;
        _plaid = plaid;
        _tokens = tokens;
    }

    // ------------------------------------------------------ bank activity

    public async Task<IReadOnlyList<ActivityTransactionDto>> GetActivityAsync(string email)
    {
        var connections = await BankConnectionsOfAsync(email);

        // Bring the shop's copy in step with the banks' records first, one bank at a time.
        foreach (var connection in connections)
        {
            using var _ = await KeyedLock.EnterAsync("connection:" + connection.Id);
            await _db.Entry(connection).ReloadAsync();
            await SyncAsync(connection);
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.BankTransactions.Where(t => ids.Contains(t.ConnectionId)).ToListAsync();
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        return rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.PlaidTransactionId, StringComparer.Ordinal)
            .Select(t => new ActivityTransactionDto(t.ConnectionId, names.GetValueOrDefault(t.ConnectionId), t.AccountId, t.Name, t.Amount,
                DateOnly.FromDateTime(t.Date), t.Pending))
            .ToList();
    }

    /// <summary>
    /// Runs /transactions/sync until Plaid has nothing more, then applies the whole pass at once:
    /// removed rows are deleted, added rows inserted and modified rows updated; pending transactions
    /// that settle arrive as a removal plus an addition. If the data changes while paging
    /// (TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION) the pass restarts from the cursor it began with.
    /// </summary>
    private async Task SyncAsync(BankConnection connection)
    {
        var accessToken = _tokens.Unprotect(connection);
        var startCursor = connection.TransactionsCursor;

        for (var restart = 0; ; restart++)
        {
            var added = new List<Transaction>();
            var modified = new List<Transaction>();
            var removed = new List<string>();
            var cursor = startCursor;
            var polls = 0;
            var quietPolls = 0;
            try
            {
                while (true)
                {
                    var page = await SyncPageAsync(accessToken, cursor);
                    added.AddRange(page.Added);
                    modified.AddRange(page.Modified);
                    removed.AddRange(page.Removed.Select(r => r.TransactionId));
                    cursor = page.NextCursor;
                    if (page.HasMore) continue;

                    // Plaid hands over a freshly connected bank's history in stages and says it is complete a
                    // little before all of it has arrived. On a first sync, poll until two passes in a row bring
                    // nothing new; afterwards one pass is enough.
                    if (!string.IsNullOrEmpty(startCursor) || ++polls >= MaxFirstSyncPolls) break;
                    var changed = page.Added.Count + page.Modified.Count + page.Removed.Count > 0;
                    var complete = page.TransactionsUpdateStatus == TransactionsUpdateStatus.HistoricalUpdateComplete;
                    quietPolls = !changed && complete ? quietPolls + 1 : 0;
                    if (quietPolls >= 2) break;
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && restart < MaxSyncRestarts)
            {
                continue;
            }

            await ApplyAsync(connection, added, modified, removed, cursor);
            return;
        }
    }

    /// <summary>
    /// A connection whose transactions Plaid has not initialised yet answers 200 with status NOT_READY and no
    /// data (and an empty cursor); wait for the data instead of recording an empty pass.
    /// </summary>
    private async Task<TransactionsSyncResponse> SyncPageAsync(string accessToken, string? cursor)
    {
        for (var attempt = 1; ; attempt++)
        {
            var page = await PlaidRetry.WhenReadyAsync(() => _plaid.Call(c => c.TransactionsSync(new TransactionsSyncOperationRequest
            {
                Body = new TransactionsSyncRequest
                {
                    AccessToken = accessToken,
                    Cursor = cursor,
                    Count = PageSize,
                    Options = new TransactionsSyncRequestOptions { DaysRequested = 730 }
                }
            })));
            if (page.TransactionsUpdateStatus != TransactionsUpdateStatus.NotReady) return page;
            if (attempt >= 8) throw Fail.Conflict("The bank's transactions are not ready yet; try again shortly.");
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    private async Task ApplyAsync(BankConnection connection, List<Transaction> added, List<Transaction> modified, List<string> removed, string? cursor)
    {
        var touched = removed.Concat(added.Select(t => t.TransactionId)).Concat(modified.Select(t => t.TransactionId)).Distinct().ToList();
        var existing = new Dictionary<string, BankTransaction>();
        foreach (var chunk in touched.Chunk(1000))
        {
            var ids = chunk.ToList();
            var rows = await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id && ids.Contains(t.PlaidTransactionId)).ToListAsync();
            foreach (var row in rows) existing[row.PlaidTransactionId] = row;
        }

        foreach (var id in removed)
        {
            if (existing.Remove(id, out var row)) _db.BankTransactions.Remove(row);
        }

        foreach (var t in added.Concat(modified))
        {
            if (!existing.TryGetValue(t.TransactionId, out var row))
            {
                row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = t.TransactionId };
                _db.BankTransactions.Add(row);
                existing[t.TransactionId] = row;
            }
            row.AccountId = t.AccountId;
            row.Name = string.IsNullOrWhiteSpace(t.Name) ? (t.MerchantName ?? string.Empty) : t.Name;
            row.Amount = Money.FromDouble(t.Amount);
            row.Date = t.Date.Date;
            row.Pending = t.Pending;
        }

        connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync();
    }

    // ------------------------------------------- transactions between dates

    public async Task<IReadOnlyList<LookupTransactionDto>> GetTransactionsAsync(string email, string? fromText, string? toText)
    {
        var from = ParseDate(fromText, "from");
        var to = ParseDate(toText, "to");
        if (from > to) throw Fail.BadRequest("'from' must not be after 'to'.");

        var connections = await BankConnectionsOfAsync(email);
        var result = new List<LookupTransactionDto>();

        foreach (var connection in connections)
        {
            var accessToken = _tokens.Unprotect(connection);
            var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

            var fetched = new List<Transaction>();
            var total = int.MaxValue;
            while (fetched.Count < total)
            {
                var offset = fetched.Count;
                var page = await PlaidRetry.WhenReadyAsync(() => _plaid.Call(c => c.TransactionsGet(new TransactionsGetOperationRequest
                {
                    Body = new TransactionsGetRequest
                    {
                        AccessToken = accessToken,
                        StartDate = start,
                        EndDate = end,
                        Options = new TransactionsGetRequestOptions { Count = PageSize, Offset = offset }
                    }
                })));
                total = page.TotalTransactions;
                if (page.Transactions.Count == 0) break;
                fetched.AddRange(page.Transactions);
            }

            result.AddRange(fetched.Select(t => new LookupTransactionDto(connection.Id, connection.InstitutionName, t.AccountId,
                string.IsNullOrWhiteSpace(t.Name) ? (t.MerchantName ?? string.Empty) : t.Name,
                Money.FromDouble(t.Amount), DateOnly.FromDateTime(t.Date.DateTime))));
        }

        return result.OrderByDescending(t => t.Date).ToList();
    }

    // -------------------------------------------------------------- helpers

    private async Task<List<BankConnection>> BankConnectionsOfAsync(string email)
    {
        var userId = Caller.Normalize(email);
        var connections = await _db.BankConnections
            .Where(c => c.UserId == userId && c.Kind == BankConnectionKind.Bank)
            .OrderBy(c => c.Id).ToListAsync();
        if (connections.Count == 0) throw Fail.NotFound($"{email} has no connected bank accounts.");
        return connections;
    }

    private static DateOnly ParseDate(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text)) throw Fail.BadRequest($"'{name}' is required (YYYY-MM-DD).");
        if (!DateOnly.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw Fail.BadRequest($"'{name}' must be a date in the form YYYY-MM-DD.");
        return date;
    }
}
