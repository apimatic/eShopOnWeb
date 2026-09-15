using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

internal sealed class CreateCheckoutOrderRequest
{
    [JsonPropertyName("intent")]
    public string Intent { get; set; } = "AUTHORIZE";

    [JsonPropertyName("purchase_units")]
    public List<PurchaseUnitRequest> PurchaseUnits { get; set; } = new();

    [JsonPropertyName("payment_source")]
    public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PurchaseUnitRequest
{
    [JsonPropertyName("custom_id")]
    public string? CustomId { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("amount")]
    public AmountRequest? Amount { get; set; }

    [JsonPropertyName("items")]
    public List<ItemRequest>? Items { get; set; }
}

internal sealed class AmountRequest
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("breakdown")]
    public AmountBreakdownRequest? Breakdown { get; set; }
}

internal sealed class AmountBreakdownRequest
{
    [JsonPropertyName("item_total")]
    public MoneyRequest? ItemTotal { get; set; }
}

internal sealed class MoneyRequest
{
    [JsonPropertyName("currency_code")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }
}

internal sealed class ItemRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("quantity")]
    public string? Quantity { get; set; }

    [JsonPropertyName("sku")]
    public string? Sku { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("unit_amount")]
    public MoneyRequest? UnitAmount { get; set; }
}

internal sealed class AuthorizeOrderRequest
{
    [JsonPropertyName("payment_source")]
    public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class PaymentSourceRequest
{
    [JsonPropertyName("card")]
    public CardRequest? Card { get; set; }
}

internal sealed class CardRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("number")]
    public string? Number { get; set; }

    [JsonPropertyName("expiry")]
    public string? Expiry { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("vault_id")]
    public string? VaultId { get; set; }

    [JsonPropertyName("billing_address")]
    public BillingAddressRequest? BillingAddress { get; set; }

    [JsonPropertyName("attributes")]
    public CardAttributesRequest? Attributes { get; set; }

    [JsonPropertyName("stored_credential")]
    public StoredCredentialRequest? StoredCredential { get; set; }
}

internal sealed class BillingAddressRequest
{
    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }

    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; }

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; }

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }
}

internal sealed class CardAttributesRequest
{
    [JsonPropertyName("verification")]
    public CardVerificationRequest? Verification { get; set; }
}

internal sealed class CardVerificationRequest
{
    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

internal sealed class StoredCredentialRequest
{
    [JsonPropertyName("payment_initiator")]
    public string? PaymentInitiator { get; set; }

    [JsonPropertyName("payment_type")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("usage")]
    public string? Usage { get; set; }
}

internal sealed class CaptureRequest
{
    [JsonPropertyName("amount")]
    public MoneyRequest? Amount { get; set; }

    [JsonPropertyName("invoice_id")]
    public string? InvoiceId { get; set; }

    [JsonPropertyName("final_capture")]
    public bool FinalCapture { get; set; }
}

internal sealed class ReauthorizeRequest
{
    [JsonPropertyName("amount")]
    public MoneyRequest? Amount { get; set; }
}

internal sealed class RefundRequest
{
    [JsonPropertyName("amount")]
    public MoneyRequest? Amount { get; set; }
}

internal sealed class CreatePaymentTokenRequest
{
    [JsonPropertyName("customer")]
    public VaultCustomerRequest? Customer { get; set; }

    [JsonPropertyName("payment_source")]
    public PaymentSourceRequest? PaymentSource { get; set; }
}

internal sealed class VaultCustomerRequest
{
    [JsonPropertyName("merchant_customer_id")]
    public string? MerchantCustomerId { get; set; }
}
