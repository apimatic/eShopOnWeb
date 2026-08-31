using System.Collections.Generic;
using System.Text.Json.Serialization;

// DTOs for the Checkout Orders API v2 (api-specs/paypal/checkout_orders_v2).
namespace Microsoft.eShopWeb.Infrastructure.Payments.Dto;

/// <summary>order_request schema.</summary>
public class PayPalOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = string.Empty;

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PayPalPaymentSource? PaymentSource { get; set; }
}

/// <summary>purchase_unit_request schema (subset).</summary>
public class PayPalPurchaseUnitRequest
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney Amount { get; set; } = new();

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>payment_source schema (only the sources this integration uses).</summary>
public class PayPalPaymentSource
{
    [JsonPropertyName("card")]
    public PayPalCardRequest? Card { get; set; }

    [JsonPropertyName("token")]
    public PayPalTokenRequest? Token { get; set; }
}

/// <summary>card_request schema (subset).</summary>
public class PayPalCardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("expiry")]
    public string Expiry { get; set; } = string.Empty;

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public PayPalAddress? BillingAddress { get; set; }
}

/// <summary>token schema — a tokenized payment source (e.g. a vaulted card).</summary>
public class PayPalTokenRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    // The spec's enum lists only BILLING_AGREEMENT, but the field is a free-form string
    // per its pattern; PayPal's vault documentation defines PAYMENT_METHOD_TOKEN as the
    // type for vaulted payment method tokens.
    public const string PaymentMethodTokenType = "PAYMENT_METHOD_TOKEN";

    [JsonPropertyName("type")]
    public string Type { get; set; } = PaymentMethodTokenType;
}

/// <summary>order / order_authorize_response schema (subset, read side).</summary>
public class PayPalOrderResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PayPalPurchaseUnit>? PurchaseUnits { get; set; }
}

/// <summary>purchase_unit schema (read side, subset).</summary>
public class PayPalPurchaseUnit
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("amount")]
    public PayPalMoney? Amount { get; set; }

    [JsonPropertyName("payments")]
    public PayPalPaymentCollection? Payments { get; set; }
}

/// <summary>payment_collection schema.</summary>
public class PayPalPaymentCollection
{
    [JsonPropertyName("authorizations")]
    public List<PayPalAuthorization>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<PayPalCapture>? Captures { get; set; }

    [JsonPropertyName("refunds")]
    public List<PayPalRefund>? Refunds { get; set; }
}
