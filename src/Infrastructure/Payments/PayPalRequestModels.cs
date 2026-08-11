using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

// Request bodies shaped exactly after the PayPal OpenAPI specs (api-specs/). Field names use
// [JsonPropertyName] so serialization matches the spec's snake_case contract precisely, and
// null members are dropped so only the fields we set are sent.

internal sealed class MoneyModel
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = default!;
    [JsonPropertyName("value")] public string Value { get; set; } = default!;
}

internal sealed class CardBillingAddressModel
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; }   // city
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; }   // state / province
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
}

internal sealed class CardModel
{
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("billing_address")] public CardBillingAddressModel? BillingAddress { get; set; }
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

internal sealed class PaymentSourceModel
{
    [JsonPropertyName("card")] public CardModel? Card { get; set; }
}

internal sealed class PurchaseUnitModel
{
    [JsonPropertyName("invoice_id")] public string? InvoiceId { get; set; }
    [JsonPropertyName("custom_id")] public string? CustomId { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("amount")] public MoneyModel Amount { get; set; } = default!;
}

internal sealed class CreateOrderModel
{
    [JsonPropertyName("intent")] public string Intent { get; set; } = "AUTHORIZE";
    [JsonPropertyName("purchase_units")] public PurchaseUnitModel[] PurchaseUnits { get; set; } = default!;
    [JsonPropertyName("payment_source")] public PaymentSourceModel? PaymentSource { get; set; }
}

internal sealed class AmountOnlyModel
{
    [JsonPropertyName("amount")] public MoneyModel Amount { get; set; } = default!;
}

internal sealed class VaultPaymentTokenRequestModel
{
    [JsonPropertyName("payment_source")] public PaymentSourceModel PaymentSource { get; set; } = default!;
}
