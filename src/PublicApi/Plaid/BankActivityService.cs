using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record BankTransactionDto(
    string Name,
    decimal Amount,
    DateOnly Date,
    bool Pending,
    int ConnectionId,
    string? Bank,
    string AccountId);

public record BankStatusDto(int ConnectionId, string? Bank, bool UpToDate, string? Problem);

public record BankActivityDto(IReadOnlyList<BankTransactionDto> Transactions, IReadOnlyList<BankStatusDto> Banks);

public class BankActivityService
{
    private const int PageSize = 500;
    private const int NotReadyPolls = 10;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly AccessTokenProtector _protector;
    private readonly PerKeyLock _locks;
    private readonly UserManager<ApplicationUser> _users;

    public BankActivityService(ThePlaidApiClient plaid, CatalogContext db, AccessTokenProtector protector,
        PerKeyLock locks, UserManager<ApplicationUser> users)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
        _locks = locks;
        _users = users;
    }

    // ------------------------------------------------------- bank activity

    /// <summary>
    /// The shop's copy of the shopper's bank activity, first brought in step with the banks' records.
    /// </summary>
    public async Task<BankActivityDto> GetActivityAsync(string email, CancellationToken cancellationToken)
    {
        var connections = await GetBankConnectionsAsync(email, cancellationToken).ConfigureAwait(false);

        var statuses = new List<BankStatusDto>();
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection.Id, cancellationToken).ConfigureAwait(false);
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, true, null));
            }
            catch (ApiProblemException ex)
            {
                // One bank that can't be reached must not hide the others; say which one is stale.
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, false, ex.Message));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var stored = await _db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.BankConnectionId))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id).ToListAsync(cancellationToken).ConfigureAwait(false);
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);

        return new BankActivityDto(
            stored.Select(t => new BankTransactionDto(t.Name, t.Amount, DateOnly.FromDateTime(t.Date), t.Pending,
                t.BankConnectionId, names.GetValueOrDefault(t.BankConnectionId), t.AccountId)).ToList(),
            statuses);
    }

    /// <summary>
    /// Brings the local copy of one connection's transactions in step with the bank: new, changed and removed
    /// transactions (a pending transaction that posts is replaced by its posted version).
    /// </summary>
    private async Task SyncAsync(int connectionId, CancellationToken cancellationToken)
    {
        using var _ = await _locks.AcquireAsync("sync:" + connectionId, cancellationToken).ConfigureAwait(false);

        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId, cancellationToken).ConfigureAwait(false);
        if (connection == null)
            return;
        var accessToken = _protector.Unprotect(connection.ProtectedAccessToken);

        for (var attempt = 0; ; attempt++)
        {
            var cursor = connection.TransactionsCursor;
            var added = new Dictionary<string, Transaction>();
            var removed = new HashSet<string>();
            var notReadyPolls = 0;
            try
            {
                var hasMore = true;
                while (hasMore)
                {
                    var page = await PlaidCall.RunAsync(() => _plaid.TransactionsSync(new()
                    {
                        Body = new() { AccessToken = accessToken, Cursor = cursor, Count = PageSize }
                    })).ConfigureAwait(false);

                    if (cursor == null && string.IsNullOrEmpty(page.NextCursor) &&
                        page.TransactionsUpdateStatus != TransactionsUpdateStatus.HistoricalUpdateComplete)
                    {
                        // Right after linking, the bank's data is still being fetched.
                        if (++notReadyPolls > NotReadyPolls)
                            throw new ApiProblemException(409, "The bank's transactions are not ready yet; try again shortly.");
                        await Task.Delay(NotReadyDelay, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    foreach (var t in page.Added.Concat(page.Modified))
                    {
                        added[t.TransactionId] = t;
                        removed.Remove(t.TransactionId);
                    }
                    foreach (var r in page.Removed)
                    {
                        added.Remove(r.TransactionId);
                        removed.Add(r.TransactionId);
                    }

                    if (!string.IsNullOrEmpty(page.NextCursor))
                        cursor = page.NextCursor;
                    hasMore = page.HasMore;
                }
            }
            catch (Exception ex) when (attempt < 2 &&
                                       PlaidCall.IsCode(ex, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION"))
            {
                continue; // the data changed under us: start the run over from the saved cursor
            }

            await ApplyAsync(connection, added, removed, cursor, cancellationToken).ConfigureAwait(false);
            return;
        }
    }

    private async Task ApplyAsync(BankConnection connection, Dictionary<string, Transaction> changed, HashSet<string> removed,
        string? cursor, CancellationToken cancellationToken)
    {
        var touched = changed.Keys.Concat(removed).ToList();
        var existing = new Dictionary<string, BankTransaction>();
        foreach (var chunk in touched.Chunk(500))
        {
            var ids = chunk.ToList();
            var rows = await _db.BankTransactions
                .Where(t => t.BankConnectionId == connection.Id && ids.Contains(t.PlaidTransactionId))
                .ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var row in rows)
                existing[row.PlaidTransactionId] = row;
        }

        foreach (var id in removed)
        {
            if (existing.TryGetValue(id, out var row))
                _db.BankTransactions.Remove(row);
        }

        foreach (var (id, t) in changed)
        {
            if (!existing.TryGetValue(id, out var row))
            {
                row = new BankTransaction { BankConnectionId = connection.Id, PlaidTransactionId = id };
                _db.BankTransactions.Add(row);
            }
            row.AccountId = t.AccountId;
            row.Name = t.Name;
            row.Amount = (decimal)t.Amount;
            row.Date = t.Date.Date; // the calendar date as named by the bank
            row.Pending = t.Pending;
        }

        // The cursor moves together with the data it describes, in one save.
        if (!string.IsNullOrEmpty(cursor))
            connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    // ------------------------------------------------ transactions by date

    /// <summary>
    /// The transactions on all the shopper's connected banks between two dates, asked of the banks directly.
    /// </summary>
    public async Task<BankActivityDto> GetTransactionsBetweenAsync(string email, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        var connections = await GetBankConnectionsAsync(email, cancellationToken).ConfigureAwait(false);

        var transactions = new List<BankTransactionDto>();
        var statuses = new List<BankStatusDto>();
        ApiProblemException? firstProblem = null;
        foreach (var connection in connections)
        {
            try
            {
                var accessToken = _protector.Unprotect(connection.ProtectedAccessToken);
                var rows = await FetchBetweenAsync(accessToken, from, to, cancellationToken).ConfigureAwait(false);
                transactions.AddRange(rows.Select(t => new BankTransactionDto(t.Name, (decimal)t.Amount, DateOnly.FromDateTime(t.Date.Date),
                    t.Pending, connection.Id, connection.InstitutionName, t.AccountId)));
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, true, null));
            }
            catch (ApiProblemException ex)
            {
                firstProblem ??= ex;
                statuses.Add(new BankStatusDto(connection.Id, connection.InstitutionName, false, ex.Message));
            }
        }

        // If no bank could answer, there is nothing to show but the reason.
        if (firstProblem != null && statuses.All(s => !s.UpToDate))
            throw firstProblem;

        return new BankActivityDto(
            transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Name, StringComparer.Ordinal).ToList(), statuses);
    }

    private async Task<List<Transaction>> FetchBetweenAsync(string accessToken, DateOnly from, DateOnly to,
        CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var all = new Dictionary<string, Transaction>();
        var offset = 0;
        var notReadyPolls = 0;
        while (true)
        {
            TransactionsGetResponse page;
            try
            {
                var currentOffset = offset;
                page = await PlaidCall.RunAsync(() => _plaid.TransactionsGet(new()
                {
                    Body = new()
                    {
                        AccessToken = accessToken,
                        StartDate = start,
                        EndDate = end,
                        Options = new() { Count = PageSize, Offset = currentOffset }
                    }
                })).ConfigureAwait(false);
            }
            catch (Exception ex) when (PlaidCall.IsCode(ex, "PRODUCT_NOT_READY") && notReadyPolls < NotReadyPolls)
            {
                notReadyPolls++;
                await Task.Delay(NotReadyDelay, cancellationToken).ConfigureAwait(false);
                continue;
            }

            foreach (var t in page.Transactions)
                all[t.TransactionId] = t;
            offset += page.Transactions.Count;
            if (page.Transactions.Count == 0 || offset >= page.TotalTransactions)
                break;
        }
        return all.Values.ToList();
    }

    // ------------------------------------------------------------- helpers

    private async Task<List<BankConnection>> GetBankConnectionsAsync(string email, CancellationToken cancellationToken)
    {
        var key = CurrentUser.Key(email);
        var all = await _db.BankConnections.AsNoTracking().Where(c => c.UserKey == key)
            .OrderBy(c => c.Id).ToListAsync(cancellationToken).ConfigureAwait(false);

        if (all.Count == 0 && await _users.FindByNameAsync(email.Trim()) == null && await _users.FindByEmailAsync(email.Trim()) == null)
            throw ApiProblemException.NotFound("There is no such shopper.");

        return all.Where(c => c.Kind == BankConnectionKind.Bank).ToList();
    }
}
