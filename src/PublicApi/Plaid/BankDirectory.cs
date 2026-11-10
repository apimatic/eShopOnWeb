using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public class BankDto
{
    public string InstitutionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Logo { get; set; }
    public string? PrimaryColor { get; set; }
    public bool Oauth { get; set; }
    public List<string> CountryCodes { get; set; } = new();
    public List<string> Products { get; set; } = new();
}

public class BankPage
{
    public List<BankDto> Banks { get; set; } = new();
    /// <summary>How many banks Plaid supports (or, for a search, how many matched).</summary>
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// The banks Plaid supports. Plaid has over ten thousand and rate-limits /institutions/get to a
/// handful of calls a minute, while every checkout opens the bank picker. So the list is read a page
/// at a time, each page is cached (and shared by concurrent requests), and a stale copy is served if
/// Plaid refuses.
/// </summary>
public class BankDirectory
{
    public static readonly string[] CountryCodes = { "US", "CA", "GB", "ES", "FR", "IE", "NL", "DE" };

    public const int DefaultCount = 100;
    public const int MaxCount = 500; // Plaid's maximum for /institutions/get

    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(6);
    private static readonly TimeSpan StaleFor = TimeSpan.FromDays(7);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly PlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public BankDirectory(PlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public Task<BankPage> GetBanksAsync(string? query, int? count, int? offset, CancellationToken ct)
    {
        var take = count ?? DefaultCount;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxCount)
        {
            throw ApiProblemException.BadRequest($"count must be between 1 and {MaxCount}.");
        }

        if (skip < 0)
        {
            throw ApiProblemException.BadRequest("offset cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return CachedAsync($"plaid-institutions:{take}:{skip}", () => ListAsync(take, skip, ct), ct);
        }

        var q = query.Trim();
        return CachedAsync($"plaid-institution-search:{q.ToLowerInvariant()}:{take}:{skip}", () => SearchAsync(q, take, skip, ct), ct);
    }

    private async Task<BankPage> ListAsync(int count, int offset, CancellationToken ct)
    {
        var json = await _plaid.PostAsync("/institutions/get", new
        {
            count,
            offset,
            country_codes = CountryCodes,
            options = new { include_optional_metadata = true }
        }, ct);
        var banks = Map(json);
        return new BankPage { Banks = banks, Total = json["total"]?.GetValue<int>() ?? banks.Count, Offset = offset, Count = banks.Count };
    }

    private async Task<BankPage> SearchAsync(string query, int count, int offset, CancellationToken ct)
    {
        // /institutions/search has no paging; page over its (short) answer here.
        var json = await _plaid.PostAsync("/institutions/search", new
        {
            query,
            country_codes = CountryCodes,
            options = new { include_optional_metadata = true }
        }, ct);
        var all = Map(json);
        var page = all.Skip(offset).Take(count).ToList();
        return new BankPage { Banks = page, Total = all.Count, Offset = offset, Count = page.Count };
    }

    private async Task<BankPage> CachedAsync(string key, Func<Task<BankPage>> load, CancellationToken ct)
    {
        if (_cache.TryGetValue(key, out BankPage? hit) && hit is not null)
        {
            return hit;
        }

        var gate = Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out hit) && hit is not null)
            {
                return hit;
            }

            try
            {
                var page = await load();
                _cache.Set(key, page, FreshFor);
                _cache.Set(key + ":stale", page, StaleFor);
                return page;
            }
            catch (ApiProblemException) when (_cache.TryGetValue(key + ":stale", out BankPage? stale) && stale is not null)
            {
                // Better an old list than a checkout that cannot open its bank picker.
                _cache.Set(key, stale, TimeSpan.FromMinutes(2));
                return stale;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static List<BankDto> Map(JsonNode json) =>
        (json["institutions"]?.AsArray() ?? new JsonArray()).Select(i => new BankDto
        {
            InstitutionId = i!["institution_id"]?.GetValue<string>() ?? string.Empty,
            Name = i["name"]?.GetValue<string>() ?? string.Empty,
            Url = i["url"]?.GetValue<string>(),
            Logo = i["logo"]?.GetValue<string>(),
            PrimaryColor = i["primary_color"]?.GetValue<string>(),
            Oauth = i["oauth"]?.GetValue<bool>() ?? false,
            CountryCodes = (i["country_codes"]?.AsArray() ?? new JsonArray()).Select(c => c!.GetValue<string>()).ToList(),
            Products = (i["products"]?.AsArray() ?? new JsonArray()).Select(c => c!.GetValue<string>()).ToList()
        }).ToList();
}
