using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Plaid;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankDto(string Id, string Name, string? Url, string? Logo, string? PrimaryColor);
public record BankPage(IReadOnlyList<BankDto> Banks, int Total, int Count, int Offset);

/// <summary>The bank picker: banks Plaid supports, searchable by name. Cached because every checkout opens it.</summary>
public class InstitutionService
{
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public InstitutionService(PlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<BankPage> GetAsync(string? query, int? count, int? offset, CancellationToken ct)
    {
        var take = Math.Clamp(count ?? 100, 1, 500);
        var skip = Math.Max(offset ?? 0, 0);
        query = query?.Trim();

        if (string.IsNullOrEmpty(query))
        {
            return await Cached($"banks:list:{take}:{skip}", ListTtl, async () =>
            {
                var response = await _plaid.PostAsync("institutions/get", new JsonObject
                {
                    ["count"] = take,
                    ["offset"] = skip,
                    ["country_codes"] = Json.Arr("US"),
                    ["options"] = new JsonObject { ["products"] = Json.Arr("auth"), ["include_optional_metadata"] = true }
                }, ct);
                var banks = Parse(response["institutions"]);
                return new BankPage(banks, response["total"]?.GetValue<int>() ?? banks.Count, banks.Count, skip);
            });
        }

        var all = await Cached($"banks:search:{query.ToLowerInvariant()}", SearchTtl, async () =>
        {
            var response = await _plaid.PostAsync("institutions/search", new JsonObject
            {
                ["query"] = query,
                ["country_codes"] = Json.Arr("US"),
                ["products"] = Json.Arr("auth"),
                ["options"] = new JsonObject { ["include_optional_metadata"] = true }
            }, ct);
            var banks = Parse(response["institutions"]);
            return new BankPage(banks, banks.Count, banks.Count, 0);
        });

        var page = all.Banks.Skip(skip).Take(take).ToList();
        return new BankPage(page, all.Total, page.Count, skip);
    }

    private static List<BankDto> Parse(JsonNode? institutions) =>
        (institutions as JsonArray ?? new JsonArray())
            .Select(i => new BankDto(
                Json.Str(i?["institution_id"]) ?? string.Empty,
                Json.Str(i?["name"]) ?? string.Empty,
                Json.Str(i?["url"]),
                Json.Str(i?["logo"]),
                Json.Str(i?["primary_color"])))
            .ToList();

    // Concurrent callers for the same key share one Plaid call.
    private async Task<T> Cached<T>(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        if (_cache.TryGetValue(key, out T? hit) && hit is not null) return hit;

        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out hit) && hit is not null) return hit;
            var value = await load();
            _cache.Set(key, value, ttl);
            return value;
        }
        finally
        {
            gate.Release();
        }
    }
}

public record LinkTokenResult(string LinkToken, string? Expiration);
public record ConnectionDto(Guid ConnectionId, string Type, string? InstitutionId, string? InstitutionName, string? AccountId, string? AccountName, string? AccountMask);

public class ConnectionService
{
    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;
    private readonly TokenProtector _protector;

    public ConnectionService(PlaidClient plaid, CatalogContext db, TokenProtector protector)
    {
        _plaid = plaid;
        _db = db;
        _protector = protector;
    }

    public static string NormalizeKind(string? type)
    {
        var kind = (type ?? BankConnectionKinds.Bank).Trim().ToLowerInvariant();
        if (kind is not (BankConnectionKinds.Bank or BankConnectionKinds.Brokerage))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "type must be 'bank' or 'brokerage'.");
        }
        return kind;
    }

    public async Task<LinkTokenResult> CreateLinkTokenAsync(string userName, string? type, CancellationToken ct)
    {
        var kind = NormalizeKind(type);
        var body = new JsonObject
        {
            ["client_name"] = "eShopOnWeb",
            ["language"] = "en",
            ["country_codes"] = Json.Arr("US"),
            // Plaid asks for a stable id that is not personally identifying.
            ["user"] = new JsonObject { ["client_user_id"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userName.ToLowerInvariant()))) }
        };

        if (kind == BankConnectionKinds.Bank)
        {
            body["products"] = Json.Arr("auth");
            // Consent to transactions up front so support can look up activity on every connected bank.
            body["additional_consented_products"] = Json.Arr("transactions");
            body["account_filters"] = new JsonObject { ["depository"] = new JsonObject { ["account_subtypes"] = Json.Arr("checking", "savings") } };
        }
        else
        {
            body["products"] = Json.Arr("investments");
            body["account_filters"] = new JsonObject { ["investment"] = new JsonObject { ["account_subtypes"] = Json.Arr("all") } };
        }

        var response = await _plaid.PostAsync("link/token/create", body, ct);
        return new LinkTokenResult(Json.Str(response["link_token"]) ?? string.Empty, Json.Str(response["expiration"]));
    }

    public async Task<ConnectionDto> SaveAsync(string userName, string? publicToken, string? type, string? accountId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(publicToken))
        {
            throw new ApiException(HttpStatusCode.BadRequest, "publicToken is required.");
        }
        var kind = NormalizeKind(type);

        var exchange = await _plaid.PostAsync("item/public_token/exchange", new JsonObject { ["public_token"] = publicToken }, ct);
        var accessToken = Json.Str(exchange["access_token"]) ?? throw new ApiException(HttpStatusCode.BadGateway, "Plaid returned no access token.");
        var itemId = Json.Str(exchange["item_id"]) ?? string.Empty;

        try
        {
            var existing = await _db.BankConnections.FirstOrDefaultAsync(c => c.UserName == userName && c.ItemId == itemId, ct);
            if (existing is not null)
            {
                return ToDto(existing);
            }

            var connection = new BankConnection { UserName = userName, Kind = kind, ItemId = itemId };

            var accounts = await _plaid.PostAsync("accounts/get", new JsonObject { ["access_token"] = accessToken }, ct);
            var list = (accounts["accounts"] as JsonArray ?? new JsonArray()).ToList();

            if (kind == BankConnectionKinds.Bank)
            {
                var depository = list.Where(a => Json.Str(a?["type"]) == "depository").ToList();
                JsonNode? chosen;
                if (!string.IsNullOrWhiteSpace(accountId))
                {
                    chosen = depository.FirstOrDefault(a => Json.Str(a?["account_id"]) == accountId)
                        ?? throw new ApiException(HttpStatusCode.BadRequest, "accountId is not a bank account of this connection.");
                }
                else
                {
                    chosen = depository.FirstOrDefault(a => Json.Str(a?["subtype"]) == "checking") ?? depository.FirstOrDefault()
                        ?? throw new ApiException(HttpStatusCode.Conflict, "The connection has no checking or savings account to pay from.");
                }
                connection.AccountId = Json.Str(chosen?["account_id"]);
                connection.AccountName = Json.Str(chosen?["name"]);
                connection.AccountMask = Json.Str(chosen?["mask"]);
            }
            else if (!list.Any(a => Json.Str(a?["type"]) == "investment"))
            {
                throw new ApiException(HttpStatusCode.Conflict, "The connection has no brokerage account.");
            }

            var item = await _plaid.PostAsync("item/get", new JsonObject { ["access_token"] = accessToken }, ct);
            connection.InstitutionId = Json.Str(item["item"]?["institution_id"]);
            if (connection.InstitutionId is not null)
            {
                try
                {
                    var inst = await _plaid.PostAsync("institutions/get_by_id", new JsonObject
                    {
                        ["institution_id"] = connection.InstitutionId,
                        ["country_codes"] = Json.Arr("US")
                    }, ct);
                    connection.InstitutionName = Json.Str(inst["institution"]?["name"]);
                }
                catch (ApiException)
                {
                    // The name is cosmetic.
                }
            }

            connection.ProtectedAccessToken = _protector.Protect(accessToken);
            _db.BankConnections.Add(connection);
            await _db.SaveChangesAsync(ct);
            return ToDto(connection);
        }
        catch
        {
            // Do not leave an Item behind that we are paying Plaid for but cannot use.
            if (!await _db.BankConnections.AnyAsync(c => c.ItemId == itemId, CancellationToken.None))
            {
                try { await RemoveItemAsync(accessToken, CancellationToken.None); } catch (ApiException) { }
            }
            throw;
        }
    }

    public async Task DisconnectAsync(string userName, Guid connectionId, CancellationToken ct)
    {
        var connection = await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserName == userName, ct)
            ?? throw new ApiException(HttpStatusCode.NotFound, "No such connection.");

        // Remove the Item at Plaid first, so we stop paying for it. If that fails the connection stays and the caller can retry.
        await RemoveItemAsync(_protector.Unprotect(connection.ProtectedAccessToken), ct);

        _db.BankTransactions.RemoveRange(await _db.BankTransactions.Where(t => t.ConnectionId == connection.Id).ToListAsync(ct));
        _db.BankConnections.Remove(connection);
        await _db.SaveChangesAsync(ct);
    }

    private async Task RemoveItemAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            await _plaid.PostAsync("item/remove", new JsonObject { ["access_token"] = accessToken }, ct);
        }
        catch (PlaidApiException ex) when (ex.ErrorCode is "ITEM_NOT_FOUND" or "INVALID_ACCESS_TOKEN")
        {
            // Already gone at Plaid.
        }
    }

    /// <summary>The caller's own connection; someone else's is reported as not found.</summary>
    public async Task<BankConnection> GetOwnedAsync(string userName, Guid connectionId, CancellationToken ct) =>
        await _db.BankConnections.FirstOrDefaultAsync(c => c.Id == connectionId && c.UserName == userName, ct)
            ?? throw new ApiException(HttpStatusCode.NotFound, "No such connection.");

    public Task<List<BankConnection>> ListAsync(string userName, string kind, CancellationToken ct) =>
        _db.BankConnections.Where(c => c.UserName == userName && c.Kind == kind).OrderBy(c => c.CreatedOn).ToListAsync(ct);

    public string AccessToken(BankConnection connection) => _protector.Unprotect(connection.ProtectedAccessToken);

    private static ConnectionDto ToDto(BankConnection c) =>
        new(c.Id, c.Kind, c.InstitutionId, c.InstitutionName, c.AccountId, c.AccountName, c.AccountMask);
}
