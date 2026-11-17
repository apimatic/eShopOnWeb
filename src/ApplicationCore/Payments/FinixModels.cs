using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixCreateIdentityRequest
{
    [JsonPropertyName("entity")]
    public Dictionary<string, object> Entity { get; set; } = new();

    [JsonPropertyName("identity_roles")]
    public string[] IdentityRoles { get; set; } = new[] { "BUYER" };

    [JsonPropertyName("type")]
    public string Type { get; set; } = "PERSONAL";
}

public sealed class FinixAddressRequest
{
    [JsonPropertyName("line1")]
    public string Line1 { get; set; } = string.Empty;

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

    [JsonPropertyName("security_code")]
    public string? SecurityCode { get; set; }

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("address")]
    public FinixAddressRequest? Address { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixUpdatePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
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
    public string IdempotencyId { get; set; } = string.Empty;

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

    [JsonPropertyName("idempotency_id")]
    public string IdempotencyId { get; set; } = string.Empty;
}

public sealed class FinixIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public sealed class FinixPaymentInstrument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixAuthorization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixFee
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("linked_to")]
    public string? LinkedTo { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("fee_type")]
    public string? FeeType { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public sealed class FinixListResponse<T>
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public Dictionary<string, List<T>>? Embedded { get; set; }

    public IReadOnlyList<T> Items()
    {
        if (Embedded == null)
        {
            return Array.Empty<T>();
        }

        foreach (var value in Embedded.Values)
        {
            return value;
        }

        return Array.Empty<T>();
    }
}

public sealed class FinixErrorEnvelope
{
    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

public sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError>? Errors { get; set; }
}

public sealed class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("logref")]
    public string? LogRef { get; set; }
}

public sealed class FinixApiException : Exception
{
    public FinixApiException(int statusCode, string message, string? code = null, string? logRef = null)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        LogRef = logRef;
    }

    public int StatusCode { get; }
    public string? Code { get; }
    public string? LogRef { get; }
}
