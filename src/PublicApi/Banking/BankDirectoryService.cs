using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.eShopWeb.PublicApi.Banking.Plaid;

namespace Microsoft.eShopWeb.PublicApi.Banking;

public record BankSummary(string InstitutionId, string Name, IReadOnlyList<string> Products, bool Oauth);

/// <param name="Complete">False while the full list is still being loaded; the banks listed are then only a part of it.</param>
public record BankListing(IReadOnlyList<BankSummary> Banks, bool Complete);

/// <summary>
/// The shop's bank picker: the banks Plaid supports, optionally searched by name.
/// <para>
/// Every checkout opens the picker, but Plaid allows only a handful of /institutions/get calls a minute and the
/// full list takes ~20 pages. So the list is loaded once in the background (paced, resuming after rate limits),
/// shared by every request, and refreshed in the background while the previous copy keeps being served.
/// </para>
/// </summary>
public class BankDirectoryService
{
    private static readonly string[] CountryCodes = { "US" };
    private const int PageSize = 500; // Plaid's maximum for /institutions/get
    private static readonly TimeSpan ListTtl = TimeSpan.FromHours(6);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RateLimitPause = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan FirstPageWait = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory _scopes;
    private readonly IMemoryCache _cache;
    private readonly ILogger<BankDirectoryService> _logger;
    private readonly TimeProvider _clock;

    private readonly object _gate = new();
    private Task? _loading;
    private IReadOnlyList<BankSummary> _complete = Array.Empty<BankSummary>();
    private bool _hasComplete;
    private DateTimeOffset _completedAt;
    private List<BankSummary> _building = new();
    private int _builtTotal = int.MaxValue;
    private DateTimeOffset _lastFailure = DateTimeOffset.MinValue;
    private Exception? _lastError;

    public BankDirectoryService(IServiceScopeFactory scopes, IMemoryCache cache, ILogger<BankDirectoryService> logger, TimeProvider clock)
    {
        _scopes = scopes;
        _cache = cache;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>Starts loading (or refreshing) the list in the background if that is due.</summary>
    public void EnsureLoading()
    {
        lock (_gate)
        {
            if (_loading is { IsCompleted: false })
            {
                return;
            }
            var now = _clock.GetUtcNow();
            var fresh = _hasComplete && now - _completedAt < ListTtl;
            if (fresh || now - _lastFailure < FailureCooldown)
            {
                return;
            }
            _loading = Task.Run(LoadAsync);
        }
    }

    public async Task<BankListing> GetAsync(string? query, CancellationToken ct)
    {
        query = string.IsNullOrWhiteSpace(query) ? null : query.Trim();
        if (query is { Length: > 100 })
        {
            throw BankApiException.BadRequest("query is too long.");
        }

        EnsureLoading();

        if (query is not null)
        {
            lock (_gate)
            {
                if (_hasComplete)
                {
                    // the full list is at hand: search it without another trip to Plaid
                    var matches = _complete.Where(b => b.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                    return new BankListing(matches, true);
                }
            }
            return new BankListing(await SearchAsync(query, ct), true);
        }

        // no query: wait (briefly) only if there is nothing at all to show yet
        var deadline = _clock.GetUtcNow() + FirstPageWait;
        while (true)
        {
            lock (_gate)
            {
                if (_hasComplete)
                {
                    return new BankListing(_complete, true);
                }
                if (_building.Count > 0)
                {
                    return new BankListing(_building.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList(), false);
                }
                if (_lastError is not null && (_loading is null || _loading.IsCompleted))
                {
                    throw _lastError is PlaidException p ? p : new PlaidException(null, "API_ERROR", "LOAD_FAILED", _lastError.Message);
                }
            }
            if (_clock.GetUtcNow() > deadline)
            {
                throw new BankApiException(System.Net.HttpStatusCode.ServiceUnavailable, "The bank list is still loading; please try again shortly.");
            }
            await Task.Delay(250, ct);
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var plaid = scope.ServiceProvider.GetRequiredService<PlaidClient>();

            lock (_gate)
            {
                // resume a partly built list after a failure; start over when refreshing a finished one
                if (_hasComplete && _building.Count >= _builtTotal)
                {
                    _building = new();
                    _builtTotal = int.MaxValue;
                }
            }

            while (true)
            {
                int offset;
                lock (_gate) { offset = _building.Count; if (offset >= _builtTotal) break; }

                PlaidInstitutionsResponse page;
                try
                {
                    page = await plaid.PostAsync<PlaidInstitutionsResponse>("institutions/get",
                        new { count = PageSize, offset, country_codes = CountryCodes }, default, rateLimitRetries: 0);
                }
                catch (PlaidException ex) when (ex.ErrorCode is "RATE_LIMIT_EXCEEDED" or "INSTITUTIONS_GET_LIMIT")
                {
                    await Task.Delay(RateLimitPause);
                    continue;
                }

                lock (_gate)
                {
                    _builtTotal = page.Total;
                    if (page.Institutions.Count == 0) break;
                    _building.AddRange(page.Institutions.Select(Map));
                }
            }

            lock (_gate)
            {
                _complete = _building.OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase).ToList();
                _hasComplete = true;
                _completedAt = _clock.GetUtcNow();
                _lastError = null;
            }
            _logger.LogInformation("Loaded {Count} banks from Plaid", _complete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loading the bank list failed; will retry");
            lock (_gate)
            {
                _lastError = ex;
                _lastFailure = _clock.GetUtcNow();
            }
        }
    }

    private async Task<IReadOnlyList<BankSummary>> SearchAsync(string query, CancellationToken ct)
    {
        var key = "banks:search:" + query.ToLowerInvariant();
        if (_cache.TryGetValue(key, out IReadOnlyList<BankSummary>? cached) && cached is not null)
        {
            return cached;
        }

        using var scope = _scopes.CreateScope();
        var plaid = scope.ServiceProvider.GetRequiredService<PlaidClient>();
        var result = await plaid.PostAsync<PlaidInstitutionsResponse>("institutions/search",
            new { query, country_codes = CountryCodes }, ct, rateLimitRetries: 4);
        var banks = result.Institutions.Select(Map).ToList();
        _cache.Set(key, (IReadOnlyList<BankSummary>)banks, SearchTtl);
        return banks;
    }

    private static BankSummary Map(PlaidInstitution i) =>
        new(i.InstitutionId, i.Name, (IReadOnlyList<string>)(i.Products ?? new List<string>()), i.Oauth);
}

/// <summary>Starts loading the bank list as soon as the app is up, so the first checkout does not wait for it.</summary>
public class BankDirectoryWarmup : IHostedService
{
    private readonly BankDirectoryService _directory;
    private readonly Microsoft.Extensions.Options.IOptions<PlaidOptions> _options;

    public BankDirectoryWarmup(BankDirectoryService directory, Microsoft.Extensions.Options.IOptions<PlaidOptions> options)
    {
        _directory = directory;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.Value.ClientId) && !string.IsNullOrWhiteSpace(_options.Value.Secret))
        {
            _directory.EnsureLoading();
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
