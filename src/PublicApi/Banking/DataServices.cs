using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record ActivityTransaction(string Id, string Name, decimal Amount, string Date, bool Pending, string? AccountId, string? Bank);
public record BankProblem(Guid ConnectionId, string? Bank, string Reason);
public record BankActivity(string Email, IReadOnlyList<ActivityTransaction> Transactions, IReadOnlyList<BankProblem> Unavailable);

public record RangeTransaction(string Id, string Name, decimal Amount, string Date, string? AccountId, string? Bank);
public record BankTransactions(string Email, string From, string To, IReadOnlyList<RangeTransaction> Transactions, IReadOnlyList<BankProblem> Unavailable);

public class ShopperResolver
{
    private readonly UserManager<ApplicationUser> _users;

    public ShopperResolver(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    /// <summary>Finds the shopper by email; returns the user name their connections are stored under.</summary>
    public async Task<string> ResolveAsync(string email)
    {
        var user = await _users.FindByEmailAsync(email) ?? await _users.FindByNameAsync(email);
        return user?.UserName ?? throw new ApiException(HttpStatusCode.NotFound, "No such shopper.");
    }
}

public class TransactionService
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> SyncLocks = new();
    private const int PageSize = 500;

    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly ConnectionService _connections;

    public TransactionService(PlaidClient plaid, CatalogContext db, ConnectionService connections)
    {
        _plaid = plaid;
        _db = db;
        _connections = connections;
    }

    // ---- Local copy, kept in step with /transactions/sync ----

    public async Task<BankActivity> GetActivityAsync(string email, string userName, CancellationToken ct)
    {
        var banks = await _connections.ListAsync(userName, BankConnectionKinds.Bank, ct);
        var problems = new List<BankProblem>();

        foreach (var bank in banks)
        {
            try
            {
                await SyncAsync(bank, ct);
            }
            catch (ApiException ex)
            {
                problems.Add(new BankProblem(bank.Id, bank.InstitutionName, ex.Message));
            }
        }

        var ids = banks.Select(b => b.Id).ToList();
        var rows = await _db.BankTransactions.Where(t => ids.Contains(t.ConnectionId)).ToListAsync(ct);
        var names = banks.ToDictionary(b => b.Id, b => b.InstitutionName);
        var transactions = rows
            .OrderByDescending(t => t.Date).ThenBy(t => t.TransactionId, StringComparer.Ordinal)
            .Select(t => new ActivityTransaction(t.TransactionId, t.Name, t.Amount, t.Date.ToString("yyyy-MM-dd"), t.Pending, t.AccountId, names.GetValueOrDefault(t.ConnectionId)))
            .ToList();
        return new BankActivity(email, transactions, problems);
    }

    public async Task SyncAsync(BankConnection connection, CancellationToken ct)
    {
        var gate = SyncLocks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var accessToken = _connections.AccessToken(connection);
            var startCursor = connection.TransactionsCursor;

            List<JsonNode> added = new(), modified = new();
            List<string> removed = new();
            string? cursor = startCursor;
            var restarts = 0;
            var notReadyPolls = 0;

            while (true)
            {
                JsonObject page;
                try
                {
                    var body = new JsonObject { ["access_token"] = accessToken, ["count"] = PageSize };
                    if (!string.IsNullOrEmpty(cursor)) body["cursor"] = cursor;
                    page = await _plaid.PostAsync("transactions/sync", body, ct);
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && restarts < 3)
                {
                    // The data changed while paging: start over from where this sync began.
                    restarts++;
                    added.Clear(); modified.Clear(); removed.Clear();
                    cursor = startCursor;
                    continue;
                }
                catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && notReadyPolls < 8)
                {
                    notReadyPolls++;
                    await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                    continue;
                }

                var pageAdded = page["added"] as JsonArray ?? new JsonArray();
                var pageModified = page["modified"] as JsonArray ?? new JsonArray();
                var pageRemoved = page["removed"] as JsonArray ?? new JsonArray();
                added.AddRange(pageAdded.Where(n => n is not null)!);
                modified.AddRange(pageModified.Where(n => n is not null)!);
                removed.AddRange(pageRemoved.Select(r => Json.Str(r?["transaction_id"])).Where(s => s is not null)!);
                cursor = Json.Str(page["next_cursor"]) ?? cursor;

                if (page["has_more"]?.GetValue<bool>() == true) continue;

                // A brand-new Item reports an empty first update until Plaid has pulled its history: wait for it a little.
                var status = Json.Str(page["transactions_update_status"]);
                var nothingYet = string.IsNullOrEmpty(startCursor) && added.Count == 0 && modified.Count == 0
                    && status is "NOT_READY" or "INITIAL_UPDATE_COMPLETE";
                if (nothingYet && notReadyPolls < 8)
                {
                    notReadyPolls++;
                    await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                    cursor = startCursor;
                    continue;
                }
                break;
            }

            await ApplyAsync(connection, added, modified, removed, cursor, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task ApplyAsync(BankConnection connection, List<JsonNode> added, List<JsonNode> modified, List<string> removed, string? cursor, CancellationToken ct)
    {
        // Within one sync an id can be added, modified and removed: apply in that order.
        var final = new Dictionary<string, BankTransaction?>();
        foreach (var node in added.Concat(modified))
        {
            var id = Json.Str(node["transaction_id"]);
            if (id is null) continue;
            final[id] = new BankTransaction
            {
                ConnectionId = connection.Id,
                TransactionId = id,
                AccountId = Json.Str(node["account_id"]) ?? string.Empty,
                Name = Json.Str(node["name"]) ?? Json.Str(node["merchant_name"]) ?? string.Empty,
                Amount = Json.Dec(node["amount"]),
                Date = DateTime.Parse(Json.Str(node["date"]) ?? Json.Str(node["authorized_date"])!, System.Globalization.CultureInfo.InvariantCulture),
                Pending = node["pending"]?.GetValue<bool>() ?? false
            };
        }
        foreach (var id in removed) final[id] = null;

        foreach (var chunk in final.Keys.Chunk(500))
        {
            var existing = await _db.BankTransactions
                .Where(t => t.ConnectionId == connection.Id && chunk.Contains(t.TransactionId))
                .ToDictionaryAsync(t => t.TransactionId, ct);
            foreach (var id in chunk)
            {
                var incoming = final[id];
                existing.TryGetValue(id, out var current);
                if (incoming is null)
                {
                    if (current is not null) _db.BankTransactions.Remove(current);
                }
                else if (current is null)
                {
                    _db.BankTransactions.Add(incoming);
                }
                else
                {
                    current.AccountId = incoming.AccountId;
                    current.Name = incoming.Name;
                    current.Amount = incoming.Amount;
                    current.Date = incoming.Date;
                    current.Pending = incoming.Pending;
                }
            }
        }

        // The cursor moves together with the data it covers.
        var tracked = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connection.Id, ct);
        if (tracked is not null)
        {
            tracked.TransactionsCursor = cursor;
            connection.TransactionsCursor = cursor;
        }
        await _db.SaveChangesAsync(ct);
    }

    // ---- Straight from the bank, between two dates ----

    public async Task<BankTransactions> GetRangeAsync(string email, string userName, DateTime from, DateTime to, CancellationToken ct)
    {
        var banks = await _connections.ListAsync(userName, BankConnectionKinds.Bank, ct);
        var transactions = new List<RangeTransaction>();
        var problems = new List<BankProblem>();

        foreach (var bank in banks)
        {
            try
            {
                var accessToken = _connections.AccessToken(bank);
                var offset = 0;
                var total = int.MaxValue;
                var notReadyPolls = 0;
                while (offset < total)
                {
                    JsonObject page;
                    try
                    {
                        page = await _plaid.PostAsync("transactions/get", new JsonObject
                        {
                            ["access_token"] = accessToken,
                            ["start_date"] = from.ToString("yyyy-MM-dd"),
                            ["end_date"] = to.ToString("yyyy-MM-dd"),
                            ["options"] = new JsonObject { ["count"] = PageSize, ["offset"] = offset }
                        }, ct);
                    }
                    catch (PlaidApiException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && notReadyPolls < 8)
                    {
                        // The Item's transactions are still being prepared (first lookup on an older connection).
                        notReadyPolls++;
                        await Task.Delay(TimeSpan.FromSeconds(1.5), ct);
                        continue;
                    }

                    total = page["total_transactions"]?.GetValue<int>() ?? 0;
                    var batch = page["transactions"] as JsonArray ?? new JsonArray();
                    if (batch.Count == 0) break;
                    foreach (var t in batch)
                    {
                        transactions.Add(new RangeTransaction(
                            Json.Str(t?["transaction_id"]) ?? string.Empty,
                            Json.Str(t?["name"]) ?? Json.Str(t?["merchant_name"]) ?? string.Empty,
                            Json.Dec(t?["amount"]),
                            Json.Str(t?["date"]) ?? string.Empty,
                            Json.Str(t?["account_id"]),
                            bank.InstitutionName));
                    }
                    offset += batch.Count;
                }
            }
            catch (ApiException ex)
            {
                problems.Add(new BankProblem(bank.Id, bank.InstitutionName, ex.Message));
            }
        }

        var ordered = transactions.OrderByDescending(t => t.Date).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        return new BankTransactions(email, from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"), ordered, problems);
    }
}

public class PortfolioPosition
{
    public string? Ticker { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Value { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Quantity { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Contracts { get; set; }
    public string? Type { get; set; }
    public string? Bank { get; set; }
    public string? AccountId { get; set; }
}

public class PortfolioService
{
    /// <summary>Shares covered by one standard option contract.</summary>
    private const decimal OptionMultiplier = 100m;

    private readonly PlaidClient _plaid;
    private readonly ConnectionService _connections;

    public PortfolioService(PlaidClient plaid, ConnectionService connections)
    {
        _plaid = plaid;
        _connections = connections;
    }

    public async Task<List<PortfolioPosition>> GetAsync(string userName, CancellationToken ct)
    {
        var positions = new List<PortfolioPosition>();
        foreach (var connection in await _connections.ListAsync(userName, BankConnectionKinds.Brokerage, ct))
        {
            var response = await _plaid.PostAsync("investments/holdings/get",
                new JsonObject { ["access_token"] = _connections.AccessToken(connection) }, ct);

            var securities = (response["securities"] as JsonArray ?? new JsonArray())
                .Where(s => s is not null)
                .GroupBy(s => Json.Str(s!["security_id"]) ?? string.Empty)
                .ToDictionary(g => g.Key, g => g.First()!);

            foreach (var holding in response["holdings"] as JsonArray ?? new JsonArray())
            {
                var securityId = Json.Str(holding?["security_id"]) ?? string.Empty;
                securities.TryGetValue(securityId, out var security);
                var quantity = Json.Dec(holding?["quantity"]);
                var isOption = security?["option_contract"] is JsonObject || Json.Str(security?["type"]) == "derivative";

                var position = new PortfolioPosition
                {
                    Ticker = Json.Str(security?["ticker_symbol"]),
                    Name = Json.Str(security?["name"]) ?? string.Empty,
                    Value = Json.Dec(holding?["institution_value"]),
                    Type = Json.Str(security?["type"]),
                    Bank = connection.InstitutionName,
                    AccountId = Json.Str(holding?["account_id"])
                };

                if (isOption)
                {
                    // Plaid reports options in underlying units (price is per share): contracts are 100 units each.
                    position.Contracts = quantity / OptionMultiplier;
                }
                else
                {
                    position.Quantity = quantity;
                }
                positions.Add(position);
            }
        }
        return positions;
    }
}
