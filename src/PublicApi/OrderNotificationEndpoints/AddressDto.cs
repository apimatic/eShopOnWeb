using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>Optional shipping address for a placed order. Defaults are used when omitted.</summary>
public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "N/A" : Street,
        string.IsNullOrWhiteSpace(City) ? "N/A" : City,
        string.IsNullOrWhiteSpace(State) ? "N/A" : State,
        string.IsNullOrWhiteSpace(Country) ? "N/A" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "00000" : ZipCode);

    public static Address Default() => new("N/A", "N/A", "N/A", "N/A", "00000");
}
