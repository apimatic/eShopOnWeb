using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.eShopWeb.PublicApi.Plaid;

/// <summary>The checkout's bank picker: the banks Plaid supports, optionally searched by name.</summary>
public class BankPickerService
{
    public static readonly string[] CountryCodes = { "US" };
    private const int RateLimitRetries = 12;
    private const int PageSize = 500; // Plaid's maximum for /institutions/get
    private static readonly SemaphoreSlim ListLock = new(1, 1);
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(12);
    public static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);

    private readonly PlaidClient _plaid;
    private readonly IMemoryCache _cache;

    public BankPickerService(PlaidClient plaid, IMemoryCache cache)
    {
        _plaid = plaid;
        _cache = cache;
    }

    public async Task<IResult> ListAsync(string? query, CancellationToken ct)
    {
        try
        {
            query = query?.Trim();
            List<BankDto> banks = string.IsNullOrEmpty(query)
                ? await GetAllAsync(ct)
                : await SearchAsync(query, ct);
            return Results.Ok(new { total = banks.Count, banks });
        }
        catch (PlaidException ex)
        {
            return ApiResults.FromPlaid(ex);
        }
    }

    /// <summary>Walks Plaid again and replaces the cached list.</summary>
    public Task RefreshAsync(CancellationToken ct) => GetAllAsync(ct, force: true);

    private async Task<List<BankDto>> GetAllAsync(CancellationToken ct, bool force = false)
    {
        const string key = "plaid:institutions:all";
        if (!force && _cache.TryGetValue(key, out List<BankDto>? cached) && cached is not null)
            return cached;

        // Single flight: the first checkout pays for the 500-per-page walk, the rest wait for it.
        await ListLock.WaitAsync(ct);
        try
        {
            if (!force && _cache.TryGetValue(key, out cached) && cached is not null)
                return cached;

            var all = new Dictionary<string, BankDto>();
            var offset = 0;
            var total = int.MaxValue;
            while (offset < total)
            {
                var page = await GetPageAsync(offset, ct);
                var items = page.Arr("institutions").Select(ToDto).Where(b => b is not null).Select(b => b!).ToList();
                total = page.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : offset + items.Count;
                foreach (var b in items) all[b.Id] = b;
                if (items.Count == 0) break;
                offset += items.Count;
            }

            var list = all.Values.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
            _cache.Set(key, list, ListTtl);
            return list;
        }
        finally
        {
            ListLock.Release();
        }
    }

    /// <summary>One page of the walk; Plaid rate-limits this endpoint, so back off and try again when told to.</summary>
    private async Task<JsonElement> GetPageAsync(int offset, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _plaid.InstitutionsGetAsync(PageSize, offset, CountryCodes, ct);
            }
            catch (PlaidException ex) when ((ex.HttpStatus == 429 || ex.ErrorType == "RATE_LIMIT_EXCEEDED") && attempt < RateLimitRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(5 * (attempt + 1), 20)), ct);
            }
        }
    }

    private async Task<List<BankDto>> SearchAsync(string query, CancellationToken ct)
    {
        var key = "plaid:institutions:search:" + query.ToLowerInvariant();
        if (_cache.TryGetValue(key, out List<BankDto>? cached) && cached is not null)
            return cached;

        var result = await _plaid.InstitutionsSearchAsync(query, CountryCodes, ct);
        var list = result.Arr("institutions").Select(ToDto).Where(b => b is not null).Select(b => b!).ToList();
        _cache.Set(key, list, SearchTtl);
        return list;
    }

    private static BankDto? ToDto(JsonElement e)
    {
        var id = e.Str("institution_id");
        var name = e.Str("name");
        if (id is null || name is null) return null;
        return new BankDto(id, name, e.Bool("oauth"));
    }

    public record BankDto(string Id, string Name, bool Oauth);
}
