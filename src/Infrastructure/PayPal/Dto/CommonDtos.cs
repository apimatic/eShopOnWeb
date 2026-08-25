using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Dto;

/// <summary>PayPal's "money" schema. Note: value is a STRING per spec (e.g. "19.99"), not a JSON number.</summary>
public class AmountDto
{
    [JsonPropertyName("currency_code")] public string CurrencyCode { get; set; } = null!;
    [JsonPropertyName("value")] public string Value { get; set; } = null!;
}

public class PayPalErrorDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("debug_id")] public string? DebugId { get; set; }
    [JsonPropertyName("details")] public List<PayPalErrorDetailDto>? Details { get; set; }
}

public class PayPalErrorDetailDto
{
    [JsonPropertyName("field")] public string? Field { get; set; }
    [JsonPropertyName("issue")] public string? Issue { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
}

public class PayPalAccessTokenResponseDto
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = null!;
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}
