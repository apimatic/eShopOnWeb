using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

/// <summary>Shared "card_request" shape used by Orders v2 (payment_source.card) and Vault v3 (payment_source.card).</summary>
public class CardRequestDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("billing_address")] public CardBillingAddressDto? BillingAddress { get; set; }

    /// <summary>The PayPal-generated vault id for a previously saved card. Mutually exclusive with number/expiry/security_code.</summary>
    [JsonPropertyName("vault_id")] public string? VaultId { get; set; }
}

public class CardBillingAddressDto
{
    [JsonPropertyName("address_line_1")] public string? AddressLine1 { get; set; }
    [JsonPropertyName("address_line_2")] public string? AddressLine2 { get; set; }
    [JsonPropertyName("admin_area_2")] public string? AdminArea2 { get; set; } // city
    [JsonPropertyName("admin_area_1")] public string? AdminArea1 { get; set; } // state/province
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country_code")] public string CountryCode { get; set; } = null!;
}

/// <summary>The "card_response" shape - safe fields only, never a full card number.</summary>
public class CardResponseDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("last_digits")] public string? LastDigits { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("expiry")] public string? Expiry { get; set; }
}
