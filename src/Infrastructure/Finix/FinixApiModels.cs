using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

internal sealed class FinixIdentityRequest
{
    [JsonPropertyName("entity")]
    public Dictionary<string, object> Entity { get; set; } = new();
}

internal sealed class FinixIdentityResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

internal sealed class FinixAddressRequest
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

internal sealed class FinixPaymentInstrumentRequest
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
    public FinixAddressRequest Address { get; set; } = new();
}

internal sealed class FinixPaymentInstrumentResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int? ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int? ExpirationYear { get; set; }

    [JsonPropertyName("identity")]
    public string? Identity { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

internal sealed class FinixDisablePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = false;
}

internal sealed class FinixAuthorizationRequest
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
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}

internal sealed class FinixCaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")]
    public long CaptureAmount { get; set; }
}

internal sealed class FinixVoidAuthorizationRequest
{
    [JsonPropertyName("void_me")]
    public bool VoidMe { get; set; } = true;
}

internal sealed class FinixRefundRequest
{
    [JsonPropertyName("refund_amount")]
    public long RefundAmount { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string IdempotencyId { get; set; } = string.Empty;
}

internal sealed class FinixAuthorizationResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }
}

internal sealed class FinixTransferResponse
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

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset? UpdatedAt { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }
}

internal sealed class FinixFeeResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("fee_type")]
    public string? FeeType { get; set; }
}

internal sealed class FinixPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

internal sealed class FinixAuthorizationListResponse
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
}

internal sealed class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")]
    public List<FinixAuthorizationResponse> Authorizations { get; set; } = new();
}

internal sealed class FinixTransferListResponse
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
}

internal sealed class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")]
    public List<FinixTransferResponse> Transfers { get; set; } = new();
}

internal sealed class FinixFeeListResponse
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }
}

internal sealed class FinixFeeEmbedded
{
    [JsonPropertyName("fees")]
    public List<FinixFeeResponse> Fees { get; set; } = new();
}

internal sealed class FinixErrorResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

internal sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError> Errors { get; set; } = new();
}

internal sealed class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("logref")]
    public string? LogRef { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("authorization")]
    public string? Authorization { get; set; }
}
