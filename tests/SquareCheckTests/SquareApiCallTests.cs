using System.Net;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;
using Square.Requests.Merchants;

namespace SquareCheckTests;

public class SquareApiCallTests
{
    [Fact]
    public async Task ListMerchants_deserializes_business_name_and_id()
    {
        var (client, _) = StubHandler.ClientReturning(HttpStatusCode.OK, """
            {
              "merchant": [{
                "id": "MERCH123",
                "business_name": "Test Bakery",
                "country": "US"
              }]
            }
            """);

        var resp = await client.Merchants.ListMerchants(
            new ListMerchantsRequest(), cancellationToken: default);

        Assert.NotNull(resp.Merchant);
        Assert.Single(resp.Merchant);
        Assert.Equal("MERCH123", resp.Merchant[0].Id);
        Assert.Equal("Test Bakery", resp.Merchant[0].BusinessName);
    }

    [Fact]
    public async Task ListLocations_deserializes_location_name_and_address()
    {
        var (client, _) = StubHandler.ClientReturning(HttpStatusCode.OK, """
            {
              "locations": [{
                "id": "LOC456",
                "name": "Main Street",
                "status": "ACTIVE",
                "address": {
                  "address_line_1": "123 Main St",
                  "locality": "Springfield",
                  "administrative_district_level_1": "IL",
                  "postal_code": "62701"
                }
              }]
            }
            """);

        var resp = await client.Locations.ListLocations(cancellationToken: default);

        Assert.NotNull(resp.Locations);
        Assert.Single(resp.Locations);
        Assert.Equal("LOC456", resp.Locations[0].Id);
        Assert.Equal("Main Street", resp.Locations[0].Name);
        Assert.Equal("123 Main St", resp.Locations[0].Address?.AddressLine1);
        Assert.Equal("Springfield", resp.Locations[0].Address?.Locality);
    }

    [Fact]
    public async Task ListMerchants_throws_ApiException_on_error_status()
    {
        var (client, _) = StubHandler.ClientReturning(
            HttpStatusCode.Unauthorized, """{"errors":[{"code":"UNAUTHORIZED"}]}""");

        var ex = await Assert.ThrowsAsync<ApiException<RawError>>(
            () => client.Merchants.ListMerchants(
                new ListMerchantsRequest(), cancellationToken: default));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ListLocations_throws_ApiException_on_error_status()
    {
        var (client, _) = StubHandler.ClientReturning(
            HttpStatusCode.Forbidden, """{"errors":[{"code":"FORBIDDEN"}]}""");

        var ex = await Assert.ThrowsAsync<ApiException<RawError>>(
            () => client.Locations.ListLocations(cancellationToken: default));

        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }
}
