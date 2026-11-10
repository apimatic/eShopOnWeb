using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi;
using ThePlaidApi.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

public record BankDto(string Id, string Name, string? Url, string? Logo, string? PrimaryColor, bool Oauth);

public record BankListDto(IReadOnlyList<BankDto> Banks, int Total);

/// <summary>
/// The bank picker's data. Every checkout opens the picker, so answers are cached and concurrent identical
/// lookups share one call to Plaid instead of each making their own.
/// </summary>
public class InstitutionService
{
    private const int MaxCount = 500; // Plaid's page size limit for /institutions/get
    private static readonly TimeSpan ListTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan NameTtl = TimeSpan.FromHours(6);

    private readonly ThePlaidApiClient _plaid;
    private readonly IMemoryCache _cache;
    private readonly ConcurrentDictionary<string, Lazy<Task<BankListDto>>> _inFlight = new();

    public InstitutionService(ThePlaidApiClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<BankListDto> ListAsync(string? query, int? count, int? offset)
    {
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (query is { Length: > 200 })
            throw ApiProblemException.BadRequest("query is too long.");
        var pageSize = count ?? 100;
        var skip = offset ?? 0;
        if (pageSize < 1 || pageSize > MaxCount)
            throw ApiProblemException.BadRequest($"count must be between 1 and {MaxCount}.");
        if (skip < 0)
            throw ApiProblemException.BadRequest("offset must not be negative.");

        var key = query == null
            ? $"banks:list:{pageSize}:{skip}"
            : $"banks:search:{query.ToLowerInvariant()}:{pageSize}:{skip}";

        if (_cache.TryGetValue(key, out BankListDto? cached) && cached != null)
            return cached;

        var lazy = _inFlight.GetOrAdd(key, _ => new Lazy<Task<BankListDto>>(() => LoadAsync(key, query, pageSize, skip)));
        try
        {
            return await lazy.Value.ConfigureAwait(false);
        }
        finally
        {
            _inFlight.TryRemove(key, out _);
        }
    }

    private async Task<BankListDto> LoadAsync(string key, string? query, int count, int offset)
    {
        BankListDto result;
        if (query == null)
        {
            var response = await PlaidCall.RunAsync(() => _plaid.InstitutionsGet(new()
            {
                Body = new() { Count = count, Offset = offset, CountryCodes = [CountryCode.Us] }
            })).ConfigureAwait(false);
            result = new BankListDto(response.Institutions.Select(ToDto).ToList(), response.Total);
        }
        else
        {
            // Plaid's search has no paging: it returns the best matches, so page over them here.
            var response = await PlaidCall.RunAsync(() => _plaid.InstitutionsSearch(new()
            {
                Body = new() { Query = query, CountryCodes = [CountryCode.Us] }
            })).ConfigureAwait(false);
            var all = response.Institutions;
            result = new BankListDto(all.Skip(offset).Take(count).Select(ToDto).ToList(), all.Count);
        }

        _cache.Set(key, result, query == null ? ListTtl : SearchTtl);
        return result;
    }

    /// <summary>The display name of an institution, or null when Plaid doesn't know it.</summary>
    public async Task<string?> GetNameAsync(string institutionId)
    {
        var key = "banks:name:" + institutionId;
        if (_cache.TryGetValue(key, out string? name))
            return name;
        try
        {
            var response = await PlaidCall.RunAsync(() => _plaid.InstitutionsGetById(new()
            {
                Body = new() { InstitutionId = institutionId, CountryCodes = [CountryCode.Us] }
            })).ConfigureAwait(false);
            name = response.Institution.Name;
            _cache.Set(key, name, NameTtl);
            return name;
        }
        catch (ApiProblemException)
        {
            return null; // the name is decoration; never fail a connection over it
        }
    }

    private static BankDto ToDto(ThePlaidApi.Models.Institution i) =>
        new(i.InstitutionId, i.Name, i.Url, i.Logo, i.PrimaryColor, i.Oauth);
}
