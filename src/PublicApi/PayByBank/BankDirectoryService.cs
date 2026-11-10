using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.PayByBank;

public record BankDto(string Id, string Name, string? Url, string? Logo, string? PrimaryColor, bool Oauth);

public record BankListDto(IReadOnlyList<BankDto> Banks, int? Total, int Count, int Offset);

/// <summary>
/// The bank picker's data. Every checkout opens the picker at once, so answers are cached
/// (and concurrent identical lookups share one Plaid call) to stay clear of Plaid's rate limits.
/// </summary>
public class BankDirectoryService
{
    public const int DefaultCount = 50;
    public const int MaxCount = 500;
    private static readonly TimeSpan CacheTime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StaleTime = TimeSpan.FromHours(24);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> InFlight = new();

    private readonly PlaidGateway _plaid;
    private readonly IMemoryCache _cache;

    public BankDirectoryService(PlaidGateway plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<BankListDto> ListAsync(string? query, int? count, int? offset)
    {
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        var take = count ?? DefaultCount;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxCount) throw Fail.BadRequest($"count must be between 1 and {MaxCount}.");
        if (skip < 0) throw Fail.BadRequest("offset must not be negative.");

        var key = $"banks|{query?.ToLowerInvariant()}|{take}|{skip}";
        if (_cache.TryGetValue(key, out BankListDto? hit) && hit != null) return hit;

        var gate = InFlight.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cache.TryGetValue(key, out hit) && hit != null) return hit;
            try
            {
                var result = query == null ? await LoadPageAsync(take, skip) : await SearchAsync(query, take, skip);
                _cache.Set(key, result, CacheTime);
                _cache.Set(StaleKey(key), result, StaleTime);
                return result;
            }
            catch (PayByBankException) when (_cache.TryGetValue(StaleKey(key), out BankListDto? stale) && stale != null)
            {
                // Plaid is unavailable or rate limiting: the picker must still open, so serve the last good answer.
                _cache.Set(key, stale, TimeSpan.FromMinutes(1));
                return stale;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string StaleKey(string key) => "stale|" + key;

    private async Task<BankListDto> LoadPageAsync(int take, int skip)
    {
        var response = await _plaid.Call(c => c.InstitutionsGet(new InstitutionsGetOperationRequest
        {
            Body = new InstitutionsGetRequest
            {
                Count = take,
                Offset = skip,
                CountryCodes = [CountryCode.Us],
                Options = new InstitutionsGetRequestOptions { IncludeOptionalMetadata = true }
            }
        }));
        return new BankListDto(response.Institutions.Select(ToDto).ToList(), response.Total, response.Institutions.Count, skip);
    }

    private async Task<BankListDto> SearchAsync(string query, int take, int skip)
    {
        var response = await _plaid.Call(c => c.InstitutionsSearch(new InstitutionsSearchOperationRequest
        {
            Body = new InstitutionsSearchRequest
            {
                Query = query,
                CountryCodes = [CountryCode.Us],
                Options = new InstitutionsSearchRequestOptions { IncludeOptionalMetadata = true }
            }
        }));
        // Plaid's search is not paged; page it here so the shape matches the unfiltered list.
        var page = response.Institutions.Skip(skip).Take(take).Select(ToDto).ToList();
        return new BankListDto(page, response.Institutions.Count, page.Count, skip);
    }

    private static BankDto ToDto(Institution i) => new(i.InstitutionId, i.Name, i.Url, i.Logo, i.PrimaryColor, i.Oauth);
}
