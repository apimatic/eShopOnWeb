using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class FinixCreateIdentityRequest
{
    [JsonPropertyName("entity")]
    public Dictionary<string, object> Entity { get; set; } = new();

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixAddressRequest
{
    [JsonPropertyName("line1")]
    public string Line1 { get; set; } = string.Empty;

    [JsonPropertyName("line2")]
    public string? Line2 { get; set; }

    [JsonPropertyName("city")]
    public string City { get; set; } = string.Empty;

    [JsonPropertyName("region")]
    public string Region { get; set; } = string.Empty;

    [JsonPropertyName("postal_code")]
    public string PostalCode { get; set; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; set; } = string.Empty;
}

public sealed class FinixCreatePaymentInstrumentRequest
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "PAYMENT_CARD";

    [JsonPropertyName("identity")]
    public string Identity { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("address")]
    public FinixAddressRequest? Address { get; set; }
}

public sealed class FinixCreateAuthorizationRequest
{
    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("merchant")]
    public string Merchant { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")]
    public long CaptureAmount { get; set; }
}

public sealed class FinixVoidAuthorizationRequest
{
    [JsonPropertyName("void_me")]
    public bool VoidMe { get; set; } = true;
}

public sealed class FinixCreateRefundRequest
{
    [JsonPropertyName("refund_amount")]
    public long RefundAmount { get; set; }
}

public sealed class FinixUpdatePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}
