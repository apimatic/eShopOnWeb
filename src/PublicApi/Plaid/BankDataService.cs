using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.Banking;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public class BankTransactionDto
{
    public int ConnectionId { get; set; }
    public string? InstitutionName { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public DateOnly Date { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Pending { get; set; }
}

public class BankActivityResponse
{
    public string Email { get; set; } = string.Empty;
    public List<BankTransactionDto> Transactions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class PositionDto
{
    public int ConnectionId { get; set; }
    public string? InstitutionName { get; set; }
    public string? Ticker { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public decimal? Value { get; set; }
    /// <summary>Units held, for anything that is not an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }
    /// <summary>Number of contracts held, for an option.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }
}

public class PortfolioResponse
{
    public List<PositionDto> Positions { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class BankDataService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> SyncLocks = new();

    private readonly CatalogContext _db;
    private readonly PlaidClient _plaid;
    private readonly AccessTokenProtector _protector;

    public BankDataService(CatalogContext db, PlaidClient plaid, AccessTokenProtector protector)
    {
        _db = db;
        _plaid = plaid;
        _protector = protector;
    }

    private static decimal Dec(JsonNode? node) => node is null ? 0m : node.GetValue<decimal>();

    private static decimal? DecOrNull(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }
        return node.GetValueKind() == System.Text.Json.JsonValueKind.Number ? Dec(node) : null;
    }

    private static DateTime Date(JsonNode? node) =>
        DateTime.ParseExact(node!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string TxnName(JsonObject t) =>
        t["name"]?.GetValue<string>() ?? t["merchant_name"]?.GetValue<string>() ?? string.Empty;

    // ---------- Bank activity: the shop's own copy, kept in step with the bank ----------

    public async Task<BankActivityResponse> GetBankActivityAsync(string email)
    {
        var key = CallerIdentity.NormaliseKey(email);
        var connections = await _db.BankConnections.Where(c => c.BuyerId == key && c.Purpose == ConnectionPurpose.Bank).OrderBy(c => c.Id).ToListAsync();
        var response = new BankActivityResponse { Email = email };

        var failures = new List<PlaidException>();
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection);
            }
            catch (PlaidException ex)
            {
                failures.Add(ex);
                response.Warnings.Add($"{Label(connection)} could not be refreshed, so its activity may be out of date: {ex.Message}");
            }
        }
        var ids = connections.Select(c => c.Id).ToList();
        if (connections.Count > 0 && failures.Count == connections.Count && !await _db.BankTransactions.AnyAsync(t => ids.Contains(t.ConnectionId)))
        {
            throw failures[0];
        }

        var rows = await _db.BankTransactions.AsNoTracking().Where(t => ids.Contains(t.ConnectionId)).ToListAsync();
        var names = connections.ToDictionary(c => c.Id, c => c.InstitutionName);
        response.Transactions = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.Name).ThenBy(t => t.Id)
            .Select(t => new BankTransactionDto
            {
                ConnectionId = t.ConnectionId,
                InstitutionName = names.GetValueOrDefault(t.ConnectionId),
                AccountId = t.AccountId,
                TransactionId = t.PlaidTransactionId,
                Name = t.Name,
                Amount = t.Amount,
                Date = DateOnly.FromDateTime(t.Date),
                Pending = t.Pending
            }).ToList();
        return response;
    }

    private static string Label(BankConnection c) => $"Bank connection {c.Id} ({c.InstitutionName ?? "unknown bank"})";

    private async Task SyncAsync(BankConnection connection)
    {
        var gate = SyncLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            // Another request may have synced meanwhile.
            await _db.Entry(connection).ReloadAsync();
            var accessToken = _protector.Unprotect(connection);
            var startCursor = connection.TransactionsCursor ?? string.Empty;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    var (changes, cursor, ready) = await PullChangesAsync(accessToken, startCursor);
                    await ApplyAsync(connection, changes, ready ? cursor : startCursor);
                    return;
                }
                catch (PlaidException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && attempt < 4)
                {
                    // The bank's records changed while paging: start over from where we began.
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Reads every page of changes after the cursor. A null transaction means "removed".</summary>
    private async Task<(Dictionary<string, JsonObject?> Changes, string Cursor, bool Ready)> PullChangesAsync(string accessToken, string startCursor)
    {
        var changes = new Dictionary<string, JsonObject?>();
        var cursor = startCursor;
        var notReadyWaits = 0;

        while (true)
        {
            var body = new JsonObject { ["access_token"] = accessToken, ["count"] = 500 };
            if (!string.IsNullOrEmpty(cursor))
            {
                body["cursor"] = cursor;
            }

            JsonObject page;
            try
            {
                page = await _plaid.PostAsync("/transactions/sync", body);
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && notReadyWaits++ < 15)
            {
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                continue;
            }

            if (page["transactions_update_status"]?.GetValue<string>() == "NOT_READY")
            {
                // The bank's history is still being prepared (for example for a bank connected before
                // transactions were used): wait for it rather than storing an empty copy.
                if (notReadyWaits++ < 15)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1.5));
                    continue;
                }
                return (changes, startCursor, false);
            }

            foreach (var t in (page["added"] as JsonArray ?? new JsonArray()).Concat(page["modified"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                changes[t["transaction_id"]!.GetValue<string>()] = t;
            }
            foreach (var r in (page["removed"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                changes[r["transaction_id"]!.GetValue<string>()] = null;
            }

            cursor = page["next_cursor"]?.GetValue<string>() ?? cursor;
            if (page["has_more"]?.GetValue<bool>() != true)
            {
                return (changes, cursor, true);
            }
        }
    }

    private async Task ApplyAsync(BankConnection connection, Dictionary<string, JsonObject?> changes, string cursor)
    {
        if (changes.Count > 0)
        {
            var ids = changes.Keys.ToList();
            var existing = await _db.BankTransactions
                .Where(t => t.ConnectionId == connection.Id && ids.Contains(t.PlaidTransactionId))
                .ToDictionaryAsync(t => t.PlaidTransactionId);

            foreach (var (id, txn) in changes)
            {
                existing.TryGetValue(id, out var row);
                if (txn is null)
                {
                    if (row is not null)
                    {
                        _db.BankTransactions.Remove(row);
                    }
                    continue;
                }

                if (row is null)
                {
                    row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = id };
                    _db.BankTransactions.Add(row);
                }
                row.AccountId = txn["account_id"]?.GetValue<string>() ?? string.Empty;
                row.Name = TxnName(txn);
                row.Amount = Dec(txn["amount"]);
                row.CurrencyCode = txn["iso_currency_code"]?.GetValue<string>();
                row.Date = Date(txn["date"]);
                row.Pending = txn["pending"]?.GetValue<bool>() ?? false;
            }
        }

        connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync();
    }

    // ---------- Transactions between two dates, straight from the bank ----------

    public async Task<BankActivityResponse> GetTransactionsBetweenAsync(string email, DateTime from, DateTime to)
    {
        var key = CallerIdentity.NormaliseKey(email);
        var connections = await _db.BankConnections.AsNoTracking().Where(c => c.BuyerId == key && c.Purpose == ConnectionPurpose.Bank).OrderBy(c => c.Id).ToListAsync();
        var response = new BankActivityResponse { Email = email };
        var failures = new List<PlaidException>();

        foreach (var connection in connections)
        {
            try
            {
                var accessToken = _protector.Unprotect(connection);
                var offset = 0;
                var total = int.MaxValue;
                while (offset < total)
                {
                    var page = await _plaid.PostWhenReadyAsync("/transactions/get", () => new JsonObject
                    {
                        ["access_token"] = accessToken,
                        ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["options"] = new JsonObject { ["count"] = 500, ["offset"] = offset }
                    });

                    var txns = (page["transactions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToList();
                    total = page["total_transactions"]?.GetValue<int>() ?? 0;
                    foreach (var t in txns)
                    {
                        response.Transactions.Add(new BankTransactionDto
                        {
                            ConnectionId = connection.Id,
                            InstitutionName = connection.InstitutionName,
                            AccountId = t["account_id"]?.GetValue<string>() ?? string.Empty,
                            TransactionId = t["transaction_id"]?.GetValue<string>() ?? string.Empty,
                            Name = TxnName(t),
                            Amount = Dec(t["amount"]),
                            Date = DateOnly.FromDateTime(Date(t["date"]))
                        });
                    }
                    if (txns.Count == 0)
                    {
                        break;
                    }
                    offset += txns.Count;
                }
            }
            catch (PlaidException ex)
            {
                failures.Add(ex);
                response.Warnings.Add($"{Label(connection)} could not be read: {ex.Message}");
            }
        }

        if (connections.Count > 0 && failures.Count == connections.Count)
        {
            throw failures[0];
        }

        response.Transactions = response.Transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Name).ToList();
        return response;
    }

    // ---------- Portfolio ----------

    public async Task<PortfolioResponse> GetPortfolioAsync(string buyerKey)
    {
        var connections = await _db.BankConnections.AsNoTracking().Where(c => c.BuyerId == buyerKey && c.Purpose == ConnectionPurpose.Brokerage).OrderBy(c => c.Id).ToListAsync();
        var response = new PortfolioResponse();
        var failures = new List<PlaidException>();

        foreach (var connection in connections)
        {
            try
            {
                var accessToken = _protector.Unprotect(connection);
                var holdings = await _plaid.PostWhenReadyAsync("/investments/holdings/get", () => new JsonObject { ["access_token"] = accessToken });
                var securities = (holdings["securities"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
                    .ToDictionary(s => s["security_id"]!.GetValue<string>());

                foreach (var h in (holdings["holdings"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                {
                    securities.TryGetValue(h["security_id"]?.GetValue<string>() ?? string.Empty, out var security);
                    var type = security?["type"]?.GetValue<string>();
                    var isOption = type == "derivative" || security?["option_contract"] is JsonObject;
                    var quantity = DecOrNull(h["quantity"]);

                    response.Positions.Add(new PositionDto
                    {
                        ConnectionId = connection.Id,
                        InstitutionName = connection.InstitutionName,
                        Ticker = security?["ticker_symbol"]?.GetValue<string>(),
                        Name = security?["name"]?.GetValue<string>(),
                        Type = type,
                        Value = DecOrNull(h["institution_value"]),
                        Contracts = isOption ? quantity : null,
                        Quantity = isOption ? null : quantity
                    });
                }
            }
            catch (PlaidException ex)
            {
                failures.Add(ex);
                response.Warnings.Add($"{Label(connection)} could not be read: {ex.Message}");
            }
        }

        if (connections.Count > 0 && failures.Count == connections.Count)
        {
            throw failures[0];
        }
        return response;
    }
}

public class BankInfoDto
{
    public string Id { get; set; } = string.Empty;
    public string InstitutionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public bool Oauth { get; set; }
    /// <summary>True when the bank supports what the shop needs to take a payment from it.</summary>
    public bool SupportsPayments { get; set; }
    public List<string> Products { get; set; } = new();
}

/// <summary>The banks Plaid supports, for the checkout bank picker. Cached: every checkout opens it.</summary>
public class BankDirectoryService
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new();

    private readonly PlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public BankDirectoryService(PlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<List<BankInfoDto>> GetBanksAsync(string? query, int? count, int? offset)
    {
        query = query?.Trim();
        string cacheKey;
        TimeSpan ttl;
        Func<Task<List<BankInfoDto>>> load;

        if (string.IsNullOrEmpty(query))
        {
            var c = count ?? 100;
            var o = offset ?? 0;
            if (c < 1 || c > 500)
            {
                throw new ApiException(400, "count must be between 1 and 500.");
            }
            if (o < 0)
            {
                throw new ApiException(400, "offset can't be negative.");
            }
            cacheKey = $"banks:list:{c}:{o}";
            ttl = ListTtl;
            load = async () => Map(await _plaid.PostAsync("/institutions/get", new JsonObject
            {
                ["count"] = c,
                ["offset"] = o,
                ["country_codes"] = new JsonArray("US")
            }));
        }
        else
        {
            if (query.Length > 100)
            {
                throw new ApiException(400, "query is too long.");
            }
            cacheKey = "banks:search:" + query.ToLowerInvariant();
            ttl = SearchTtl;
            load = async () => Map(await _plaid.PostAsync("/institutions/search", new JsonObject
            {
                ["query"] = query,
                ["country_codes"] = new JsonArray("US")
            }));
        }

        if (_cache.TryGetValue(cacheKey, out List<BankInfoDto>? cached) && cached is not null)
        {
            return cached;
        }

        // One load per key at a time, so a busy checkout doesn't stampede Plaid.
        var gate = Gates.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }
            var banks = await load();
            _cache.Set(cacheKey, banks, ttl);
            return banks;
        }
        finally
        {
            gate.Release();
            if (Gates.Count > 1000)
            {
                Gates.Clear();
            }
        }
    }

    private static List<BankInfoDto> Map(JsonObject response)
    {
        return (response["institutions"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(i =>
        {
            var products = (i["products"] as JsonArray)?.Select(p => p!.GetValue<string>()).ToList() ?? new List<string>();
            var id = i["institution_id"]!.GetValue<string>();
            return new BankInfoDto
            {
                Id = id,
                InstitutionId = id,
                Name = i["name"]?.GetValue<string>() ?? id,
                Url = i["url"]?.GetValue<string>(),
                Oauth = i["oauth"]?.GetValue<bool>() ?? false,
                SupportsPayments = products.Contains("auth") || products.Contains("transfer"),
                Products = products
            };
        }).ToList();
    }
}
