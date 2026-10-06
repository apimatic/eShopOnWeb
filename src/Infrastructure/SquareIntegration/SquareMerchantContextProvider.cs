using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Merchants;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>The merchant the shop acts for, the location orders go to, and that location's currency.</summary>
public sealed record SquareMerchantContext(string MerchantId, string? BusinessName, string LocationId, Currency Currency);

public sealed class SquareMerchantContextProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly SquareClientHolder _clients;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SquareMerchantContextProvider> _logger;

    public SquareMerchantContextProvider(SquareClientHolder clients, IMemoryCache cache, ILogger<SquareMerchantContextProvider> logger)
    {
        _clients = clients;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>The merchant behind the current credentials ("me"), straight from Square.</summary>
    public async Task<Merchant> GetMerchantAsync(CancellationToken cancellationToken)
    {
        var response = await SquareCall.RunAsync("Merchants.RetrieveMerchant", ct => _clients.Client.Merchants.RetrieveMerchant(
            new RetrieveMerchantRequest { MerchantId = "me" }, cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);

        if (response.Merchant?.Id is not { Length: > 0 })
        {
            throw new SquareIntegrationException(SquareFailureKind.UnreadableResponse, "Square did not return the merchant for the shop's credentials.");
        }

        return response.Merchant;
    }

    public async Task<SquareMerchantContext> GetAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"square:merchant-context:{_clients.Generation}";
        if (_cache.TryGetValue(cacheKey, out SquareMerchantContext? cached) && cached is not null)
        {
            return cached;
        }

        var merchant = await GetMerchantAsync(cancellationToken).ConfigureAwait(false);
        var locations = await SquareCall.RunAsync("Locations.ListLocations",
            ct => _clients.Client.Locations.ListLocations(cancellationToken: ct), _logger, cancellationToken).ConfigureAwait(false);

        var active = (locations.Locations ?? [])
            .Where(l => l.Id is { Length: > 0 }
                        && l.Status is { } status && status == LocationStatus.Active
                        && (l.MerchantId is null || l.MerchantId == merchant.Id))
            .ToList();
        var location = active.FirstOrDefault(l => l.Id == merchant.MainLocationId) ?? active.FirstOrDefault();
        if (location is null)
        {
            throw new SquareIntegrationException(SquareFailureKind.Rejected,
                "The connected Square merchant has no active location to place orders at.");
        }

        var currency = location.Currency ?? merchant.Currency;
        if (currency is null)
        {
            throw new SquareIntegrationException(SquareFailureKind.UnreadableResponse,
                "Square did not report a currency for the merchant's location.");
        }

        var context = new SquareMerchantContext(merchant.Id!, merchant.BusinessName, location.Id!, currency);
        _cache.Set(cacheKey, context, CacheLifetime);
        _logger.LogInformation("Acting for Square merchant {MerchantId} at location {LocationId} ({Currency})",
            context.MerchantId, context.LocationId, currency.Value);
        return context;
    }
}
