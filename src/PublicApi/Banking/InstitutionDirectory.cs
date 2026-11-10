using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ThePlaidApi;
using ThePlaidApi.Models;
using ThePlaidApi.Models.Enums;
using ThePlaidApi.Requests;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankInfo(string Id, string Name, string? Url, bool Oauth, IReadOnlyList<string> Products);

/// <summary>
/// The shop's bank picker: every bank Plaid supports, searchable by name.
/// Every checkout opens the picker, and Plaid rate-limits its institution list (which is large and paged),
/// so the full list is loaded once (by one caller, paced to Plaid's limit), shared by every concurrent
/// caller and served from memory. Plaid's own name search only returns a handful of matches, so it is
/// used only to answer while the full list is still loading.
/// </summary>
public class InstitutionDirectory
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan LoadBudget = TimeSpan.FromMinutes(10);
    private const int PAGE_SIZE = 500;
    private const int MAX_SEARCH_CACHE = 500;

    private readonly ThePlaidApiClient _plaid;
    private readonly object _gate = new();
    private IReadOnlyList<BankInfo>? _banks;
    private DateTimeOffset _loadedAt;
    private Task? _loading;
    private readonly ConcurrentDictionary<string, (DateTimeOffset At, IReadOnlyList<BankInfo> Banks)> _searches = new();

    public InstitutionDirectory(ThePlaidApiClient plaid)
    {
        _plaid = plaid;
    }

    public async Task<IReadOnlyList<BankInfo>> SearchAsync(string? query, CancellationToken cancellationToken)
    {
        query = query?.Trim();
        var banks = _banks;
        if (banks is not null && DateTimeOffset.UtcNow - _loadedAt >= Ttl) EnsureLoading(); // refresh, keep serving the old list

        if (banks is null)
        {
            var loading = EnsureLoading();
            if (!string.IsNullOrEmpty(query))
            {
                // Quick answer from Plaid's own search while the full list is still loading.
                var quick = await TryPlaidSearchAsync(query, cancellationToken);
                if (quick is not null) return Filter(quick, query); // same name-matching as the full list
            }
            await loading.WaitAsync(cancellationToken);
            banks = _banks ?? throw new InvalidOperationException("The bank list failed to load.");
        }

        return Filter(banks, query);
    }

    private static IReadOnlyList<BankInfo> Filter(IReadOnlyList<BankInfo> banks, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return banks;

        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return banks
            .Where(b => terms.All(t => b.Name.Contains(t, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(b => b.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Starts (once) loading the full list; everyone shares the one load.</summary>
    private Task EnsureLoading()
    {
        lock (_gate)
        {
            if (_loading is { IsCompleted: false }) return _loading;

            _loading = Task.Run(async () =>
            {
                using var budget = new CancellationTokenSource(LoadBudget);
                var loaded = await LoadAsync(budget.Token);
                _banks = loaded;
                _loadedAt = DateTimeOffset.UtcNow;
            });
            return _loading;
        }
    }

    private async Task<IReadOnlyList<BankInfo>?> TryPlaidSearchAsync(string query, CancellationToken cancellationToken)
    {
        var key = query.ToLowerInvariant();
        if (_searches.TryGetValue(key, out var cached) && DateTimeOffset.UtcNow - cached.At < SearchTtl) return cached.Banks;

        try
        {
            var response = await PlaidCall.Run(() => _plaid.InstitutionsSearch(new InstitutionsSearchOperationRequest
            {
                Body = new InstitutionsSearchRequest { Query = query, CountryCodes = new[] { CountryCode.Us } },
            }, cancellationToken: cancellationToken));

            var banks = response.Institutions.Select(ToBank).ToList();
            if (_searches.Count >= MAX_SEARCH_CACHE) _searches.Clear();
            _searches[key] = (DateTimeOffset.UtcNow, banks);
            return banks;
        }
        catch (PlaidCallException)
        {
            return null; // fall back to waiting for the full list
        }
    }

    private async Task<IReadOnlyList<BankInfo>> LoadAsync(CancellationToken cancellationToken)
    {
        var all = new List<Institution>();
        var total = int.MaxValue;
        for (var offset = 0; offset < total; offset += PAGE_SIZE)
        {
            var page = await GetPageWithPacingAsync(offset, cancellationToken);
            total = page.Total;
            if (page.Institutions.Count == 0) break;
            all.AddRange(page.Institutions);
        }

        return all
            .GroupBy(i => i.InstitutionId)
            .Select(g => g.First())
            .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(ToBank)
            .ToList();
    }

    /// <summary>One page, waiting out Plaid's rate limit rather than failing.</summary>
    private async Task<InstitutionsGetResponse> GetPageWithPacingAsync(int offset, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return await PlaidCall.Run(() => _plaid.InstitutionsGet(new InstitutionsGetOperationRequest
                {
                    Body = new InstitutionsGetRequest
                    {
                        Count = PAGE_SIZE,
                        Offset = offset,
                        CountryCodes = new[] { CountryCode.Us },
                    },
                }, cancellationToken: cancellationToken));
            }
            catch (PlaidCallException ex) when (ex.StatusCode == 429)
            {
                await Task.Delay(RateLimitPause, cancellationToken);
            }
        }
    }

    private static BankInfo ToBank(Institution i) =>
        new(i.InstitutionId, i.Name, i.Url, i.Oauth, i.Products.Select(p => p.Value).ToList());
}
