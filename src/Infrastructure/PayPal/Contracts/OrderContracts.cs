using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// --- POST /v2/checkout/orders request (order_request) ---

public class CreateOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PaymentSourceRequest? PaymentSource { get; set; }
}

public class PurchaseUnitRequest
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("amount")]
    public Money Amount { get; set; } = new();

    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }
}

public class PaymentSourceRequest
{
    [JsonPropertyName("card")]
    public CardRequest? Card { get; set; }
}

public class CardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; } // YYYY-MM

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("billing_address")]
    public AddressPortable? BillingAddress { get; set; }

    /// <summary>Reference a saved card instead of raw details.</summary>
    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("stored_credential")]
    public CardStoredCredential? StoredCredential { get; set; }
}

public class CardStoredCredential
{
    [JsonPropertyName("payment_initiator")]
    public string? PaymentInitiator { get; set; } // CUSTOMER / MERCHANT

    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; set; } // ONE_TIME / RECURRING / UNSCHEDULED

    [JsonPropertyName("usage")]
    public string? Usage { get; set; } // FIRST / SUBSEQUENT / DERIVED
}

// --- Order response (order) ---

public class OrderResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitResponse>? PurchaseUnits { get; set; }

    [JsonPropertyName("links")]
    public List<LinkDescription>? Links { get; set; }
}

public class PurchaseUnitResponse
{
    [JsonPropertyName("reference_id")]
    public string? ReferenceId { get; set; }

    [JsonPropertyName("payments")]
    public PaymentCollection? Payments { get; set; }
}

public class PaymentCollection
{
    [JsonPropertyName("authorizations")]
    public List<AuthorizationResponse>? Authorizations { get; set; }

    [JsonPropertyName("captures")]
    public List<CaptureResponse>? Captures { get; set; }
}
