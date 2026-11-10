using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public class BankTransactionDto
{
    public string TransactionId { get; set; } = string.Empty;
    public int ConnectionId { get; set; }
    public string? Bank { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? CurrencyCode { get; set; }
    public string Date { get; set; } = string.Empty;
    public bool Pending { get; set; }
}

public class BankActivityService
{
    private const int PageSize = 500;
    private const int NotReadyAttempts = 8;

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public BankActivityService(PlaidClient plaid, CatalogContext db, UserManager<ApplicationUser> users)
    {
        _plaid = plaid;
        _db = db;
        _users = users;
    }

    private async Task<List<BankConnection>> GetShopperBanksAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || await _users.FindByEmailAsync(email.Trim()) is null)
        {
            throw ApiProblemException.NotFound($"No shopper with email {email}.");
        }

        var id = BankConnectionService.NormalizeBuyer(email);
        return await _db.BankConnections
            .Where(c => c.BuyerId == id && c.Type == BankConnectionTypes.Bank)
            .OrderBy(c => c.Id)
            .ToListAsync(ct);
    }

    private static string Fmt(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------- the shop's own copy

    public async Task<List<BankTransactionDto>> GetActivityAsync(string email, CancellationToken ct)
    {
        var banks = await GetShopperBanksAsync(email, ct);
        var result = new List<BankTransactionDto>();

        foreach (var bank in banks)
        {
            await SyncAsync(bank, ct);
            var rows = await _db.BankTransactions.Where(t => t.ConnectionId == bank.Id).ToListAsync(ct);
            result.AddRange(rows.Select(t => new BankTransactionDto
            {
                TransactionId = t.TransactionId,
                ConnectionId = bank.Id,
                Bank = bank.InstitutionName,
                AccountId = t.AccountId,
                Name = t.Name,
                Amount = t.Amount,
                CurrencyCode = t.CurrencyCode,
                Date = Fmt(t.Date),
                Pending = t.Pending
            }));
        }

        return result.OrderByDescending(t => t.Date).ThenBy(t => t.Name).ToList();
    }

    /// <summary>Brings the local copy of a bank's transactions up to date with /transactions/sync.</summary>
    private async Task SyncAsync(BankConnection bank, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var startCursor = bank.TransactionsCursor;
            var cursor = startCursor;
            var added = new List<JsonNode>();
            var modified = new List<JsonNode>();
            var removed = new List<string>();
            var restart = false;
            var notReady = false;

            while (true)
            {
                JsonNode page;
                try
                {
                    var request = new JsonObject { ["access_token"] = bank.AccessToken, ["count"] = PageSize };
                    if (!string.IsNullOrEmpty(cursor)) request["cursor"] = cursor;
                    page = await _plaid.PostAsync("/transactions/sync", request, ct);
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION")
                {
                    // The data changed while paging; Plaid wants the whole pull to start again from the saved cursor.
                    restart = true;
                    break;
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY")
                {
                    notReady = true;
                    break;
                }

                added.AddRange(page["added"]?.AsArray().Select(n => n!) ?? Enumerable.Empty<JsonNode>());
                modified.AddRange(page["modified"]?.AsArray().Select(n => n!) ?? Enumerable.Empty<JsonNode>());
                removed.AddRange(page["removed"]?.AsArray().Select(n => n!["transaction_id"]!.GetValue<string>()) ?? Enumerable.Empty<string>());
                cursor = page["next_cursor"]?.GetValue<string>() ?? cursor;
                if (page["has_more"]?.GetValue<bool>() != true)
                {
                    break;
                }
            }

            if (!restart && !notReady)
            {
                await ApplyAsync(bank, added, modified, removed, cursor, ct);
                return;
            }

            if (attempt >= NotReadyAttempts)
            {
                throw ApiProblemException.Conflict(notReady
                    ? "Plaid is still preparing this bank's transactions; try again in a moment."
                    : "This bank's transactions kept changing while they were read; try again.");
            }

            await Task.Delay(TimeSpan.FromSeconds(notReady ? 2 : 0.2), ct);
        }
    }

    private async Task ApplyAsync(BankConnection bank, List<JsonNode> added, List<JsonNode> modified, List<string> removed,
        string? cursor, CancellationToken ct)
    {
        var existing = (await _db.BankTransactions.Where(t => t.ConnectionId == bank.Id).ToListAsync(ct))
            .ToDictionary(t => t.TransactionId);

        void Upsert(JsonNode n)
        {
            var id = n["transaction_id"]!.GetValue<string>();
            if (!existing.TryGetValue(id, out var row))
            {
                row = new BankTransaction { ConnectionId = bank.Id, TransactionId = id };
                existing[id] = row;
                _db.BankTransactions.Add(row);
            }

            row.AccountId = n["account_id"]?.GetValue<string>() ?? row.AccountId;
            row.Name = n["name"]?.GetValue<string>() ?? n["merchant_name"]?.GetValue<string>() ?? row.Name;
            row.Amount = n["amount"]?.GetValue<decimal>() ?? 0m;
            row.CurrencyCode = n["iso_currency_code"]?.GetValue<string>() ?? n["unofficial_currency_code"]?.GetValue<string>();
            row.Date = DateTime.ParseExact(n["date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            row.Pending = n["pending"]?.GetValue<bool>() ?? false;
        }

        foreach (var n in added) Upsert(n);
        foreach (var n in modified) Upsert(n);
        foreach (var id in removed)
        {
            if (existing.Remove(id, out var row))
            {
                if (_db.Entry(row).State == EntityState.Added) _db.Entry(row).State = EntityState.Detached;
                else _db.BankTransactions.Remove(row);
            }
        }

        bank.TransactionsCursor = cursor;
        await _db.SaveChangesAsync(ct); // the changes and the cursor that produced them are saved together
    }

    // ------------------------------------------------- straight from the bank

    public async Task<List<BankTransactionDto>> GetBetweenAsync(string email, string? from, string? to, CancellationToken ct)
    {
        if (!TryParseDate(from, out var start) || !TryParseDate(to, out var end))
        {
            throw ApiProblemException.BadRequest("from and to are required and must be dates formatted YYYY-MM-DD.");
        }

        if (start > end)
        {
            throw ApiProblemException.BadRequest("from must not be after to.");
        }

        var banks = await GetShopperBanksAsync(email, ct);
        var result = new List<BankTransactionDto>();

        foreach (var bank in banks)
        {
            var offset = 0;
            var total = int.MaxValue;
            while (offset < total)
            {
                var page = await GetPageAsync(bank, start, end, offset, ct);
                total = page["total_transactions"]?.GetValue<int>() ?? 0;
                var transactions = page["transactions"]?.AsArray() ?? new JsonArray();
                if (transactions.Count == 0)
                {
                    break;
                }

                foreach (var t in transactions)
                {
                    result.Add(new BankTransactionDto
                    {
                        TransactionId = t!["transaction_id"]!.GetValue<string>(),
                        ConnectionId = bank.Id,
                        Bank = bank.InstitutionName,
                        AccountId = t["account_id"]?.GetValue<string>() ?? string.Empty,
                        Name = t["name"]?.GetValue<string>() ?? string.Empty,
                        Amount = t["amount"]?.GetValue<decimal>() ?? 0m,
                        CurrencyCode = t["iso_currency_code"]?.GetValue<string>() ?? t["unofficial_currency_code"]?.GetValue<string>(),
                        Date = t["date"]!.GetValue<string>(),
                        Pending = t["pending"]?.GetValue<bool>() ?? false
                    });
                }

                offset += transactions.Count;
            }
        }

        return result.OrderByDescending(t => t.Date).ThenBy(t => t.Name).ToList();
    }

    private async Task<JsonNode> GetPageAsync(BankConnection bank, DateTime start, DateTime end, int offset, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _plaid.PostAsync("/transactions/get", new
                {
                    access_token = bank.AccessToken,
                    start_date = Fmt(start),
                    end_date = Fmt(end),
                    options = new { count = PageSize, offset }
                }, ct);
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < NotReadyAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY")
            {
                throw ApiProblemException.Conflict("Plaid is still preparing this bank's transactions; try again in a moment.");
            }
            catch (PlaidApiException ex) when (ex.ErrorCode is "ADDITIONAL_CONSENT_REQUIRED" or "PRODUCTS_NOT_SUPPORTED")
            {
                throw ApiProblemException.Conflict(
                    $"{bank.InstitutionName ?? "This bank"} was connected without access to transactions; " +
                    "the shopper must reopen Link for it (POST /api/bank/link-token with its connectionId) to grant it.");
            }
        }
    }

    private static bool TryParseDate(string? text, out DateTime date) =>
        DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
}
