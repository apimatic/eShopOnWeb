using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Square.Core.Exceptions;
using Square.Models;
using Square.Models.Enums;
using Square.Requests.Merchants;

namespace Microsoft.eShopWeb.Infrastructure.SquareIntegration;

/// <summary>The merchant the shop currently acts for, and the location its orders go to.</summary>
public sealed record SquareMerchantContext(
    string MerchantId,
    string? BusinessName,
    string LocationId,
    string? LocationName,
    Currency Currency,
    Address? LocationAddress,
    SquareCredentialSource Source);

/// <summary>
/// Resolves (and caches per connection) the merchant id, business name and the active location orders are
/// created at. The location always comes from the merchant's own location list, preferring its main location.
/// </summary>
public sealed class SquareMerchantContextProvider
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly SquareClientProvider _clients;
    private readonly SquareAccessTokenProvider _tokens;
    private readonly TimeProvider _clock;
    private volatile Cached? _cached;

    public SquareMerchantContextProvider(SquareClientProvider clients, SquareAccessTokenProvider tokens, TimeProvider clock)
    {
        _clients = clients;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<SquareMerchantContext> GetAsync(CancellationToken cancellationToken, bool refresh = false)
    {
        var credential = await _tokens.GetAsync(cancellationToken).ConfigureAwait(false);
        var cached = _cached;
        if (!refresh && cached is not null && cached.Generation == credential.Generation
            && _clock.GetUtcNow() - cached.LoadedAt < CacheLifetime)
        {
            return cached.Context;
        }

        var client = _clients.Merchant;
        Merchant merchant;
        Location[] activeLocations;
        try
        {
            var merchantResponse = await client.Merchants.RetrieveMerchant(
                new RetrieveMerchantRequest { MerchantId = "me" }, cancellationToken: cancellationToken).ConfigureAwait(false);
            merchant = merchantResponse.Merchant
                ?? throw new SquareIntegrationException(SquareFailureKind.Unavailable, "Square returned no merchant profile.");

            var locationsResponse = await client.Locations.ListLocations(cancellationToken: cancellationToken).ConfigureAwait(false);
            activeLocations = (locationsResponse.Locations ?? [])
                .Where(l => l.Id is not null && l.Status == LocationStatus.Active)
                .ToArray();
        }
        catch (SdkException ex)
        {
            throw SquareErrors.Translate(ex, "reading the Square merchant profile");
        }

        if (string.IsNullOrEmpty(merchant.Id))
            throw new SquareIntegrationException(SquareFailureKind.Unavailable, "Square returned a merchant profile without an id.");

        var location = activeLocations.FirstOrDefault(l => l.Id == merchant.MainLocationId) ?? activeLocations.FirstOrDefault()
            ?? throw new SquareIntegrationException(SquareFailureKind.Rejected,
                "The Square merchant has no active location to create orders at.");

        var currency = location.Currency ?? merchant.Currency
            ?? throw new SquareIntegrationException(SquareFailureKind.Rejected, "The Square location has no currency.");

        var context = new SquareMerchantContext(merchant.Id, merchant.BusinessName, location.Id!, location.Name, currency,
            location.Address, credential.Source);
        _cached = new Cached(context, credential.Generation, _clock.GetUtcNow());
        return context;
    }

    private sealed record Cached(SquareMerchantContext Context, long Generation, DateTimeOffset LoadedAt);
}
