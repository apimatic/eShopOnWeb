using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record BankActivityTransactionDto(
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Pending,
    string? IsoCurrencyCode,
    int ConnectionId,
    string? InstitutionName,
    string AccountId);

public sealed record SyncProblemDto(int ConnectionId, string? InstitutionName, string Reason);

public sealed record BankActivityDto(
    string Email,
    IReadOnlyList<BankActivityTransactionDto> Transactions,
    IReadOnlyList<SyncProblemDto> Problems);

public sealed record DatedTransactionDto(
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Pending,
    string? IsoCurrencyCode,
    int ConnectionId,
    string? InstitutionName,
    string AccountId);

public sealed record DatedTransactionsDto(
    string Email,
    DateOnly From,
    DateOnly To,
    IReadOnlyList<DatedTransactionDto> Transactions,
    IReadOnlyList<SyncProblemDto> Problems);

public sealed class BankActivityService
{
    private const int PageSize = 500;
    private const int NotReadyAttempts = 8;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> SyncLocks = new();

    private readonly PlaidGateway _plaid;
    private readonly CatalogContext _db;

    public BankActivityService(PlaidGateway plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    private Task<List<BankConnection>> BankConnectionsAsync(string userId) =>
        _db.BankConnections.Where(c => c.UserId == userId && c.Kind == BankConnectionKinds.Bank).OrderBy(c => c.Id).ToListAsync();

    // ---- the shop's own copy of the activity ---------------------------------------------------------------

    /// <summary>Brings the shop's copy of every connected bank up to date with the bank's records, then reads it.</summary>
    public async Task<BankActivityDto> GetActivityAsync(string userId, string email)
    {
        var connections = await BankConnectionsAsync(userId);
        var problems = new List<SyncProblemDto>();
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection);
            }
            catch (PlaidApiException ex)
            {
                problems.Add(new SyncProblemDto(connection.Id, connection.InstitutionName, ex.Message));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.BankTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.BankConnectionId))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id)
            .ToListAsync();
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        var transactions = rows.Select(t => new BankActivityTransactionDto(
            t.Name, t.Amount, DateOnly.FromDateTime(t.Date), t.Pending, t.IsoCurrencyCode,
            t.BankConnectionId, names.GetValueOrDefault(t.BankConnectionId), t.AccountId)).ToList();
        return new BankActivityDto(email, transactions, problems);
    }

    private async Task SyncAsync(BankConnection connection)
    {
        var gate = SyncLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _db.Entry(connection).ReloadAsync();
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await SyncOnceAsync(connection);
                    return;
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
                {
                    // A fresh connection is still being read by Plaid.
                    await Task.Delay(NotReadyDelay);
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && attempt < NotReadyAttempts)
                {
                    // The bank's records changed mid-way: start the pages again from the stored cursor.
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SyncOnceAsync(BankConnection connection)
    {
        var cursor = connection.TransactionsCursor;
        var added = new List<Transaction>();
        var modified = new List<Transaction>();
        var removed = new List<string>();

        // Read every page first. The cursor is only kept once all pages are in, as Plaid requires.
        bool hasMore;
        do
        {
            var page = await _plaid.CallAsync(c => c.TransactionsSync(new TransactionsSyncOperationRequest
            {
                Body = new TransactionsSyncRequest
                {
                    AccessToken = connection.AccessToken,
                    Cursor = string.IsNullOrEmpty(cursor) ? null : cursor,
                    Count = PageSize,
                }
            }));
            added.AddRange(page.Added);
            modified.AddRange(page.Modified);
            removed.AddRange(page.Removed.Select(r => r.TransactionId));
            cursor = page.NextCursor;
            hasMore = page.HasMore;
        } while (hasMore);

        var existing = await _db.BankTransactions
            .Where(t => t.BankConnectionId == connection.Id)
            .ToDictionaryAsync(t => t.PlaidTransactionId);

        // Replay in the order the bank made the changes: added, modified, removed.
        foreach (var t in added.Concat(modified))
        {
            if (!existing.TryGetValue(t.TransactionId, out var row))
            {
                row = new BankTransaction { BankConnectionId = connection.Id, PlaidTransactionId = t.TransactionId };
                _db.BankTransactions.Add(row);
                existing[t.TransactionId] = row;
            }
            row.AccountId = t.AccountId;
            row.Name = t.Name;
            row.Amount = Convert.ToDecimal(t.Amount, CultureInfo.InvariantCulture);
            row.IsoCurrencyCode = t.IsoCurrencyCode;
            row.Date = t.Date.Date;
            row.Pending = t.Pending;
        }

        foreach (var id in removed)
        {
            if (existing.TryGetValue(id, out var row))
            {
                if (_db.Entry(row).State == EntityState.Added) _db.Entry(row).State = EntityState.Detached;
                else _db.BankTransactions.Remove(row);
                existing.Remove(id);
            }
        }

        connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync();
    }

    // ---- straight from the bank ----------------------------------------------------------------------------

    public async Task<DatedTransactionsDto> GetBetweenAsync(string userId, string email, DateOnly from, DateOnly to)
    {
        var connections = await BankConnectionsAsync(userId);
        var transactions = new List<DatedTransactionDto>();
        var problems = new List<SyncProblemDto>();

        foreach (var connection in connections)
        {
            try
            {
                foreach (var t in await FetchAsync(connection, from, to))
                {
                    transactions.Add(new DatedTransactionDto(
                        t.Name, Convert.ToDecimal(t.Amount, CultureInfo.InvariantCulture), DateOnly.FromDateTime(t.Date.Date),
                        t.Pending, t.IsoCurrencyCode, connection.Id, connection.InstitutionName, t.AccountId));
                }
            }
            catch (PlaidApiException ex)
            {
                problems.Add(new SyncProblemDto(connection.Id, connection.InstitutionName, ex.Message));
            }
        }

        var ordered = transactions.OrderByDescending(t => t.Date).ThenBy(t => t.ConnectionId).ToList();
        return new DatedTransactionsDto(email, from, to, ordered, problems);
    }

    private async Task<List<Transaction>> FetchAsync(BankConnection connection, DateOnly from, DateOnly to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var all = new List<Transaction>();
                int total;
                do
                {
                    var offset = all.Count;
                    var page = await _plaid.CallAsync(c => c.TransactionsGet(new TransactionsGetOperationRequest
                    {
                        Body = new TransactionsGetRequest
                        {
                            AccessToken = connection.AccessToken,
                            StartDate = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                            EndDate = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                            Options = new TransactionsGetRequestOptions { Count = PageSize, Offset = offset },
                        }
                    }));
                    total = page.TotalTransactions;
                    all.AddRange(page.Transactions);
                    if (page.Transactions.Count == 0) break;
                } while (all.Count < total);
                return all;
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
            {
                await Task.Delay(NotReadyDelay);
            }
        }
    }
}
