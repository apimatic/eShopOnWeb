using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// Wire DTOs that mirror the PayPal OpenAPI schemas. Property names are the exact JSON field names
// from the specs. Amounts are strings, as the specs define. Only non-null fields are serialized.

public class Money
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>The PayPal "Address" schema (Orders/Vault). country_code is the only required field.</summary>
public class AddressPortable
{
    [JsonPropertyName("address_line_1")]
    public string? AddressLine1 { get; set; }

    [JsonPropertyName("address_line_2")]
    public string? AddressLine2 { get; set; }

    [JsonPropertyName("admin_area_2")]
    public string? AdminArea2 { get; set; } // city

    [JsonPropertyName("admin_area_1")]
    public string? AdminArea1 { get; set; } // state / province

    [JsonPropertyName("postal_code")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; set; }
}

public class LinkDescription
{
    [JsonPropertyName("href")]
    public string? Href { get; set; }

    [JsonPropertyName("rel")]
    public string? Rel { get; set; }

    [JsonPropertyName("method")]
    public string? Method { get; set; }
}

public class PayPalErrorResponse
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("debug_id")]
    public string? DebugId { get; set; }

    [JsonPropertyName("details")]
    public List<PayPalErrorDetail>? Details { get; set; }
}

public class PayPalErrorDetail
{
    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
