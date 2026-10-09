using Microsoft.eShopWeb.SquareCheck.Configuration;
using Square;
using Square.Models;
using Square.Requests.Merchants;
using Square.Requests.OAuth;

namespace Microsoft.eShopWeb.SquareCheck.SquareAccess;

public sealed record MerchantSummary(string Id, string? BusinessName)
{
    /// <summary>What to call the business when Square has no business name on file.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(BusinessName) ? Id : BusinessName;
}

public sealed record LocationSummary(string? Id, string? Name, string Status, string? Address);

/// <summary>Exchanges this run's authorization code for an access token.</summary>
public sealed class SquareTokenExchange(SquareClient client, SquareConnectionSettings settings)
{
    public Task<string> ExchangeAsync(string authorizationCode, CancellationToken cancellationToken) =>
        SquareCall.RunAsync("complete the sign-in", async () =>
        {
            // POST: never resent by the SDK, which is right - the code is single-use.
            var response = await client.OAuth.ObtainToken(
                new ObtainTokenOperationRequest
                {
                    Body = new ObtainTokenRequest
                    {
                        ClientId = settings.ApplicationId,
                        ClientSecret = settings.ApplicationSecret,
                        GrantType = "authorization_code",
                        Code = authorizationCode,
                        RedirectUri = settings.RedirectUri,
                    },
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(response.AccessToken))
            {
                throw new SquareRequestException(
                    $"Square did not issue an access token when completing the sign-in{FirstError(response.Errors)}.");
            }

            return response.AccessToken;
        });

    internal static string FirstError(IReadOnlyList<Error>? errors) =>
        errors is { Count: > 0 } ? $": {errors[0].Code.Value}{(errors[0].Detail is { } detail ? $" - {detail}" : string.Empty)}" : string.Empty;
}

/// <summary>Reads what the signed-in token can see: the merchant and its locations.</summary>
public sealed class SquareAccountReader(SquareClient client)
{
    public Task<MerchantSummary> GetMerchantAsync(CancellationToken cancellationToken) =>
        SquareCall.RunAsync("read the merchant profile", async () =>
        {
            // "me" = the merchant this access token belongs to.
            var response = await client.Merchants.RetrieveMerchant(
                new RetrieveMerchantRequest { MerchantId = "me" },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Merchant is not { Id: { Length: > 0 } id } merchant)
            {
                throw new SquareRequestException(
                    $"Square did not return the merchant profile{SquareTokenExchange.FirstError(response.Errors)}.");
            }

            return new MerchantSummary(id, merchant.BusinessName);
        });

    public Task<IReadOnlyList<LocationSummary>> ListLocationsAsync(CancellationToken cancellationToken) =>
        SquareCall.RunAsync("list the locations", async () =>
        {
            // Not paginated: one response carries every location, active and inactive.
            var response = await client.Locations.ListLocations(cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Errors is { Count: > 0 })
            {
                throw new SquareRequestException(
                    $"Square reported an error listing the locations{SquareTokenExchange.FirstError(response.Errors)}.");
            }

            IReadOnlyList<LocationSummary> locations = (response.Locations ?? [])
                .Select(location => new LocationSummary(
                    location.Id,
                    location.Name,
                    location.Status?.Value ?? "UNKNOWN",
                    FormatAddress(location.Address)))
                .ToList();
            return locations;
        });

    internal static string? FormatAddress(Address? address)
    {
        if (address is null)
        {
            return null;
        }

        var regionLine = string.Join(" ", new[] { address.AdministrativeDistrictLevel1, address.PostalCode }
            .Where(part => !string.IsNullOrWhiteSpace(part)));
        var parts = new[]
            {
                address.AddressLine1, address.AddressLine2, address.AddressLine3,
                address.Locality, regionLine, address.Country?.Value,
            }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }
}
