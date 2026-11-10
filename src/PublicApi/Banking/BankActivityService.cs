using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record ActivityTransaction(
    string TransactionId,
    int ConnectionId,
    string? Bank,
    string AccountId,
    string Name,
    decimal Amount,
    DateTime Date,
    bool Pending);

/// <summary>
/// What support staff see of a shopper's connected banks: the shop's own, synced copy of the activity,
/// and live look-ups straight from the bank.
/// </summary>
public class BankActivityService
{
    private static readonly TimeSpan NotReadyTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan NotReadyPoll = TimeSpan.FromSeconds(2);
    private const int PAGE_SIZE = 500;
    private const int MAX_PAGES = 400;

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AccessTokenProtector _protector;
    private readonly ConnectionLocks _locks;

    public BankActivityService(ThePlaidApiClient plaid, CatalogContext db, UserManager<ApplicationUser> users,
        AccessTokenProtector protector, ConnectionLocks locks)
    {
        _plaid = plaid;
        _db = db;
        _users = users;
        _protector = protector;
        _locks = locks;
    }

    // ------------------------------------------------- the shop's own copy

    /// <summary>Brings the shop's copy of every connected bank's activity up to date, then returns it.</summary>
    public async Task<IReadOnlyList<ActivityTransaction>> GetActivityAsync(string email, CancellationToken ct)
    {
        var connections = await BankConnectionsOfShopperAsync(email, ct);

        foreach (var connection in connections)
        {
            await SyncAsync(connection.Id, ct);
        }

        var ids = connections.Select(c => c.Id).ToList();
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        var rows = await _db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        return rows
            .OrderByDescending(t => t.Date)
            .ThenBy(t => t.Id)
            .Select(t => new ActivityTransaction(t.PlaidTransactionId, t.ConnectionId, names.GetValueOrDefault(t.ConnectionId),
                t.AccountId, t.Name, t.Amount, t.Date, t.Pending))
            .ToList();
    }

    /// <summary>
    /// Applies Plaid's transaction changes (added, modified, removed) since the last cursor.
    /// A pending transaction that posts arrives as a removal of the pending one and an addition of the posted one.
    /// </summary>
    private async Task SyncAsync(int connectionId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync($"sync-{connectionId}", ct);

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId, ct);
        if (connection is null) return; // disconnected meanwhile
        var accessToken = _protector.Unprotect(connection);

        var runStart = connection.TransactionsCursor;
        var cursor = runStart;
        var deadline = DateTimeOffset.UtcNow + NotReadyTimeout;

        for (var page = 0; page < MAX_PAGES; page++)
        {
            TransactionsSyncResponse response;
            try
            {
                response = await PlaidCall.Run(() => _plaid.TransactionsSync(new TransactionsSyncOperationRequest
                {
                    Body = new TransactionsSyncRequest { AccessToken = accessToken, Cursor = string.IsNullOrEmpty(cursor) ? null : cursor, Count = PAGE_SIZE },
                }, cancellationToken: ct));
            }
            catch (PlaidCallException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")
            {
                // The data changed under us mid-pagination: Plaid says to start the whole run again.
                cursor = runStart;
                page = -1;
                continue;
            }

            // A bank that was connected without the transactions product starts out empty while Plaid prepares it.
            // That is "not ready", not "no activity": wait for it.
            if (response.TransactionsUpdateStatus == TransactionsUpdateStatus.NotReady && string.IsNullOrEmpty(cursor))
            {
                if (DateTimeOffset.UtcNow >= deadline)
                {
                    throw new BankingException(503, "The bank's activity is still being prepared; try again in a moment.");
                }
                await Task.Delay(NotReadyPoll, ct);
                page--;
                continue;
            }

            await ApplyAsync(connection, response, ct);
            cursor = response.NextCursor;
            connection.TransactionsCursor = cursor;
            await _db.SaveChangesAsync(ct);

            if (!response.HasMore) return;
        }
    }

    private async Task ApplyAsync(BankConnection connection, TransactionsSyncResponse response, CancellationToken ct)
    {
        var touched = response.Added.Select(t => t.TransactionId)
            .Concat(response.Modified.Select(t => t.TransactionId))
            .Concat(response.Removed.Select(t => t.TransactionId))
            .Distinct()
            .ToList();
        if (touched.Count == 0) return;

        var existing = (await _db.BankTransactions
                .Where(t => t.ConnectionId == connection.Id && touched.Contains(t.PlaidTransactionId))
                .ToListAsync(ct))
            .ToDictionary(t => t.PlaidTransactionId);

        foreach (var removed in response.Removed)
        {
            if (existing.Remove(removed.TransactionId, out var row)) _db.BankTransactions.Remove(row);
        }

        foreach (var plaid in response.Added.Concat(response.Modified))
        {
            if (!existing.TryGetValue(plaid.TransactionId, out var row))
            {
                row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = plaid.TransactionId };
                _db.BankTransactions.Add(row);
                existing[plaid.TransactionId] = row;
            }

            row.AccountId = plaid.AccountId;
            row.Name = plaid.Name;
            row.Amount = Money.Round(Money.FromDouble(plaid.Amount));
            row.IsoCurrencyCode = plaid.IsoCurrencyCode;
            row.Date = PlaidDate(plaid.Date);
            row.Pending = plaid.Pending;
        }
    }

    // ------------------------------------------ live, between two dates

    /// <summary>Asks each connected bank directly for the transactions between two dates (inclusive).</summary>
    public async Task<IReadOnlyList<ActivityTransaction>> GetTransactionsAsync(string email, DateTime from, DateTime to, CancellationToken ct)
    {
        if (from > to) throw new BankingException(400, "'from' must not be after 'to'.");

        var connections = await BankConnectionsOfShopperAsync(email, ct);
        var result = new List<ActivityTransaction>();

        foreach (var connection in connections)
        {
            var accessToken = _protector.Unprotect(connection);
            var deadline = DateTimeOffset.UtcNow + NotReadyTimeout;
            var collected = new List<Transaction>();

            // Page through everything in the range; a single call returns at most one page.
            for (var page = 0; page < MAX_PAGES; page++)
            {
                TransactionsGetResponse response;
                try
                {
                    var offset = collected.Count;
                    response = await PlaidCall.Run(() => _plaid.TransactionsGet(new TransactionsGetOperationRequest
                    {
                        Body = new TransactionsGetRequest
                        {
                            AccessToken = accessToken,
                            StartDate = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), TimeSpan.Zero),
                            EndDate = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Unspecified), TimeSpan.Zero),
                            Options = new TransactionsGetRequestOptions { Count = PAGE_SIZE, Offset = offset },
                        },
                    }, cancellationToken: ct));
                }
                catch (PlaidCallException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY")
                {
                    // Banks connected before this lookup existed don't have transactions prepared yet; the first
                    // request starts that, so wait for it.
                    if (DateTimeOffset.UtcNow >= deadline)
                    {
                        throw new BankingException(503, "The bank's transactions are still being prepared; try again in a moment.", ex);
                    }
                    await Task.Delay(NotReadyPoll, ct);
                    page--;
                    continue;
                }

                collected.AddRange(response.Transactions);
                if (response.Transactions.Count == 0 || collected.Count >= response.TotalTransactions) break;
            }

            result.AddRange(collected
                .GroupBy(t => t.TransactionId).Select(g => g.First())
                .Select(t => new ActivityTransaction(t.TransactionId, connection.Id, connection.InstitutionName, t.AccountId,
                    t.Name, Money.Round(Money.FromDouble(t.Amount)), PlaidDate(t.Date), t.Pending)));
        }

        return result.OrderByDescending(t => t.Date).ThenBy(t => t.TransactionId, StringComparer.Ordinal).ToList();
    }

    // -------------------------------------------------------------- shared

    /// <summary>Plaid's date-only values come through the SDK as local-midnight DateTimeOffsets; keep the calendar date.</summary>
    private static DateTime PlaidDate(DateTimeOffset value) => DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

    private async Task<List<BankConnection>> BankConnectionsOfShopperAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email)) throw new BankingException(400, "A shopper email is required.");
        var user = await _users.FindByEmailAsync(email.Trim()) ?? await _users.FindByNameAsync(email.Trim());
        if (user is null) throw new BankingException(404, "No such shopper.");

        var buyer = BankConnectionService.NormalizeBuyer(user.UserName ?? email);
        return await _db.BankConnections.AsNoTracking()
            .Where(c => c.BuyerId == buyer && c.Kind == BankConnection.KIND_BANK && c.HasDepositoryAccounts)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
    }
}
