using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record BankDto(string Id, string Name);

/// <summary>
/// The bank picker. Every checkout opens it, and the list is the same for everybody,
/// so the full list is fetched from Plaid once and shared instead of once per checkout.
/// </summary>
public class BankPickerService
{
    private const string CacheKey = "plaid-institutions";
    private static readonly object Sync = new();
    private static Task<IReadOnlyList<BankDto>>? _loading;
    // Progress survives a failed attempt, so a rate-limited load resumes instead of restarting.
    private static readonly List<BankDto> Partial = new();
    private readonly IPlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public BankPickerService(IPlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<IReadOnlyList<BankDto>> SearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return await GetAllAsync();
        }

        var q = query.Trim();
        if (q.Length > 100)
        {
            throw new ApiException(400, "query is too long.");
        }

        var key = "plaid-institutions-search:" + q.ToLowerInvariant();
        if (_cache.TryGetValue(key, out IReadOnlyList<BankDto>? hit) && hit != null)
        {
            return hit;
        }

        var response = await _plaid.PostAsync("/institutions/search", new JsonObject
        {
            ["query"] = q,
            ["country_codes"] = new JsonArray("US"),
            ["products"] = new JsonArray("auth"),
        });
        IReadOnlyList<BankDto> found = response["institutions"]!.AsArray()
            .Select(i => new BankDto(i!["institution_id"]!.GetValue<string>(), i["name"]!.GetValue<string>())).ToList();
        _cache.Set(key, found, TimeSpan.FromMinutes(10));
        return found;
    }

    private Task<IReadOnlyList<BankDto>> GetAllAsync()
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<BankDto>? cached) && cached != null)
        {
            return Task.FromResult(cached);
        }

        lock (Sync)
        {
            // Everyone opening checkout at once shares one load.
            if (_loading == null || _loading.IsFaulted || _loading.IsCanceled)
            {
                _loading = LoadAllAsync();
            }
            return _loading;
        }
    }

    private async Task<IReadOnlyList<BankDto>> LoadAllAsync()
    {
        const int pageSize = 500;
        var rateLimited = 0;
        while (true)
        {
            int offset;
            lock (Sync) { offset = Partial.Count; }
            JsonNode page;
            try
            {
                page = await _plaid.PostAsync("/institutions/get", new JsonObject
                {
                    ["count"] = pageSize,
                    ["offset"] = offset,
                    ["country_codes"] = new JsonArray("US"),
                    // Only banks a shopper can actually pay from.
                    ["options"] = new JsonObject { ["products"] = new JsonArray("auth") },
                });
            }
            catch (ApiException ex) when (ex.StatusCode == 429 && rateLimited++ < 8)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                continue;
            }

            var items = page["institutions"]!.AsArray();
            var total = page["total"]?.GetValue<int>() ?? 0;
            bool done;
            lock (Sync)
            {
                Partial.AddRange(items.Select(i => new BankDto(i!["institution_id"]!.GetValue<string>(), i["name"]!.GetValue<string>())));
                done = items.Count == 0 || Partial.Count >= total;
            }
            if (done) break;
        }

        IReadOnlyList<BankDto> result;
        lock (Sync)
        {
            result = Partial.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Partial.Clear();
        }
        _cache.Set(CacheKey, result, TimeSpan.FromHours(6));
        return result;
    }
}

public class BankTransactionDto
{
    public int ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Date { get; set; } = string.Empty;
    public bool? Pending { get; set; }
}

public record ActivityWarning(int ConnectionId, string Institution, string Message);

public class BankActivityService
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private readonly CatalogContext _db;
    private readonly IPlaidClient _plaid;

    public BankActivityService(CatalogContext db, IPlaidClient plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    private async Task<List<BankConnection>> BankConnectionsAsync(string email)
    {
        var buyer = Buyer.Normalize(email);
        var connections = await _db.BankConnections
            .Where(c => c.BuyerId == buyer && c.HasBankAccounts).OrderBy(c => c.Id).ToListAsync();
        if (connections.Count == 0)
        {
            throw new ApiException(404, "That shopper has no connected bank accounts.");
        }
        return connections;
    }

    /// <summary>The shop's own copy, brought up to date with the bank first, for every connected bank.</summary>
    public async Task<(List<BankTransactionDto> Transactions, List<ActivityWarning> Warnings)> GetActivityAsync(string email)
    {
        var connections = await BankConnectionsAsync(email);
        var warnings = new List<ActivityWarning>();
        foreach (var connection in connections)
        {
            try
            {
                await SyncAsync(connection);
            }
            catch (ApiException ex)
            {
                warnings.Add(new ActivityWarning(connection.Id, connection.InstitutionName, ex.Message));
            }
        }

        var ids = connections.Select(c => c.Id).ToList();
        var rows = await _db.BankTransactions.Where(t => ids.Contains(t.ConnectionId)).ToListAsync();
        var dtos = rows.OrderByDescending(t => t.Date).ThenBy(t => t.Id).Select(t => new BankTransactionDto
        {
            ConnectionId = t.ConnectionId,
            AccountId = t.AccountId,
            Name = t.Name,
            Amount = t.Amount,
            Date = t.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Pending = t.Pending,
        }).ToList();
        if (warnings.Count == connections.Count)
        {
            throw new ApiException(502, warnings[0].Message);
        }
        return (dtos, warnings);
    }

    private async Task SyncAsync(BankConnection connection)
    {
        var gate = Locks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            await _db.Entry(connection).ReloadAsync();
            var startCursor = connection.TransactionsCursor;
            var added = new List<JsonNode>();
            var modified = new List<JsonNode>();
            var removed = new List<string>();
            string cursor = startCursor ?? string.Empty;

            for (var attempt = 0; ; attempt++)
            {
                added.Clear(); modified.Clear(); removed.Clear();
                cursor = startCursor ?? string.Empty;
                try
                {
                    bool hasMore;
                    do
                    {
                        var body = new JsonObject { ["access_token"] = connection.AccessToken, ["count"] = 500 };
                        if (cursor.Length > 0) body["cursor"] = cursor;
                        var page = await _plaid.PostAsync("/transactions/sync", body);
                        added.AddRange(page["added"]!.AsArray().Select(n => n!));
                        modified.AddRange(page["modified"]!.AsArray().Select(n => n!));
                        removed.AddRange(page["removed"]!.AsArray().Select(n => n!["transaction_id"]!.GetValue<string>()));
                        cursor = page["next_cursor"]!.GetValue<string>();
                        hasMore = page["has_more"]!.GetValue<bool>();
                    } while (hasMore);
                    break;
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && attempt < 3)
                {
                    // The data changed between pages: start again from the last saved cursor.
                }
            }

            var existing = (await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id).ToListAsync())
                .ToDictionary(t => t.PlaidTransactionId);

            foreach (var node in added.Concat(modified))
            {
                var id = node["transaction_id"]!.GetValue<string>();
                if (!existing.TryGetValue(id, out var row))
                {
                    row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = id };
                    _db.BankTransactions.Add(row);
                    existing[id] = row;
                }
                row.AccountId = node["account_id"]?.GetValue<string>() ?? string.Empty;
                row.Name = node["name"]?.GetValue<string>() ?? node["merchant_name"]?.GetValue<string>() ?? string.Empty;
                row.Amount = node["amount"]?.GetValue<decimal>() ?? 0m;
                row.IsoCurrencyCode = node["iso_currency_code"]?.GetValue<string>();
                row.Date = DateTime.ParseExact(node["date"]!.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                row.Pending = node["pending"]?.GetValue<bool>() ?? false;
            }

            foreach (var id in removed)
            {
                if (existing.TryGetValue(id, out var row))
                {
                    if (_db.Entry(row).State == EntityState.Added)
                    {
                        _db.Entry(row).State = EntityState.Detached;
                    }
                    else
                    {
                        _db.BankTransactions.Remove(row);
                    }
                    existing.Remove(id);
                }
            }

            connection.TransactionsCursor = cursor.Length > 0 ? cursor : null;
            await _db.SaveChangesAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Straight from the bank: no local copy involved, so it works for every connection.</summary>
    public async Task<(List<BankTransactionDto> Transactions, List<ActivityWarning> Warnings)> GetTransactionsBetweenAsync(
        string email, DateOnly from, DateOnly to)
    {
        var connections = await BankConnectionsAsync(email);
        var all = new List<BankTransactionDto>();
        var warnings = new List<ActivityWarning>();

        foreach (var connection in connections)
        {
            try
            {
                all.AddRange(await FetchRangeAsync(connection, from, to));
            }
            catch (ApiException ex)
            {
                warnings.Add(new ActivityWarning(connection.Id, connection.InstitutionName, ex.Message));
            }
        }

        if (warnings.Count == connections.Count)
        {
            throw new ApiException(502, warnings[0].Message);
        }
        return (all.OrderByDescending(t => t.Date).ToList(), warnings);
    }

    private async Task<List<BankTransactionDto>> FetchRangeAsync(BankConnection connection, DateOnly from, DateOnly to)
    {
        var result = new List<BankTransactionDto>();
        var offset = 0;
        var notReady = 0;
        while (true)
        {
            JsonNode page;
            try
            {
                page = await _plaid.PostAsync("/transactions/get", new JsonObject
                {
                    ["access_token"] = connection.AccessToken,
                    ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["options"] = new JsonObject { ["count"] = 500, ["offset"] = offset },
                });
            }
            catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && notReady++ < 5)
            {
                // Connections made before transactions were used are enabled on first use and need a moment.
                await Task.Delay(TimeSpan.FromSeconds(2));
                continue;
            }

            var items = page["transactions"]!.AsArray();
            result.AddRange(items.Select(t => new BankTransactionDto
            {
                ConnectionId = connection.Id,
                AccountId = t!["account_id"]?.GetValue<string>() ?? string.Empty,
                Name = t["name"]?.GetValue<string>() ?? t["merchant_name"]?.GetValue<string>() ?? string.Empty,
                Amount = t["amount"]?.GetValue<decimal>() ?? 0m,
                Date = t["date"]!.GetValue<string>(),
                Pending = t["pending"]?.GetValue<bool>(),
            }));
            offset += items.Count;
            var total = page["total_transactions"]?.GetValue<int>() ?? 0;
            if (items.Count == 0 || offset >= total)
            {
                return result;
            }
        }
    }
}

public class PositionDto
{
    public string? Ticker { get; set; }
    public string? Name { get; set; }
    public decimal Value { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }
    public int ConnectionId { get; set; }
    public string AccountId { get; set; } = string.Empty;
}

public class PortfolioService
{
    private readonly CatalogContext _db;
    private readonly IPlaidClient _plaid;

    public PortfolioService(CatalogContext db, IPlaidClient plaid)
    {
        _db = db;
        _plaid = plaid;
    }

    public async Task<List<PositionDto>> GetAsync(string buyer)
    {
        var connections = await _db.BankConnections
            .Where(c => c.BuyerId == buyer && c.HasInvestmentAccounts).OrderBy(c => c.Id).ToListAsync();
        if (connections.Count == 0)
        {
            throw new ApiException(404, "No brokerage account is connected.");
        }

        var positions = new List<PositionDto>();
        foreach (var connection in connections)
        {
            var response = await _plaid.PostAsync("/investments/holdings/get", new JsonObject { ["access_token"] = connection.AccessToken });
            var securities = response["securities"]!.AsArray()
                .ToDictionary(s => s!["security_id"]!.GetValue<string>(), s => s!);

            foreach (var h in response["holdings"]!.AsArray())
            {
                securities.TryGetValue(h!["security_id"]!.GetValue<string>(), out var security);
                var option = security?["option_contract"];
                var isOption = security?["type"]?.GetValue<string>() == "derivative" && option != null;
                var quantity = h["quantity"]?.GetValue<decimal>() ?? 0m;
                var ticker = security?["ticker_symbol"]?.GetValue<string>()
                             ?? (isOption ? option!["underlying_security_ticker"]?.GetValue<string>() : null);
                var name = security?["name"]?.GetValue<string>();
                if (name == null && isOption)
                {
                    name = $"{ticker} {option!["strike_price"]} {option["contract_type"]} {option["expiration_date"]}";
                }

                positions.Add(new PositionDto
                {
                    Ticker = ticker,
                    Name = name,
                    Value = h["institution_value"]?.GetValue<decimal>() ?? 0m,
                    Quantity = isOption ? null : quantity,
                    Contracts = isOption ? quantity : null,
                    ConnectionId = connection.Id,
                    AccountId = h["account_id"]?.GetValue<string>() ?? string.Empty,
                });
            }
        }
        return positions;
    }
}
