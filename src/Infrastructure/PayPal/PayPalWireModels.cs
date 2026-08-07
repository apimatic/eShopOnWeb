using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

// Minimal C# projections of the PayPal OpenAPI schemas actually used by this integration.
// Property names are PascalCase and serialized/deserialized as snake_case via a JsonNamingPolicy,
// matching the wire contract defined by the specs under api-specs/paypal/**.
// Null properties are omitted on serialization so requests only carry the fields we set.

// ---- OAuth2 (client credentials) ----------------------------------------------------------

internal sealed class OAuthTokenResponse
{
    public string? AccessToken { get; set; }
    public string? TokenType { get; set; }
    public int ExpiresIn { get; set; }
}

// ---- Checkout Orders v2 : requests --------------------------------------------------------

internal sealed class CreateOrderRequest
{
    public string Intent { get; set; } = "CAPTURE";
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();
    public PaymentSource? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    public MoneyModel Amount { get; set; } = new();
    public string? CustomId { get; set; }
    public string? Description { get; set; }
}

internal sealed class MoneyModel
{
    public string CurrencyCode { get; set; } = "USD";
    public string Value { get; set; } = "0.00";
}

internal sealed class PaymentSource
{
    public CardRequest? Card { get; set; }
}

internal sealed class CardRequest
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public CardBillingAddressModel? BillingAddress { get; set; }

    /// <summary>Set to pay with a previously vaulted card instead of raw card details.</summary>
    public string? VaultId { get; set; }
}

internal sealed class CardBillingAddressModel
{
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? AdminArea2 { get; set; }
    public string? AdminArea1 { get; set; }
    public string? PostalCode { get; set; }
    public string? CountryCode { get; set; }
}

// ---- Checkout Orders v2 : responses -------------------------------------------------------

internal sealed class OrderResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }
    public PaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitResponse
{
    public PaymentCollection? Payments { get; set; }
}

internal sealed class PaymentCollection
{
    public List<CaptureResponse>? Captures { get; set; }
}

internal sealed class CaptureResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyModel? Amount { get; set; }
}

internal sealed class PaymentSourceResponse
{
    public CardResponse? Card { get; set; }
}

internal sealed class CardResponse
{
    public string? Name { get; set; }
    public string? LastDigits { get; set; }
    public string? Brand { get; set; }
    public string? Expiry { get; set; }
}

// ---- Payments v2 : refund -----------------------------------------------------------------

internal sealed class RefundResponse
{
    public string? Id { get; set; }
    public string? Status { get; set; }
    public MoneyModel? Amount { get; set; }
}

// ---- Vault Payment Tokens v3 --------------------------------------------------------------

internal sealed class PaymentTokenRequest
{
    public CustomerModel? Customer { get; set; }
    public VaultPaymentSource PaymentSource { get; set; } = new();
}

internal sealed class CustomerModel
{
    public string? Id { get; set; }
    public string? MerchantCustomerId { get; set; }
}

internal sealed class VaultPaymentSource
{
    public VaultCard? Card { get; set; }
}

internal sealed class VaultCard
{
    public string? Name { get; set; }
    public string? Number { get; set; }
    public string? Expiry { get; set; }
    public string? SecurityCode { get; set; }
    public CardBillingAddressModel? BillingAddress { get; set; }
}

internal sealed class PaymentTokenResponse
{
    public string? Id { get; set; }
    public CustomerModel? Customer { get; set; }
    public VaultPaymentSourceResponse? PaymentSource { get; set; }
}

internal sealed class VaultPaymentSourceResponse
{
    public CardResponse? Card { get; set; }
}

// ---- Error model --------------------------------------------------------------------------

internal sealed class PayPalErrorResponse
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<PayPalErrorDetail>? Details { get; set; }
}

internal sealed class PayPalErrorDetail
{
    public string? Issue { get; set; }
    public string? Description { get; set; }
    public string? Field { get; set; }
}
