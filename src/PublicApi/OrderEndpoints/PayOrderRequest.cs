namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record PayOrderRequest
{
    public int OrderId { get; init; }
    public string BuyerId { get; init; } = "";

    // One-off card fields
    public string? CardNumber { get; init; }
    public string? CardExpiry { get; init; }    // format: YYYY-MM
    public string? CardCvv { get; init; }
    public string? CardName { get; init; }
    public string? BillingStreet { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }

    // Saved card
    public int? SavedPaymentMethodId { get; init; }
}

public record PayOrderResponse
{
    public string AuthorizationId { get; init; } = "";
    public string Status { get; init; } = "";
}
