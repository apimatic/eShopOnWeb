using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankInfo(string Id, string Name, string? Url, string? Logo, string? PrimaryColor, bool Oauth);

public record BankPage(IReadOnlyList<BankInfo> Banks, int Total);

/// <summary>
/// The bank picker. Every checkout opens it at once and Plaid rate-limits the institution endpoints hard
/// (listing every institution takes dozens of calls), so the picker works a page at a time: pages and
/// searches are cached, concurrent callers share a single Plaid call, the last good answer is kept to
/// serve while Plaid pushes back, and a rate-limited Plaid is left alone for a while instead of hammered.
/// </summary>
public class InstitutionService
{
    public const int MaxPageSize = 500;
    public const int DefaultPageSize = 100;

    private static readonly TimeSpan PageLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan StaleLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);
    private const string CooldownKey = "banks:cooldown";

    private readonly ThePlaidApiClient _plaid;
    private readonly IMemoryCache _cache;
    private readonly KeyedLock _locks;

    public InstitutionService(ThePlaidApiClient plaid, IMemoryCache cache, KeyedLock locks)
    {
        _plaid = plaid;
        _cache = cache;
        _locks = locks;
    }

    public async Task<BankPage> ListAsync(string? query, int? count, int? offset, CancellationToken cancellationToken)
    {
        var size = count ?? DefaultPageSize;
        var skip = offset ?? 0;
        if (size < 1 || size > MaxPageSize)
            throw BankingException.BadRequest($"count must be between 1 and {MaxPageSize}.");
        if (skip < 0)
            throw BankingException.BadRequest("offset cannot be negative.");

        query = query?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return await Cached($"banks:page:{size}:{skip}", PageLifetime, ct => FetchPage(size, skip, ct), cancellationToken);
        }

        // Plaid searches by name; one search answers every page of it.
        var found = await Cached("banks:search:" + query.ToLowerInvariant(), SearchLifetime, ct => Search(query, ct), cancellationToken);
        return new BankPage(found.Banks.Skip(skip).Take(size).ToList(), found.Banks.Count);
    }

    private async Task<BankPage> Cached(string key, TimeSpan lifetime, Func<CancellationToken, Task<BankPage>> fetch,
        CancellationToken cancellationToken)
    {
        if (_cache.TryGetValue(key, out BankPage? hit) && hit is not null)
            return hit;

        using (await _locks.AcquireAsync("institutions:" + key, cancellationToken))
        {
            if (_cache.TryGetValue(key, out hit) && hit is not null)
                return hit;

            _cache.TryGetValue(key + ":stale", out BankPage? stale);
            if (_cache.TryGetValue(CooldownKey, out _))
            {
                // Plaid told us to slow down a moment ago.
                if (stale is not null)
                    return stale;
                throw new BankingException((HttpStatusCode)429, "The bank list is busy; try again in a moment.");
            }

            try
            {
                var fresh = await fetch(cancellationToken);
                _cache.Set(key, fresh, lifetime);
                _cache.Set(key + ":stale", fresh, StaleLifetime);
                return fresh;
            }
            catch (BankingException ex)
            {
                if ((int)ex.StatusCode == 429)
                    _cache.Set(CooldownKey, true, Cooldown);
                if (stale is not null)
                    return stale;
                throw;
            }
        }
    }

    private async Task<BankPage> FetchPage(int count, int offset, CancellationToken cancellationToken)
    {
        var page = await PlaidCalls.Run(() => _plaid.InstitutionsGet(new InstitutionsGetOperationRequest
        {
            Body = new InstitutionsGetRequest
            {
                Count = count,
                Offset = offset,
                CountryCodes = [CountryCode.Us],
            }
        }, cancellationToken: cancellationToken));

        return new BankPage(page.Institutions.Select(ToInfo).ToList(), page.Total);
    }

    private async Task<BankPage> Search(string query, CancellationToken cancellationToken)
    {
        var result = await PlaidCalls.Run(() => _plaid.InstitutionsSearch(new InstitutionsSearchOperationRequest
        {
            Body = new InstitutionsSearchRequest
            {
                Query = query,
                CountryCodes = [CountryCode.Us],
            }
        }, cancellationToken: cancellationToken));

        var banks = result.Institutions.Select(ToInfo).ToList();
        return new BankPage(banks, banks.Count);
    }

    private static BankInfo ToInfo(Institution i) => new(i.InstitutionId, i.Name, i.Url, i.Logo, i.PrimaryColor, i.Oauth);
}
