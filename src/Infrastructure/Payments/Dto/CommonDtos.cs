using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments.Dto;

/// <summary>money schema (shared by the Orders, Payments, Vault and Reporting APIs).</summary>
public class PayPalMoney
{
    [JsonPropertyName("currency_code")]
    public string CurrencyCode { get; set; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; set; } = string.Empty;
}

/// <summary>Portable postal address used by card_request / vault card schemas.</summary>
public class PayPalAddress
{
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

    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = string.Empty;
}

/// <summary>error schema returned by every PayPal API on failure.</summary>
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

    [JsonPropertyName("issue")]
    public string? Issue { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>Response of POST /v1/oauth2/token (client credentials grant).</summary>
public class PayPalOAuthTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
