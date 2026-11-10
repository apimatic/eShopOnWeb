using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankingAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using ThePlaidApi;
using ThePlaidApi.Core.ErrorResponse;
using ThePlaidApi.Core.Exceptions;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record ActivityTransaction(int ConnectionId, string AccountId, string Name, decimal Amount, DateOnly Date, bool Pending);

public record ConnectionProblem(int ConnectionId, string Reason);

public record ActivityResult(IReadOnlyList<ActivityTransaction> Transactions, IReadOnlyList<ConnectionProblem> Problems);

/// <summary>What support staff see of a shopper's bank accounts.</summary>
public class BankActivityService
{
    private const int PageSize = 500;
    private const int MaxPages = 400;

    private readonly ThePlaidApiClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;
    private readonly KeyedLock _locks;
    private readonly ILogger<BankActivityService> _logger;

    public BankActivityService(ThePlaidApiClient plaid, CatalogContext db, ConnectionService connections,
        KeyedLock locks, ILogger<BankActivityService> logger)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _locks = locks;
        _logger = logger;
    }

    private async Task<List<BankConnection>> BanksOfAsync(string shopperId, CancellationToken ct)
    {
        var banks = await _connections.ListActiveAsync(shopperId, BankConnectionKind.Bank, ct);
        if (banks.Count == 0)
            throw BankingException.NotFound("That shopper has no connected bank.");
        return banks;
    }

    // ---------------------------------------------------------------- the shop's own copy

    /// <summary>
    /// The shop's copy of the activity of every connected bank. Each bank is first brought in step with
    /// Plaid (added, modified and removed transactions, pending ones included), then the copy is read.
    /// </summary>
    public async Task<ActivityResult> GetActivityAsync(string shopperId, CancellationToken ct)
    {
        var banks = await BanksOfAsync(shopperId, ct);
        var problems = new List<ConnectionProblem>();

        foreach (var bank in banks)
        {
            try
            {
                await SyncAsync(bank.Id, ct);
            }
            catch (BankingException ex)
            {
                _logger.LogWarning("Could not sync connection {ConnectionId}: {Reason}", bank.Id, ex.Message);
                problems.Add(new ConnectionProblem(bank.Id, ex.Message));
            }
        }

        if (problems.Count == banks.Count)
        {
            // Nothing could be refreshed; the stored copy may still be useful, but only say so if there is one.
            var bankIds = banks.Select(b => b.Id).ToList();
            var any = await _db.BankTransactions.AnyAsync(t => bankIds.Contains(t.ConnectionId), ct);
            if (!any)
                throw new BankingException(System.Net.HttpStatusCode.BadGateway, problems[0].Reason);
        }

        var ids = banks.Select(b => b.Id).ToList();
        var rows = await _db.BankTransactions.Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        var transactions = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.Pending ? 0 : 1).ThenBy(t => t.Id)
            .Select(t => new ActivityTransaction(t.ConnectionId, t.AccountId, t.Name, t.Amount, t.Date, t.Pending))
            .ToList();
        return new ActivityResult(transactions, problems);
    }

    private async Task SyncAsync(int connectionId, CancellationToken ct)
    {
        using var _ = await _locks.AcquireAsync($"sync:{connectionId}", ct);

        var connection = await _db.BankConnections.FirstAsync(c => c.Id == connectionId, ct);
        var accessToken = _connections.AccessTokenOf(connection);
        var notReadyAttempts = 0;

        for (var page = 0; page < MaxPages; page++)
        {
            TransactionsSyncResponse response;
            try
            {
                response = await _plaid.TransactionsSync(new TransactionsSyncOperationRequest
                {
                    Body = new TransactionsSyncRequest
                    {
                        AccessToken = accessToken,
                        Cursor = string.IsNullOrEmpty(connection.SyncCursor) ? null : connection.SyncCursor,
                        Count = PageSize,
                        Options = new TransactionsSyncRequestOptions { DaysRequested = 730 },
                    }
                }, cancellationToken: ct);
            }
            catch (ApiException<RawError> ex) when (PlaidErrorTranslator.Parse(ex).ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")
            {
                // The data changed under us mid-pagination: start again from the last cursor we stored.
                continue;
            }
            catch (ApiException<RawError> ex) when (PlaidErrorTranslator.Parse(ex).ErrorCode == "PRODUCT_NOT_READY" && notReadyAttempts++ < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                continue;
            }
            catch (ApiException<RawError> ex)
            {
                throw PlaidErrorTranslator.Translate(ex);
            }

            // First sync of a fresh item: Plaid is still preparing its history, and says so by returning nothing yet.
            if (string.IsNullOrEmpty(connection.SyncCursor) && response.TransactionsUpdateStatus != TransactionsUpdateStatus.HistoricalUpdateComplete
                && response.Added.Count == 0 && string.IsNullOrEmpty(response.NextCursor) && notReadyAttempts++ < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                continue;
            }

            await ApplyAsync(connection, response, ct);
            if (!response.HasMore)
                return;
        }
    }

    private async Task ApplyAsync(BankConnection connection, TransactionsSyncResponse response, CancellationToken ct)
    {
        var touched = response.Added.Select(t => t.TransactionId)
            .Concat(response.Modified.Select(t => t.TransactionId))
            .Concat(response.Removed.Select(t => t.TransactionId))
            .Distinct().ToList();

        var existing = (await _db.BankTransactions
                .Where(t => t.ConnectionId == connection.Id && touched.Contains(t.PlaidTransactionId))
                .ToListAsync(ct))
            .ToDictionary(t => t.PlaidTransactionId);

        foreach (var added in response.Added.Concat(response.Modified))
        {
            if (!existing.TryGetValue(added.TransactionId, out var row))
            {
                row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = added.TransactionId };
                _db.BankTransactions.Add(row);
                existing[added.TransactionId] = row;
            }
            row.AccountId = added.AccountId;
            row.Name = added.Name;
            row.Amount = (decimal)added.Amount;
            row.Date = ToDate(added.Date);
            row.Pending = added.Pending;
        }

        foreach (var removed in response.Removed)
        {
            if (existing.TryGetValue(removed.TransactionId, out var row) && _db.Entry(row).State != EntityState.Added)
            {
                _db.BankTransactions.Remove(row);
                existing.Remove(removed.TransactionId);
            }
        }

        if (!string.IsNullOrEmpty(response.NextCursor))
            connection.SyncCursor = response.NextCursor;

        // Rows and cursor are saved together: the copy never gets ahead of, or behind, its cursor.
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Plaid sends calendar dates; the SDK attaches an offset to them, which must not shift the day.</summary>
    internal static DateOnly ToDate(DateTimeOffset date) => DateOnly.FromDateTime(date.DateTime);

    // ---------------------------------------------------------------- straight from the bank

    public async Task<ActivityResult> GetTransactionsBetweenAsync(string shopperId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var banks = await BanksOfAsync(shopperId, ct);
        var transactions = new List<ActivityTransaction>();
        var problems = new List<ConnectionProblem>();

        foreach (var bank in banks)
        {
            try
            {
                transactions.AddRange(await FetchRangeAsync(bank, from, to, ct));
            }
            catch (BankingException ex)
            {
                problems.Add(new ConnectionProblem(bank.Id, ex.Message));
            }
        }

        if (problems.Count == banks.Count)
        {
            var first = problems[0];
            throw new BankingException(System.Net.HttpStatusCode.BadGateway, first.Reason);
        }

        transactions = transactions.OrderByDescending(t => t.Date).ThenBy(t => t.ConnectionId).ToList();
        return new ActivityResult(transactions, problems);
    }

    private async Task<List<ActivityTransaction>> FetchRangeAsync(BankConnection bank, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var accessToken = _connections.AccessTokenOf(bank);
        var result = new List<ActivityTransaction>();
        var offset = 0;
        var total = int.MaxValue;
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // The ledger moves while we page through it, so pages are asked for by offset until all are read.
        for (var page = 0; offset < total && page < MaxPages; page++)
        {
            var currentOffset = offset;
            var response = await PlaidCalls.WhenProductReady(() => _plaid.TransactionsGet(new TransactionsGetOperationRequest
            {
                Body = new TransactionsGetRequest
                {
                    AccessToken = accessToken,
                    StartDate = start,
                    EndDate = end,
                    Options = new TransactionsGetRequestOptions
                    {
                        Count = PageSize,
                        Offset = currentOffset,
                        DaysRequested = 730,
                    },
                }
            }, cancellationToken: ct), ct);

            total = response.TotalTransactions;
            result.AddRange(response.Transactions.Select(t => new ActivityTransaction(
                bank.Id, t.AccountId, t.Name, (decimal)t.Amount, ToDate(t.Date), t.Pending)));
            if (response.Transactions.Count == 0)
                break;
            offset += response.Transactions.Count;
        }

        return result;
    }
}
