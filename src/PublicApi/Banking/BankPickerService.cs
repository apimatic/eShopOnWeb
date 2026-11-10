using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankDto(string Id, string Name);

public record BankListResult(List<BankDto> Banks, int Total);

/// <summary>
/// The checkout's bank picker. Every checkout opens it, so answers are kept for a while and
/// concurrent identical requests share one Plaid call instead of each spending Plaid's rate limit.
/// </summary>
public class BankPickerService(ThePlaidApiClient plaid, IMemoryCache cache)
{
    public const int DefaultCount = 100;
    public const int MaxCount = 500; // Plaid's page limit
    private static readonly object Gate = new();
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    public Task<BankListResult> ListAsync(string? query, int? count, int? offset)
    {
        var take = count ?? DefaultCount;
        var skip = offset ?? 0;
        if (take < 1 || take > MaxCount)
        {
            throw new RequestFailedException(400, $"count must be between 1 and {MaxCount}.");
        }

        if (skip < 0)
        {
            throw new RequestFailedException(400, "offset cannot be negative.");
        }

        var term = query?.Trim();
        var key = string.IsNullOrEmpty(term)
            ? $"banks:list:{take}:{skip}"
            : $"banks:search:{term.ToLowerInvariant()}";

        // A Lazy so that the Plaid call starts once even when many requests arrive together.
        Lazy<Task<BankListResult>> lazy;
        lock (Gate)
        {
            lazy = cache.GetOrCreate(key, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CacheFor;
                return new Lazy<Task<BankListResult>>(() => string.IsNullOrEmpty(term) ? FetchListAsync(take, skip) : FetchSearchAsync(term));
            })!;
        }

        var task = lazy.Value;
        if (task.IsFaulted || task.IsCanceled)
        {
            cache.Remove(key);
        }
        else
        {
            task.ContinueWith(t => cache.Remove(key), TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        return task;
    }

    private async Task<BankListResult> FetchListAsync(int count, int offset)
    {
        var response = await PlaidError.Call(() => plaid.InstitutionsGet(new InstitutionsGetOperationRequest
        {
            Body = new InstitutionsGetRequest
            {
                Count = count,
                Offset = offset,
                CountryCodes = [CountryCode.Us],
                Options = new InstitutionsGetRequestOptions
                {
                    Products = [Products.Auth],
                },
            },
        }));

        return new BankListResult(response.Institutions.Select(ToDto).ToList(), response.Total);
    }

    private async Task<BankListResult> FetchSearchAsync(string query)
    {
        var response = await PlaidError.Call(() => plaid.InstitutionsSearch(new InstitutionsSearchOperationRequest
        {
            Body = new InstitutionsSearchRequest
            {
                Query = query,
                CountryCodes = [CountryCode.Us],
                Products = [Products.Auth],
            },
        }));

        var banks = response.Institutions.Select(ToDto).ToList();
        return new BankListResult(banks, banks.Count);
    }

    private static BankDto ToDto(Institution i) => new(i.InstitutionId, i.Name);
}
