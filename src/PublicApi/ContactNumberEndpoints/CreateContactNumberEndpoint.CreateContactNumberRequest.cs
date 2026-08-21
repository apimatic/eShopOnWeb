namespace Microsoft.eShopWeb.PublicApi.ContactNumberEndpoints;

public record CreateContactNumberRequest
{
    public string PhoneNumber { get; init; } = string.Empty;
    public string? CountryCode { get; init; }
    public string? BuyerId { get; init; }
}
