namespace Microsoft.eShopWeb.ApplicationCore.Models.Payments;

/// <summary>
/// Authorize (hold) an amount. Exactly one funding source is set:
/// raw card details for a one-off payment, or a vaulted card token.
/// </summary>
public class GatewayAuthorizeRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    /// <summary>Merchant-side reference, e.g. the eShop order id.</summary>
    public string? ReferenceId { get; set; }
    public string? InvoiceId { get; set; }

    public GatewayCardDetails? Card { get; set; }
    /// <summary>PayPal vault payment token id, when paying with a saved card.</summary>
    public string? VaultPaymentTokenId { get; set; }
}
