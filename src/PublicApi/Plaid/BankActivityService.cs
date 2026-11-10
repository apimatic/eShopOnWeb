using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Support views over a shopper's connected banks.</summary>
public class BankActivityService
{
    private const int NotReadyAttempts = 10;
    private static readonly TimeSpan NotReadyDelay = TimeSpan.FromSeconds(2);

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;
    private readonly UserManager<ApplicationUser> _users;

    public BankActivityService(PlaidClient plaid, CatalogContext db, ConnectionService connections, UserManager<ApplicationUser> users)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
        _users = users;
    }

    private async Task<(string? owner, List<BankConnection> banks)> LoadShopperAsync(string email, CancellationToken ct)
    {
        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        if (user?.UserName is null) return (null, new());
        var owner = ApiResults.OwnerKey(user.UserName);
        var banks = await _db.BankConnections.Include(c => c.Accounts)
            .Where(c => c.OwnerKey == owner && c.Kind == ConnectionKinds.Bank).OrderBy(c => c.Id).ToListAsync(ct);
        return (owner, banks);
    }

    // ---- Feature 8: the shop's own copy, kept in step with the bank ----

    public async Task<IResult> ActivityAsync(string email, CancellationToken ct)
    {
        var (owner, banks) = await LoadShopperAsync(email, ct);
        if (owner is null) return ApiResults.NotFound("Shopper not found.");

        var errors = new List<object>();
        string? firstError = null;
        foreach (var bank in banks)
        {
            try
            {
                await SyncAsync(bank, ct);
            }
            catch (PlaidException ex)
            {
                // Keep serving the copy we already have for this bank, and say why it may be stale.
                firstError ??= ex.DisplayMessage ?? ex.Message;
                errors.Add(new { connectionId = bank.PublicId, institutionName = bank.InstitutionName, message = ex.DisplayMessage ?? ex.Message });
            }
        }

        var ids = banks.Select(b => b.Id).ToList();
        var rows = await _db.BankTransactions.Where(t => ids.Contains(t.BankConnectionId)).ToListAsync(ct);
        var byId = banks.ToDictionary(b => b.Id);
        var transactions = rows.OrderByDescending(t => t.Date).ThenBy(t => t.Id).Select(t => new
        {
            id = t.PlaidTransactionId,
            connectionId = byId[t.BankConnectionId].PublicId,
            institutionName = byId[t.BankConnectionId].InstitutionName,
            accountId = t.AccountId,
            name = t.Name,
            amount = t.Amount,
            isoCurrencyCode = t.IsoCurrencyCode,
            date = t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            pending = t.Pending
        }).ToList();

        if (errors.Count > 0 && errors.Count == banks.Count && transactions.Count == 0)
            return ApiResults.Conflict(firstError!);

        return Results.Ok(new { email, transactions, errors });
    }

    /// <summary>Pulls added / modified / removed transactions from the stored cursor and applies them in one save.</summary>
    private async Task SyncAsync(BankConnection bank, CancellationToken ct)
    {
        using var _ = await CheckoutService.LockAsync("sync:" + bank.Id, ct);
        var token = _connections.Unprotect(bank);
        var startCursor = bank.TransactionsCursor;

        for (var attempt = 0; ; attempt++)
        {
            var added = new List<JsonElement>();
            var modified = new List<JsonElement>();
            var removed = new List<string>();
            var cursor = startCursor;
            try
            {
                while (true)
                {
                    var page = await _plaid.TransactionsSyncAsync(token, cursor, ct);
                    added.AddRange(page.Arr("added"));
                    modified.AddRange(page.Arr("modified"));
                    removed.AddRange(page.Arr("removed").Select(r => r.Str("transaction_id")).Where(i => i is not null)!);
                    cursor = page.Str("next_cursor") ?? cursor;
                    if (!page.Bool("has_more")) break;
                }
            }
            catch (PlaidException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && attempt < 5)
            {
                continue; // data changed mid-walk: start over from the saved cursor
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
            {
                await Task.Delay(NotReadyDelay, ct);
                continue;
            }

            await ApplyAsync(bank, added, modified, removed, cursor, ct);
            return;
        }
    }

    private async Task ApplyAsync(BankConnection bank, List<JsonElement> added, List<JsonElement> modified, List<string> removed, string? cursor, CancellationToken ct)
    {
        var existing = await _db.BankTransactions.Where(t => t.BankConnectionId == bank.Id)
            .ToDictionaryAsync(t => t.PlaidTransactionId, ct);

        foreach (var id in removed.Distinct())
        {
            if (existing.Remove(id, out var gone)) _db.BankTransactions.Remove(gone);
        }

        foreach (var e in added.Concat(modified))
        {
            var id = e.Str("transaction_id");
            if (id is null) continue;
            if (!existing.TryGetValue(id, out var row))
            {
                row = new BankTransaction { BankConnectionId = bank.Id, PlaidTransactionId = id };
                existing[id] = row;
                _db.BankTransactions.Add(row);
            }
            row.AccountId = e.Str("account_id") ?? row.AccountId;
            row.Name = e.Str("name") ?? e.Str("merchant_name") ?? row.Name;
            row.Amount = e.Dec("amount") ?? 0m;
            row.IsoCurrencyCode = e.Str("iso_currency_code");
            row.Date = ParseDate(e.Str("date")) ?? row.Date;
            row.Pending = e.Bool("pending");
            row.PendingTransactionId = e.Str("pending_transaction_id");

            // A posted transaction replaces the pending one it came from, even if the removal is not in this batch.
            if (row.PendingTransactionId is not null && existing.Remove(row.PendingTransactionId, out var superseded))
                _db.BankTransactions.Remove(superseded);
        }

        bank.TransactionsCursor = cursor;
        await _db.SaveChangesAsync(ct);
    }

    // ---- Feature 9: straight from the bank between two dates ----

    public async Task<IResult> BetweenAsync(string email, string? from, string? to, string? connectionId, CancellationToken ct)
    {
        if (!TryDate(from, out var start) || !TryDate(to, out var end))
            return ApiResults.BadRequest("from and to are required, as YYYY-MM-DD.");
        if (start > end)
            return ApiResults.BadRequest("from must not be after to.");

        var (owner, banks) = await LoadShopperAsync(email, ct);
        if (owner is null) return ApiResults.NotFound("Shopper not found.");
        if (!string.IsNullOrWhiteSpace(connectionId))
        {
            banks = banks.Where(b => b.PublicId == connectionId.Trim()).ToList();
            if (banks.Count == 0) return ApiResults.NotFound("That shopper has no such connected bank.");
        }

        var transactions = new List<object>();
        var errors = new List<(object Dto, string Message)>();
        foreach (var bank in banks)
        {
            try
            {
                foreach (var e in await GetBetweenAsync(bank, start, end, ct))
                {
                    transactions.Add(new
                    {
                        id = e.Str("transaction_id"),
                        connectionId = bank.PublicId,
                        institutionName = bank.InstitutionName,
                        accountId = e.Str("account_id"),
                        name = e.Str("name") ?? e.Str("merchant_name"),
                        amount = e.Dec("amount"),
                        date = e.Str("date"),
                        pending = e.Bool("pending")
                    });
                }
            }
            catch (PlaidException ex)
            {
                var message = ex.DisplayMessage ?? ex.Message;
                errors.Add((new { connectionId = bank.PublicId, institutionName = bank.InstitutionName, message }, message));
            }
        }

        // A lookup that failed everywhere must not look like "no transactions".
        if (errors.Count > 0 && transactions.Count == 0 && errors.Count == banks.Count)
        {
            var ex = errors[0].Message;
            return ApiResults.Conflict(ex);
        }

        return Results.Ok(new { email, from = start.ToString("yyyy-MM-dd"), to = end.ToString("yyyy-MM-dd"), transactions, errors = errors.Select(e => e.Dto) });
    }

    private async Task<List<JsonElement>> GetBetweenAsync(BankConnection bank, DateTime start, DateTime end, CancellationToken ct)
    {
        var token = _connections.Unprotect(bank);
        var all = new List<JsonElement>();
        var offset = 0;
        for (var notReady = 0; ;)
        {
            JsonElement page;
            try
            {
                page = await _plaid.TransactionsGetAsync(token, start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), 500, offset, ct);
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && notReady++ < NotReadyAttempts)
            {
                // A bank connected before this lookup existed has no transactions product yet; Plaid adds it on first use.
                await Task.Delay(NotReadyDelay, ct);
                continue;
            }

            var batch = page.Arr("transactions").ToList();
            all.AddRange(batch);
            offset += batch.Count;
            var total = page.TryGetProperty("total_transactions", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : all.Count;
            if (batch.Count == 0 || offset >= total) break;
        }
        return all;
    }

    private static bool TryDate(string? s, out DateTime d) =>
        DateTime.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);

    private static DateTime? ParseDate(string? s) => TryDate(s, out var d) ? d : null;
}
