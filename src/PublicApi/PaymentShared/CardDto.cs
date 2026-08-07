namespace Microsoft.eShopWeb.PublicApi.PaymentShared;

/// <summary>
/// Raw card details on an incoming request. Kept as a plain class (not a record) so its generated
/// <c>ToString</c> can never print the PAN/CVV into a log. These values are forwarded to PayPal and
/// never persisted in the application database.
/// </summary>
public class CardDto
{
    /// <summary>Primary account number, e.g. 4111111111111111.</summary>
    public string? Number { get; set; }

    /// <summary>Expiry. Accepts <c>YYYY-MM</c>, <c>MM/YY</c> or <c>MM/YYYY</c>.</summary>
    public string? Expiry { get; set; }

    /// <summary>Card verification value (3-4 digits).</summary>
    public string? SecurityCode { get; set; }

    /// <summary>Card holder name.</summary>
    public string? Name { get; set; }

    public BillingAddressDto? BillingAddress { get; set; }

    public override string ToString() => "CardDto { REDACTED }";
}
