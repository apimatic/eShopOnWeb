using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Plaid.Services;

public sealed record BankInfo(string Id, string Name, string? Url, string? PrimaryColor, bool Oauth);

/// <summary>
/// The shop's bank picker. Every checkout opens it at once, so the (large, slow to page) list of supported
/// banks is fetched once and shared; concurrent callers wait for the same fetch instead of each hitting Plaid.
/// </summary>
public sealed class BankDirectoryService
{
    private const int PageSize = 500;
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);
    private const int MaxQueryLength = 100;

    private readonly PlaidAccess _plaid;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BankDirectoryService> _logger;

    // Last good copy of the whole list, served if a refresh fails: the picker must keep working.
    private static volatile IReadOnlyList<BankInfo>? _lastGoodList;

    public BankDirectoryService(PlaidAccess plaid, IMemoryCache cache, ILogger<BankDirectoryService> logger)
    {
        _plaid = plaid;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<BankInfo>> GetBanksAsync(string? query, CancellationToken ct)
    {
        query = query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            try
            {
                var list = await Shared("plaid:banks:all", ListTtl, () => LoadAllAsync());
                _lastGoodList = list;
                return list;
            }
            catch (Exception) when (_lastGoodList is not null)
            {
                _logger.LogWarning("Could not refresh the bank list; serving the last good copy.");
                return _lastGoodList;
            }
        }

        if (query.Length > MaxQueryLength)
            throw BankApiException.BadRequest($"The search query can be at most {MaxQueryLength} characters.");

        return await Shared("plaid:banks:q:" + query.ToLowerInvariant(), SearchTtl, () => SearchAsync(query));
    }

    /// <summary>Re-reads the whole list and swaps it in, so the cached copy never has to expire under a shopper.</summary>
    public async Task RefreshAsync()
    {
        var list = await LoadAllAsync();
        _cache.Set("plaid:banks:all", new Lazy<Task<IReadOnlyList<BankInfo>>>(() => Task.FromResult(list)),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = ListTtl });
        _lastGoodList = list;
    }

    private async Task<IReadOnlyList<BankInfo>> SearchAsync(string query)
    {
        var response = await Translate(() => _plaid.Client.InstitutionsSearch(new InstitutionsSearchOperationRequest
        {
            Body = new InstitutionsSearchRequest
            {
                Query = query,
                CountryCodes = new[] { CountryCode.Us },
                Products = new[] { Products.Transfer },
            },
        }));
        return response.Institutions.Select(Map).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<IReadOnlyList<BankInfo>> LoadAllAsync()
    {
        async Task<InstitutionsGetResponse> Page(int offset) => await Translate(() => _plaid.Client.InstitutionsGet(
            new InstitutionsGetOperationRequest
            {
                Body = new InstitutionsGetRequest
                {
                    Count = PageSize,
                    Offset = offset,
                    CountryCodes = new[] { CountryCode.Us },
                    Options = new InstitutionsGetRequestOptions { Products = new[] { Products.Transfer } },
                },
            }));

        async Task<InstitutionsGetResponse> PageWithBackoff(int offset)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await Page(offset);
                }
                catch (BankApiException e) when (e.StatusCode == 429 && attempt < 6)
                {
                    _logger.LogWarning("Plaid rate limit while loading banks (page at {Offset}); backing off.", offset);
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                }
            }
        }

        var first = await PageWithBackoff(0);
        var all = new List<Institution>(first.Institutions);
        var offsets = Enumerable.Range(1, Math.Max(0, (first.Total - 1) / PageSize)).Select(i => i * PageSize).ToList();

        // One page at a time: a burst of page requests is what trips Plaid's rate limit.
        foreach (var offset in offsets)
            all.AddRange((await PageWithBackoff(offset)).Institutions);

        return all.GroupBy(i => i.InstitutionId).Select(g => Map(g.First()))
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static BankInfo Map(Institution i) => new(i.InstitutionId, i.Name, i.Url, i.PrimaryColor, i.Oauth);

    private static async Task<T> Translate<T>(Func<Task<T>> call)
    {
        try { return await call(); }
        catch (Exception e) when (PlaidFailure.ToApiException(e) is { } mapped) { throw mapped; }
    }

    /// <summary>Single-flight cache: concurrent misses share one Lazy task; failures are not cached.</summary>
    private async Task<T> Shared<T>(string key, TimeSpan ttl, Func<Task<T>> load)
    {
        var lazy = _cache.GetOrCreate(key, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = ttl;
            return new Lazy<Task<T>>(load, LazyThreadSafetyMode.ExecutionAndPublication);
        })!;
        try
        {
            return await lazy.Value;
        }
        catch
        {
            _cache.Remove(key);
            throw;
        }
    }
}
