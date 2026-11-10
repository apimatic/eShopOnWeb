using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public sealed record BankDto(string Id, string Name, string? Logo, string? Url, bool Oauth);

public sealed record BankPage(IReadOnlyList<BankDto> Banks, int Total, int Offset, int Limit);

/// <summary>
/// The shop's bank picker. Every checkout opens it at once and Plaid rate limits its institutions endpoints, so
/// each page of banks (and each search) is fetched from Plaid once and shared by all shoppers. Concurrent requests
/// for the same page share one Plaid call, and when Plaid can't be asked again the last answer is still served.
/// </summary>
public class BankDirectoryService
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;

    private static readonly TimeSpan FreshFor = TimeSpan.FromHours(1);
    private static readonly TimeSpan KeepFor = TimeSpan.FromHours(24);
    private static readonly IReadOnlyList<CountryCode> Countries = new[] { CountryCode.Us };

    // One Plaid call at a time, so a burst of checkouts never turns into a burst of Plaid calls.
    private static readonly SemaphoreSlim FetchGate = new(1, 1);

    private sealed record Cached(BankPage Page, DateTimeOffset FetchedAt);

    private readonly PlaidApi _plaid;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _time;

    public BankDirectoryService(PlaidApi plaid, IMemoryCache cache, TimeProvider time)
    {
        _plaid = plaid;
        _cache = cache;
        _time = time;
    }

    public async Task<BankPage> GetBanksAsync(string? query, int? limit, int? offset)
    {
        query = query?.Trim();
        if (query is { Length: > 100 })
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "The query is too long (100 characters at most).");
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, $"limit must be between 1 and {MaxLimit}.");
        }

        if (offset is < 0)
        {
            throw new ApiRequestException(HttpStatusCode.BadRequest, "offset cannot be negative.");
        }

        var take = limit ?? DefaultLimit;
        var skip = offset ?? 0;

        if (string.IsNullOrEmpty(query))
        {
            return await CachedAsync($"plaid:banks:list:{take}:{skip}", async () =>
            {
                var response = await _plaid.CallAsync(c => c.InstitutionsGet(new InstitutionsGetOperationRequest
                {
                    Body = new InstitutionsGetRequest { Count = take, Offset = skip, CountryCodes = Countries }
                }));
                return new BankPage(response.Institutions.Select(ToDto).ToList(), response.Total, skip, take);
            });
        }

        // Plaid searches by name; the matches are one list that is paged here.
        var matches = await CachedAsync("plaid:banks:search:" + query.ToLowerInvariant(), async () =>
        {
            var response = await _plaid.CallAsync(c => c.InstitutionsSearch(new InstitutionsSearchOperationRequest
            {
                Body = new InstitutionsSearchRequest { Query = query, CountryCodes = Countries }
            }));
            var banks = response.Institutions.Select(ToDto).ToList();
            return new BankPage(banks, banks.Count, 0, banks.Count);
        });
        return new BankPage(matches.Banks.Skip(skip).Take(take).ToList(), matches.Total, skip, take);
    }

    private async Task<BankPage> CachedAsync(string key, Func<Task<BankPage>> fetch)
    {
        if (_cache.TryGetValue(key, out Cached? hit) && hit is not null && _time.GetUtcNow() - hit.FetchedAt < FreshFor)
        {
            return hit.Page;
        }

        await FetchGate.WaitAsync();
        try
        {
            if (_cache.TryGetValue(key, out hit) && hit is not null && _time.GetUtcNow() - hit.FetchedAt < FreshFor)
            {
                return hit.Page; // another request fetched it while this one waited
            }

            try
            {
                var page = await fetch();
                _cache.Set(key, new Cached(page, _time.GetUtcNow()), KeepFor);
                return page;
            }
            catch (ApiRequestException) when (hit is not null)
            {
                // Plaid refused (rate limit, outage): the last answer is better than none.
                return hit.Page;
            }
        }
        finally
        {
            FetchGate.Release();
        }
    }

    private static BankDto ToDto(Institution i) => new(i.InstitutionId, i.Name, i.Logo, i.Url, i.Oauth);
}
