using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record BankTransactionDto(
    string Name,
    decimal Amount,
    string Date,
    bool Pending,
    string? Bank,
    string? ConnectionId,
    string? AccountId);

public sealed record UnavailableBankDto(string ConnectionId, string? Bank, string Reason);

public sealed record BankActivityResult(IReadOnlyList<BankTransactionDto> Transactions, IReadOnlyList<UnavailableBankDto> Unavailable);

/// <summary>Support's view of the bank accounts of a shopper.</summary>
public class BankActivityService
{
    private const int PageSize = 500;
    private const int MaxNotReadyAttempts = 15;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> SyncLocks = new();

    private readonly PlaidApi _plaid;
    private readonly CatalogContext _db;
    private readonly BankConnectionService _connections;

    public BankActivityService(PlaidApi plaid, CatalogContext db, BankConnectionService connections)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
    }

    /// <summary>The shop's own copy of the activity, brought up to date with the bank's records first.</summary>
    public async Task<BankActivityResult> GetActivityAsync(string userName)
    {
        var banks = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserName == userName && c.Kind == BankConnectionKind.Bank)
            .OrderBy(c => c.Id).ToListAsync();

        var unavailable = new List<UnavailableBankDto>();
        var synced = new List<BankConnection>();
        foreach (var bank in banks)
        {
            try
            {
                await SyncAsync(bank.Id);
                synced.Add(bank);
            }
            catch (ApiRequestException ex)
            {
                unavailable.Add(new UnavailableBankDto(bank.PublicId.ToString(), bank.InstitutionName, ex.Message));
            }
        }

        if (banks.Count > 0 && synced.Count == 0 && unavailable.Count > 0)
        {
            var first = unavailable[0];
            throw new ApiRequestException(HttpStatusCode.Conflict, first.Reason);
        }

        var ids = synced.Select(b => b.Id).ToList();
        var rows = await _db.BankTransactions.AsNoTracking()
            .Where(t => ids.Contains(t.BankConnectionId)).ToListAsync();
        var byConnection = synced.ToDictionary(b => b.Id);
        var transactions = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.Id)
            .Select(t => new BankTransactionDto(t.Name, t.Amount, FormatDate(t.Date), t.Pending,
                byConnection[t.BankConnectionId].InstitutionName,
                byConnection[t.BankConnectionId].PublicId.ToString(), t.AccountId))
            .ToList();
        return new BankActivityResult(transactions, unavailable);
    }

    /// <summary>Applies the bank's changes (added, modified and removed transactions) to the shop's copy.</summary>
    private async Task SyncAsync(int connectionId)
    {
        var gate = SyncLocks.GetOrAdd(connectionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            var connection = await _db.BankConnections.FirstAsync(c => c.Id == connectionId);
            var accessToken = _connections.RevealAccessToken(connection);

            for (var restart = 0; ; restart++)
            {
                var startCursor = connection.TransactionsCursor;
                var cursor = startCursor;
                var added = new List<Transaction>();
                var modified = new List<Transaction>();
                var removed = new List<string>();
                var hasMore = true;
                var mutated = false;

                while (hasMore)
                {
                    TransactionsSyncResponse page;
                    try
                    {
                        var currentCursor = cursor;
                        Task<TransactionsSyncResponse> Fetch() => WithProductReadyAsync(() => _plaid.CallAsync(c => c.TransactionsSync(
                            new TransactionsSyncOperationRequest
                            {
                                Body = new TransactionsSyncRequest
                                {
                                    AccessToken = accessToken,
                                    Cursor = string.IsNullOrEmpty(currentCursor) ? null : currentCursor,
                                    Count = PageSize
                                }
                            })));

                        page = await Fetch();

                        // Right after linking Plaid is still pulling the first transactions: an empty answer then
                        // (no cursor yet) means "not yet", not "none". Wait for it instead of showing support an empty history.
                        for (var wait = 1; string.IsNullOrEmpty(currentCursor) &&
                                           (page.TransactionsUpdateStatus.Value == "NOT_READY" ||
                                            string.IsNullOrEmpty(page.NextCursor)); wait++)
                        {
                            if (wait >= MaxNotReadyAttempts)
                            {
                                throw new ApiRequestException(HttpStatusCode.Conflict,
                                    "The bank's transactions are not ready yet, try again in a moment.");
                            }

                            await Task.Delay(NotReadyDelay);
                            page = await Fetch();
                        }
                    }
                    catch (ApiRequestException ex) when (restart < 3 &&
                        PlaidApi.CodeOf(ex) == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")
                    {
                        // The data changed while paging: start again from where this sync began.
                        mutated = true;
                        break;
                    }

                    added.AddRange(page.Added);
                    modified.AddRange(page.Modified);
                    removed.AddRange(page.Removed.Select(r => r.TransactionId));
                    cursor = page.NextCursor;
                    hasMore = page.HasMore;
                }

                if (mutated)
                {
                    continue;
                }

                await ApplyAsync(connection, added, modified, removed, cursor);
                return;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ApplyAsync(BankConnection connection, List<Transaction> added, List<Transaction> modified,
        List<string> removed, string? cursor)
    {
        var existing = await _db.BankTransactions
            .Where(t => t.BankConnectionId == connection.Id).ToDictionaryAsync(t => t.PlaidTransactionId);

        foreach (var id in removed)
        {
            if (existing.Remove(id, out var gone))
            {
                _db.BankTransactions.Remove(gone);
            }
        }

        foreach (var tx in added.Concat(modified))
        {
            if (!existing.TryGetValue(tx.TransactionId, out var row))
            {
                row = new BankTransaction { BankConnectionId = connection.Id, PlaidTransactionId = tx.TransactionId };
                existing[tx.TransactionId] = row;
                _db.BankTransactions.Add(row);
            }

            row.AccountId = tx.AccountId;
            row.Name = tx.Name;
            row.Amount = ToMoney(tx.Amount);
            row.IsoCurrencyCode = tx.IsoCurrencyCode;
            row.Date = tx.Date.Date;
            row.Pending = tx.Pending;
        }

        connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync();
    }

    /// <summary>The transactions straight from the bank between two dates, for each of the shopper's banks.</summary>
    public async Task<BankActivityResult> GetTransactionsAsync(string userName, DateTime from, DateTime to)
    {
        var banks = await _db.BankConnections.AsNoTracking()
            .Where(c => c.UserName == userName && c.Kind == BankConnectionKind.Bank)
            .OrderBy(c => c.Id).ToListAsync();

        var transactions = new List<(DateTime Date, BankTransactionDto Dto)>();
        var unavailable = new List<UnavailableBankDto>();
        var succeeded = 0;
        foreach (var bank in banks)
        {
            try
            {
                var accessToken = _connections.RevealAccessToken(bank);
                var found = await FetchRangeAsync(accessToken, from, to);
                succeeded++;
                transactions.AddRange(found.Select(t => (t.Date.Date, new BankTransactionDto(t.Name, ToMoney(t.Amount),
                    FormatDate(t.Date.Date), t.Pending, bank.InstitutionName, bank.PublicId.ToString(), t.AccountId))));
            }
            catch (ApiRequestException ex)
            {
                unavailable.Add(new UnavailableBankDto(bank.PublicId.ToString(), bank.InstitutionName, ex.Message));
            }
        }

        if (banks.Count > 0 && succeeded == 0)
        {
            throw new ApiRequestException(HttpStatusCode.Conflict, unavailable[0].Reason);
        }

        return new BankActivityResult(
            transactions.OrderByDescending(t => t.Date).Select(t => t.Dto).ToList(), unavailable);
    }

    private async Task<List<Transaction>> FetchRangeAsync(string accessToken, DateTime from, DateTime to)
    {
        var all = new List<Transaction>();
        var total = int.MaxValue;
        while (all.Count < total)
        {
            var offset = all.Count;
            var page = await WithProductReadyAsync(() => _plaid.CallAsync(c => c.TransactionsGet(
                new TransactionsGetOperationRequest
                {
                    Body = new TransactionsGetRequest
                    {
                        AccessToken = accessToken,
                        StartDate = new DateTimeOffset(DateTime.SpecifyKind(from, DateTimeKind.Utc)),
                        EndDate = new DateTimeOffset(DateTime.SpecifyKind(to, DateTimeKind.Utc)),
                        Options = new TransactionsGetRequestOptions
                        {
                            Count = PageSize,
                            Offset = offset,
                            // Banks connected before this lookup existed get their history initialised on first use.
                            DaysRequested = 730
                        }
                    }
                })));
            total = page.TotalTransactions;
            if (page.Transactions.Count == 0)
            {
                break;
            }

            all.AddRange(page.Transactions);
        }

        return all;
    }

    /// <summary>
    /// A bank connected without the Transactions product is initialised by the first request; until then Plaid
    /// answers PRODUCT_NOT_READY, so ask again for a little while.
    /// </summary>
    private static async Task<T> WithProductReadyAsync<T>(Func<Task<T>> call)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (ApiRequestException ex) when (PlaidApi.CodeOf(ex) == "PRODUCT_NOT_READY")
            {
                if (attempt >= MaxNotReadyAttempts)
                {
                    throw new ApiRequestException(HttpStatusCode.Conflict,
                        "The bank's transactions are not ready yet, try again in a moment.", ex);
                }

                await Task.Delay(NotReadyDelay);
            }
        }
    }

    public static decimal ToMoney(double amount) => Math.Round((decimal)amount, 2, MidpointRounding.AwayFromZero);

    public static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
