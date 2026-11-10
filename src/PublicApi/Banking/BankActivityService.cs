using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankTransactionDto(string Name, decimal Amount, DateOnly Date, bool Pending, int ConnectionId, string AccountId, string? Institution);

public record UnavailableBank(int ConnectionId, string? Institution, string Reason);

public record LookupResult(List<BankTransactionDto> Transactions, List<UnavailableBank> Unavailable);

/// <summary>What support staff see of a shopper's connected banks.</summary>
public class BankActivityService(
    ThePlaidApiClient plaid,
    CatalogContext db,
    UserManager<ApplicationUser> users,
    AccessTokenProtector protector,
    KeyedLocks locks)
{
    private const int PageSize = 500;

    private async Task<List<BankConnection>> BankConnectionsOfAsync(string email)
    {
        var wanted = email.Trim().ToLowerInvariant();
        var user = await users.FindByEmailAsync(email.Trim()) ?? await users.FindByNameAsync(email.Trim());
        var names = new List<string> { wanted };
        if (user?.UserName is { } userName)
        {
            names.Add(userName.ToLowerInvariant());
        }

        var connections = await db.BankConnections.Where(c => names.Contains(c.UserName) && c.Kind == BankConnectionKind.Bank)
            .OrderBy(c => c.Id).ToListAsync();
        if (user is null && connections.Count == 0)
        {
            throw new RequestFailedException(404, $"No shopper with the email {email} was found.");
        }

        return connections;
    }

    /// <summary>
    /// The shop's own copy of the activity of every bank the shopper connected, brought up to date with the bank's records first.
    /// </summary>
    public async Task<LookupResult> GetActivityAsync(string email)
    {
        var connections = await BankConnectionsOfAsync(email);
        var unavailable = new List<UnavailableBank>();
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection);
            }
            catch (Exception e) when (PlaidError.Translate(e) is { } failure)
            {
                // The copy kept so far is still shown for this bank.
                unavailable.Add(new UnavailableBank(connection.Id, connection.InstitutionName, failure.Message));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        var rows = await db.BankTransactions.Where(t => ids.Contains(t.ConnectionId))
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id).ToListAsync();

        if (connections.Count > 0 && unavailable.Count == connections.Count && rows.Count == 0)
        {
            throw new RequestFailedException(502, "Bank activity could not be retrieved: " + unavailable[0].Reason);
        }

        return new LookupResult(
            rows.Select(t => new BankTransactionDto(t.Name, t.Amount, DateOnly.FromDateTime(t.Date), t.Pending, t.ConnectionId, t.AccountId, names[t.ConnectionId])).ToList(),
            unavailable);
    }

    /// <summary>Plaid's /transactions/sync: apply what was added, modified and removed since the saved cursor.</summary>
    private async Task SyncAsync(BankConnection connection)
    {
        using var _ = await locks.AcquireAsync($"sync:{connection.Id}");
        var accessToken = protector.Unprotect(connection);
        var startCursor = connection.Cursor;

        List<Transaction> added, modified;
        List<RemovedTransaction> removed;
        string? lastCursor;

        for (var restarts = 0; ; restarts++)
        {
            added = new();
            modified = new();
            removed = new();
            lastCursor = startCursor;
            try
            {
                var cursor = startCursor;
                var waited = 0;
                while (true)
                {
                    var page = await PlaidError.Call(() => plaid.TransactionsSync(new TransactionsSyncOperationRequest
                    {
                        Body = new TransactionsSyncRequest { AccessToken = accessToken, Cursor = cursor, Count = PageSize },
                    }));

                    if (page.TransactionsUpdateStatus == TransactionsUpdateStatus.NotReady && waited < 15)
                    {
                        // The first sync of a new Item: Plaid is still fetching the history.
                        waited++;
                        await Task.Delay(1500);
                        continue;
                    }

                    added.AddRange(page.Added);
                    modified.AddRange(page.Modified);
                    removed.AddRange(page.Removed);
                    if (!string.IsNullOrEmpty(page.NextCursor))
                    {
                        cursor = page.NextCursor;
                        lastCursor = page.NextCursor;
                    }

                    if (!page.HasMore)
                    {
                        break;
                    }
                }

                break;
            }
            catch (Exception e) when (restarts < 3 && PlaidError.Is(e, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION"))
            {
                // The data changed while paging; Plaid wants the whole sync repeated from the same cursor.
            }
            catch (Exception e) when (PlaidError.Is(e, "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION"))
            {
                throw new RequestFailedException(503, "The bank's records keep changing; try again in a moment.");
            }
        }

        await ApplyAsync(connection, added, modified, removed, lastCursor);
    }

    private async Task ApplyAsync(BankConnection connection, List<Transaction> added, List<Transaction> modified,
        List<RemovedTransaction> removed, string? lastCursor)
    {
        var touched = added.Concat(modified).Select(t => t.TransactionId)
            .Concat(removed.Select(r => r.TransactionId)).Distinct().ToList();

        var existing = new Dictionary<string, BankTransaction>();
        foreach (var chunk in touched.Chunk(500))
        {
            var ids = chunk.ToList();
            var rows = await db.BankTransactions.Where(t => t.ConnectionId == connection.Id && ids.Contains(t.PlaidTransactionId)).ToListAsync();
            foreach (var row in rows)
            {
                existing[row.PlaidTransactionId] = row;
            }
        }

        foreach (var transaction in added.Concat(modified))
        {
            if (!existing.TryGetValue(transaction.TransactionId, out var row))
            {
                row = new BankTransaction(connection.Id, transaction.TransactionId);
                existing[transaction.TransactionId] = row;
                db.BankTransactions.Add(row);
            }

            row.Update(transaction.AccountId, transaction.Name, Money.FromPlaid(transaction.Amount),
                BankDates.CalendarDate(transaction.Date), transaction.Pending, transaction.PendingTransactionId);
        }

        foreach (var gone in removed)
        {
            // e.g. a pending transaction that posted: the posted one arrived as an addition.
            if (existing.TryGetValue(gone.TransactionId, out var row))
            {
                if (db.Entry(row).State == EntityState.Added)
                {
                    db.Entry(row).State = EntityState.Detached;
                }
                else
                {
                    db.BankTransactions.Remove(row);
                }

                existing.Remove(gone.TransactionId);
            }
        }

        if (!string.IsNullOrEmpty(lastCursor))
        {
            var tracked = await db.BankConnections.FirstAsync(c => c.Id == connection.Id);
            tracked.AdvanceCursor(lastCursor);
        }

        await db.SaveChangesAsync();
    }

    /// <summary>The transactions between two dates on any of the shopper's banks, asked of the bank directly.</summary>
    public async Task<LookupResult> GetTransactionsAsync(string email, string? from, string? to)
    {
        var start = ParseDate(from, "from");
        var end = ParseDate(to, "to");
        if (start > end)
        {
            throw new RequestFailedException(400, "from cannot be after to.");
        }

        var connections = await BankConnectionsOfAsync(email);
        var transactions = new List<BankTransactionDto>();
        var unavailable = new List<UnavailableBank>();

        foreach (var connection in connections)
        {
            try
            {
                transactions.AddRange(await FetchAsync(connection, start, end));
            }
            catch (Exception e) when (PlaidError.Translate(e) is { } failure)
            {
                unavailable.Add(new UnavailableBank(connection.Id, connection.InstitutionName, failure.Message));
            }
        }

        if (connections.Count > 0 && unavailable.Count == connections.Count)
        {
            throw new RequestFailedException(502, "Transactions could not be retrieved: " + unavailable[0].Reason);
        }

        return new LookupResult(transactions.OrderByDescending(t => t.Date).ToList(), unavailable);
    }

    private async Task<List<BankTransactionDto>> FetchAsync(BankConnection connection, DateTime start, DateTime end)
    {
        var accessToken = protector.Unprotect(connection);
        var result = new List<BankTransactionDto>();
        var offset = 0;
        while (true)
        {
            var currentOffset = offset;
            // The first request on a bank connected before transactions were enabled makes Plaid prepare them; WhenReady waits for that.
            var page = await PlaidError.WhenReady(() => plaid.TransactionsGet(new TransactionsGetOperationRequest
            {
                Body = new TransactionsGetRequest
                {
                    AccessToken = accessToken,
                    StartDate = new DateTimeOffset(start, TimeSpan.Zero),
                    EndDate = new DateTimeOffset(end, TimeSpan.Zero),
                    Options = new TransactionsGetRequestOptions { Count = PageSize, Offset = currentOffset },
                },
            }));

            result.AddRange(page.Transactions.Select(t => new BankTransactionDto(
                t.Name, Money.FromPlaid(t.Amount), DateOnly.FromDateTime(BankDates.CalendarDate(t.Date)), t.Pending, connection.Id, t.AccountId, connection.InstitutionName)));

            offset += page.Transactions.Count;
            if (page.Transactions.Count == 0 || offset >= page.TotalTransactions)
            {
                return result;
            }
        }
    }

    private static DateTime ParseDate(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new RequestFailedException(400, $"{name} is required, as YYYY-MM-DD.");
        }

        if (!DateTime.TryParseExact(value.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw new RequestFailedException(400, $"{name} must be a date as YYYY-MM-DD.");
        }

        return date;
    }
}
