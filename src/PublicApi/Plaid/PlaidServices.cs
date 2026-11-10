using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BankAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>Shared helpers for the bank endpoints.</summary>
public static class BankApi
{
    public static IResult Fail(int status, string reason) =>
        Results.Json(new { statusCode = status, message = reason }, statusCode: status);

    public static IResult Fail(PlaidException ex) => Fail(ex.ResponseStatus, ex.Message);

    /// <summary>The caller's identity, from the JWT (never from the request).</summary>
    public static string? UserName(ClaimsPrincipal user)
    {
        var name = user.Identity?.Name ?? user.FindFirst(ClaimTypes.Name)?.Value;
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    public static string? Str(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is null) return null;
        try { return v.GetValue<string>(); } catch (InvalidOperationException) { return v.ToString(); }
    }

    public static decimal? Dec(this JsonNode? node, string key)
    {
        var v = node?[key];
        if (v is null) return null;
        try { return v.GetValue<decimal>(); } catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    public static bool Bool(this JsonNode? node, string key) =>
        node?[key] is { } v && v.GetValueKind() == System.Text.Json.JsonValueKind.True;

    public static JsonArray Arr(this JsonNode? node, string key) => node?[key] as JsonArray ?? new JsonArray();

    /// <summary>Retries a call while Plaid is still preparing a product for an item.</summary>
    public static async Task<T> WhenProductReady<T>(Func<Task<T>> call, CancellationToken ct, int attempts = 8, int delayMs = 2500)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (PlaidException ex) when (ex.ErrorCode == "PRODUCT_NOT_READY" && attempt < attempts)
            {
                await Task.Delay(delayMs, ct);
            }
        }
    }
}

/// <summary>Plaid's supported banks, cached so that every checkout opening the picker does not hit Plaid.</summary>
public class InstitutionService
{
    private static readonly SemaphoreSlim AllLock = new(1, 1);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> SearchLocks = new();
    private static readonly TimeSpan AllTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(15);
    private const int PageSize = 500;
    private readonly PlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public InstitutionService(PlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public record Bank(string Id, string Name, bool Oauth, IReadOnlyList<string> Products);

    private static Bank ToBank(JsonNode i) => new(
        i.Str("institution_id") ?? "",
        i.Str("name") ?? "",
        i.Bool("oauth"),
        i.Arr("products").Select(p => p?.GetValue<string>() ?? "").Where(p => p != "").ToList());

    public async Task<IReadOnlyList<Bank>> ListAsync(CancellationToken ct)
    {
        const string key = "plaid:institutions:all";
        if (_cache.TryGetValue(key, out IReadOnlyList<Bank>? cached) && cached is not null) return cached;

        await AllLock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null) return cached;

            var all = new List<Bank>();
            var offset = 0;
            while (true)
            {
                var page = await _plaid.PostAsync("/institutions/get", new JsonObject
                {
                    ["count"] = PageSize,
                    ["offset"] = offset,
                    ["country_codes"] = new JsonArray("US")
                }, ct, maxAttempts: 8, backoffMs: 4000);   // Plaid rate-limits this endpoint hard
                var items = page.Arr("institutions");
                all.AddRange(items.Where(i => i is not null).Select(i => ToBank(i!)));
                offset += items.Count;
                var total = page["total"]?.GetValue<int>() ?? 0;
                if (items.Count == 0 || offset >= total) break;
            }
            var result = all.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _cache.Set(key, (IReadOnlyList<Bank>)result, AllTtl);
            return result;
        }
        finally
        {
            AllLock.Release();
        }
    }

    public async Task<IReadOnlyList<Bank>> SearchAsync(string query, CancellationToken ct)
    {
        var key = "plaid:institutions:search:" + query.ToLowerInvariant();
        if (_cache.TryGetValue(key, out IReadOnlyList<Bank>? cached) && cached is not null) return cached;

        var gate = SearchLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out cached) && cached is not null) return cached;
            var answer = await _plaid.PostAsync("/institutions/search", new JsonObject
            {
                ["query"] = query,
                ["country_codes"] = new JsonArray("US")
            }, ct);
            var result = answer.Arr("institutions").Where(i => i is not null).Select(i => ToBank(i!)).ToList();
            _cache.Set(key, (IReadOnlyList<Bank>)result, SearchTtl);
            return result;
        }
        finally
        {
            gate.Release();
            if (SearchLocks.Count > 1000) SearchLocks.Clear();
        }
    }
}

/// <summary>Keeps the shop's own copy of a connection's transactions in step with the bank's records.</summary>
public class BankSyncService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();
    private readonly PlaidClient _plaid;
    private readonly CatalogContext _db;

    public BankSyncService(PlaidClient plaid, CatalogContext db)
    {
        _plaid = plaid;
        _db = db;
    }

    public async Task SyncAsync(BankConnection connection, CancellationToken ct)
    {
        var gate = Locks.GetOrAdd(connection.Id, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            // reload: another request may have advanced the cursor while we waited
            await _db.Entry(connection).ReloadAsync(ct);

            for (var restart = 0; ; restart++)
            {
                try
                {
                    await SyncOnceAsync(connection, ct);
                    return;
                }
                catch (PlaidException ex) when (ex.ErrorCode == "TRANSACTIONS_SYNC_MUTATION_DURING_PAGINATION" && restart < 3)
                {
                    // Plaid changed under us mid-pagination: start again from the saved cursor
                }
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task SyncOnceAsync(BankConnection connection, CancellationToken ct)
    {
        var added = new List<JsonNode>();
        var modified = new List<JsonNode>();
        var removed = new List<string>();
        var cursor = connection.TransactionsCursor;

        while (true)
        {
            var body = new JsonObject { ["access_token"] = connection.AccessToken, ["count"] = 500 };
            if (!string.IsNullOrEmpty(cursor)) body["cursor"] = cursor;
            var page = await BankApi.WhenProductReady(() => _plaid.PostAsync("/transactions/sync", body, ct), ct);

            added.AddRange(page.Arr("added").Where(n => n is not null)!);
            modified.AddRange(page.Arr("modified").Where(n => n is not null)!);
            removed.AddRange(page.Arr("removed").Select(r => r.Str("transaction_id")).Where(id => id is not null)!);
            cursor = page.Str("next_cursor");
            if (!page.Bool("has_more")) break;
        }

        var ids = added.Concat(modified).Select(t => t.Str("transaction_id")!).Concat(removed).Distinct().ToList();
        var existing = await _db.BankTransactions
            .Where(t => t.ConnectionId == connection.Id && ids.Contains(t.PlaidTransactionId))
            .ToDictionaryAsync(t => t.PlaidTransactionId, ct);

        foreach (var id in removed)
        {
            if (existing.Remove(id, out var gone)) _db.BankTransactions.Remove(gone);
        }
        foreach (var t in added.Concat(modified))
        {
            var id = t.Str("transaction_id")!;
            if (!existing.TryGetValue(id, out var row))
            {
                row = new BankTransaction { ConnectionId = connection.Id, PlaidTransactionId = id };
                _db.BankTransactions.Add(row);
                existing[id] = row;
            }
            row.AccountId = t.Str("account_id");
            row.Name = t.Str("name") ?? t.Str("merchant_name") ?? "";
            row.Amount = t.Dec("amount") ?? 0m;
            row.Date = t.Str("date") ?? "";
            row.Pending = t.Bool("pending");
        }
        connection.TransactionsCursor = cursor;
        await _db.SaveChangesAsync(ct);
    }
}

/// <summary>Loads the bank list in the background at startup so no shopper waits for it.</summary>
public class InstitutionWarmUp : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IServiceScopeFactory _scopes;

    public InstitutionWarmUp(IServiceScopeFactory scopes) => _scopes = scopes;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<InstitutionService>().ListAsync(stoppingToken);
        }
        catch (Exception)
        {
            // not configured or Plaid unavailable: the first request will try again
        }
    }
}

public static class PlaidServiceCollectionExtensions
{
    public static IServiceCollection AddPlaid(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PlaidSettings>(configuration.GetSection(PlaidSettings.SectionName));
        services.AddHttpClient<PlaidClient>(c => c.Timeout = TimeSpan.FromSeconds(60));
        services.AddScoped<InstitutionService>();
        services.AddScoped<BankSyncService>();
        services.AddHostedService<InstitutionWarmUp>();
        return services;
    }
}
