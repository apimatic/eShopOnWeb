using Square;
using Square.Requests.Merchants;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

public sealed record MerchantSummary(string? Id, string? BusinessName);

public sealed record LocationSummary(string? Id, string? Name, string? Status, string? Address);

/// <summary>
/// Square answered successfully but without the data the tool needs.
/// </summary>
public sealed class UnexpectedSquareResponseException(string message) : Exception(message);

/// <summary>
/// Reads what the signed-in merchant's account is: the merchant and its locations.
/// </summary>
public sealed class SquareAccountReader(SquareClient client)
{
    /// <summary>
    /// The merchant the access token belongs to. Being the first authenticated call, this is the call
    /// that makes the SDK run the sign-in.
    /// </summary>
    public async Task<MerchantSummary> GetMerchantAsync(CancellationToken cancellationToken)
    {
        var response = await client.Merchants.RetrieveMerchant(
            new RetrieveMerchantRequest { MerchantId = "me" },
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var merchant = response.Merchant
            ?? throw new UnexpectedSquareResponseException("Square returned no merchant for the signed-in account.");
        return new MerchantSummary(merchant.Id, merchant.BusinessName);
    }

    public async Task<IReadOnlyList<LocationSummary>> GetLocationsAsync(CancellationToken cancellationToken)
    {
        var response = await client.Locations.ListLocations(cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Locations is null && response.Errors is { Count: > 0 } errors)
        {
            var first = errors[0];
            throw new UnexpectedSquareResponseException(
                $"Square returned errors instead of locations: {first.Code.Value}{(string.IsNullOrWhiteSpace(first.Detail) ? "" : $" – {first.Detail}")}");
        }

        return (response.Locations ?? [])
            .Select(location => new LocationSummary(
                location.Id,
                location.Name,
                location.Status?.Value,
                AddressFormatter.Format(location.Address)))
            .ToList();
    }
}
