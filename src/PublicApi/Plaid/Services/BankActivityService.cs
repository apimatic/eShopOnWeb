using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Plaid.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

public sealed record ActivityLine(string Name, decimal Amount, DateTime Date, bool Pending, Guid ConnectionId,
    string InstitutionName, string AccountId);

public sealed record ConnectionProblem(Guid ConnectionId, string InstitutionName, string Reason);

public sealed record ActivityResult(IReadOnlyList<ActivityLine> Transactions, IReadOnlyList<ConnectionProblem> Problems);

/// <summary>What support staff look at: the shop's synced copy, and live lookups by date.</summary>
public sealed class BankActivityService
{
    private readonly PlaidContext _db;
    private readonly PlaidAccess _plaid;
    private readonly UserManager<ApplicationUser> _users;

    public BankActivityService(PlaidContext db, PlaidAccess plaid, UserManager<ApplicationUser> users)
    {
        _db = db;
        _plaid = plaid;
        _users = users;
    }

    private async Task<List<BankConnection>> BankConnectionsAsync(string email, CancellationToken ct)
    {
        var user = BankConnectionService.Normalize(email);
        var connections = await _db.Connections.Include(c => c.Accounts)
            .Where(c => c.UserName == user && c.IsBank).OrderBy(c => c.CreatedAt).ToListAsync(ct);
        if (connections.Count == 0
            && !await _db.Connections.AnyAsync(c => c.UserName == user, ct)
            && await _users.FindByEmailAsync(email.Trim()) is null
            && await _users.FindByNameAsync(email.Trim()) is null)
        {
            throw BankApiException.NotFound("There is no such shopper.");
        }
        return connections;
    }

    // ---- the shop's own copy ---------------------------------------------------------------------------------

    public async Task<ActivityResult> GetActivityAsync(string email, CancellationToken ct)
    {
        var connections = await BankConnectionsAsync(email, ct);
        var problems = new List<ConnectionProblem>();

        // Bring the copy in step with the banks first; a bank that can't be reached keeps its older copy.
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection, ct);
            }
            catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped)
            {
                problems.Add(new ConnectionProblem(connection.Id, connection.InstitutionName, mapped.Message));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.Transactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        var byId = connections.ToDictionary(c => c.Id);
        var lines = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new ActivityLine(t.Name, t.Amount, t.Date, t.Pending, t.ConnectionId,
                byId[t.ConnectionId].InstitutionName, t.PlaidAccountId))
            .ToList();
        return new ActivityResult(lines, problems);
    }

    /// <summary>
    /// Applies Plaid's changes (added, modified, removed) since the stored cursor. All pages of one run are collected
    /// first and applied together with the new cursor, so the copy never holds half an update. If the bank's data
    /// changes while paging, Plaid asks to start again from the old cursor, and so do we.
    /// </summary>
    private async Task SyncAsync(BankConnection connection, CancellationToken ct)
    {
        using var _ = await PlaidAccess.LockAsync(connection.Id, ct);
        await _db.Entry(connection).ReloadAsync(ct);
        var accessToken = _plaid.Unprotect(connection.ProtectedAccessToken);

        for (var restart = 0; ; restart++)
        {
            var startCursor = connection.TransactionsCursor;
            var cursor = startCursor;
            var added = new List<Transaction>();
            var modified = new List<Transaction>();
            var removed = new List<string>();

            try
            {
                bool more;
                do
                {
                    var current = cursor;
                    var page = await _plaid.WhenProductReady(() => _plaid.Client.TransactionsSync(new TransactionsSyncOperationRequest
                    {
                        Body = new TransactionsSyncRequest { AccessToken = accessToken, Cursor = current, Count = 500 },
                    }, null, ct), ct);
                    added.AddRange(page.Added);
                    modified.AddRange(page.Modified);
                    removed.AddRange(page.Removed.Select(r => r.TransactionId));
                    cursor = page.NextCursor;
                    more = page.HasMore;
                } while (more);
            }
            catch (Exception e) when (restart < 3 && PlaidFailure.HasCode(e, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION"))
            {
                continue;
            }

            var touched = added.Concat(modified).GroupBy(t => t.TransactionId).Select(g => g.Last()).ToList();
            var removedIds = removed.Distinct().ToList();
            var existing = await _db.Transactions
                .Where(t => t.ConnectionId == connection.Id && (touched.Select(x => x.TransactionId).Contains(t.TransactionId) || removedIds.Contains(t.TransactionId)))
                .ToDictionaryAsync(t => t.TransactionId, ct);

            foreach (var t in touched)
            {
                if (!existing.TryGetValue(t.TransactionId, out var row))
                {
                    row = new BankTransaction { ConnectionId = connection.Id, TransactionId = t.TransactionId };
                    _db.Transactions.Add(row);
                }
                row.PlaidAccountId = t.AccountId;
                row.Name = t.Name;
                row.Amount = decimal.Round((decimal)t.Amount, 2);
                row.Date = t.Date.Date;
                row.Pending = t.Pending;
            }

            foreach (var id in removedIds)
            {
                if (existing.TryGetValue(id, out var row) && !touched.Any(t => t.TransactionId == id))
                    _db.Transactions.Remove(row);
            }

            connection.TransactionsCursor = cursor;
            connection.LastSyncedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }
    }

    // ---- straight from the bank ------------------------------------------------------------------------------

    public async Task<ActivityResult> GetTransactionsBetweenAsync(string email, DateTime from, DateTime to, CancellationToken ct)
    {
        var connections = await BankConnectionsAsync(email, ct);
        var lines = new List<ActivityLine>();
        var problems = new List<ConnectionProblem>();

        foreach (var connection in connections)
        {
            try
            {
                lines.AddRange(await FetchRangeAsync(connection, from, to, ct));
            }
            catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped)
            {
                problems.Add(new ConnectionProblem(connection.Id, connection.InstitutionName, mapped.Message));
            }
        }

        // Every bank failing is a failure of the request, not an empty answer.
        if (connections.Count > 0 && problems.Count == connections.Count)
            throw new BankApiException(409, "The banks' transactions could not be read: " + string.Join("; ", problems.Select(p => $"{p.InstitutionName}: {p.Reason}")));

        return new ActivityResult(lines.OrderByDescending(l => l.Date).ThenBy(l => l.Name, StringComparer.Ordinal).ToList(), problems);
    }

    private async Task<List<ActivityLine>> FetchRangeAsync(BankConnection connection, DateTime from, DateTime to, CancellationToken ct)
    {
        var accessToken = _plaid.Unprotect(connection.ProtectedAccessToken);
        var start = new DateTimeOffset(from.Year, from.Month, from.Day, 0, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(to.Year, to.Month, to.Day, 0, 0, 0, TimeSpan.Zero);
        var result = new List<ActivityLine>();
        var seen = new HashSet<string>();

        const int pageSize = 500;
        for (var offset = 0; ; )
        {
            var current = offset;
            var page = await _plaid.WhenProductReady(() => _plaid.Client.TransactionsGet(new TransactionsGetOperationRequest
            {
                Body = new TransactionsGetRequest
                {
                    AccessToken = accessToken,
                    StartDate = start,
                    EndDate = end,
                    Options = new TransactionsGetRequestOptions { Count = pageSize, Offset = current },
                },
            }, PlaidAccess.DateOnlyBody, ct), ct);

            foreach (var t in page.Transactions)
            {
                if (seen.Add(t.TransactionId))
                    result.Add(new ActivityLine(t.Name, decimal.Round((decimal)t.Amount, 2), t.Date.Date, t.Pending,
                        connection.Id, connection.InstitutionName, t.AccountId));
            }

            offset += page.Transactions.Count;
            if (page.Transactions.Count == 0 || offset >= page.TotalTransactions) break;
        }
        return result;
    }
}
