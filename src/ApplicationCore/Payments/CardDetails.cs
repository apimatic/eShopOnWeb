namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Raw card details supplied by a shopper for a one-off payment or to be vaulted. These values are
/// carried only transiently to the payment gateway; they are never persisted in the application's
/// database. <see cref="ToString"/> is deliberately redacted so the PAN/CVV can never leak into a log.
/// </summary>
public sealed class CardDetails
{
    public CardDetails(
        string number,
        string expiry,
        string securityCode,
        string? name,
        BillingAddress billingAddress)
    {
        Number = number;
        Expiry = expiry;
        SecurityCode = securityCode;
        Name = name;
        BillingAddress = billingAddress;
    }

    /// <summary>Primary account number (13-19 digits). Never logged, never stored.</summary>
    public string Number { get; }

    /// <summary>Expiry in PayPal's <c>YYYY-MM</c> form.</summary>
    public string Expiry { get; }

    /// <summary>Card verification value (3-4 digits). Never logged, never stored.</summary>
    public string SecurityCode { get; }

    /// <summary>Optional card holder name.</summary>
    public string? Name { get; }

    public BillingAddress BillingAddress { get; }

    // Redacted on purpose: card data must never appear in logs or diagnostics.
    public override string ToString() => "CardDetails { REDACTED }";
}
