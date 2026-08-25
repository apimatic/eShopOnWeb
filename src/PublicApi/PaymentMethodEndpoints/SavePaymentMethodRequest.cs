namespace Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;

public record SavePaymentMethodRequest
{
    public string BuyerId { get; init; } = "";
    public string? CardNumber { get; init; }
    public string? CardExpiry { get; init; }    // format: YYYY-MM
    public string? CardCvv { get; init; }
    public string? CardName { get; init; }
    public string? BillingStreet { get; init; }
    public string? BillingCity { get; init; }
    public string? BillingState { get; init; }
    public string? BillingPostalCode { get; init; }
    public string? BillingCountryCode { get; init; }
}

public record SavePaymentMethodResponse
{
    public int PaymentMethodId { get; init; }
    public string? CardBrand { get; init; }
    public string? Last4 { get; init; }
    public string? CardExpiry { get; init; }
    public string? CardholderName { get; init; }
}
