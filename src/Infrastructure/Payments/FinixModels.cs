using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

internal sealed class FinixIdentity
{
    public string Id { get; set; } = string.Empty;
}

internal sealed class FinixPaymentInstrument
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? InstrumentType { get; set; }
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? CardType { get; set; }
    public string? Fingerprint { get; set; }
    public bool? Enabled { get; set; }
    public string? Name { get; set; }
}

internal sealed class FinixAuthorization
{
    public string Id { get; set; } = string.Empty;
    public long? Amount { get; set; }
    public long? AmountRequested { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Source { get; set; }
    public string? Merchant { get; set; }
    public string? Transfer { get; set; }
    public string? TraceId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? IdempotencyId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public bool? IsVoid { get; set; }
    public string? VoidState { get; set; }
}

internal sealed class FinixTransfer
{
    public string Id { get; set; } = string.Empty;
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public long? Amount { get; set; }
    public long? AmountRequested { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? Subtype { get; set; }
    public string? ParentTransfer { get; set; }
    public string? ParentTransferTraceId { get; set; }
    public string? Source { get; set; }
    public string? Destination { get; set; }
    public string? Merchant { get; set; }
    public string? TraceId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? IdempotencyId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, JsonElement>? Tags { get; set; }
}

internal sealed class FinixFee
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? LinkedTo { get; set; }
    public string? LinkedType { get; set; }
    public string? FeeType { get; set; }
    public string? Category { get; set; }
}

internal sealed class FinixPage
{
    public int? Limit { get; set; }
    public string? NextCursor { get; set; }
}

internal sealed class FinixTransferList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
}

internal sealed class FinixTransferEmbedded
{
    public List<FinixTransfer>? Transfers { get; set; }
}

internal sealed class FinixFeeList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }
}

internal sealed class FinixFeeEmbedded
{
    public List<FinixFee>? Fees { get; set; }
}

internal sealed class FinixAuthorizationList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
}

internal sealed class FinixAuthorizationEmbedded
{
    public List<FinixAuthorization>? Authorizations { get; set; }
}

internal sealed class FinixErrorEnvelope
{
    public int Total { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

internal sealed class FinixErrorEmbedded
{
    public List<FinixError>? Errors { get; set; }
}

internal sealed class FinixError
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Logref { get; set; }
}

internal sealed class FinixCreateIdentityRequest
{
    public object Entity { get; set; } = new { };
}

internal sealed class FinixAddressRequest
{
    public string Line1 { get; set; } = string.Empty;
    public string? Line2 { get; set; }
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Country { get; set; } = "USA";
}

internal sealed class FinixCreatePaymentInstrumentRequest
{
    public string Type { get; set; } = "PAYMENT_CARD";
    public string Identity { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string Name { get; set; } = string.Empty;
    public FinixAddressRequest? Address { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

internal sealed class FinixDisablePaymentInstrumentRequest
{
    public bool Enabled { get; set; } = false;
}

internal sealed class FinixCreateAuthorizationRequest
{
    public long Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? IdempotencyId { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

internal sealed class FinixCaptureAuthorizationRequest
{
    public long CaptureAmount { get; set; }
}

internal sealed class FinixVoidAuthorizationRequest
{
    public bool VoidMe { get; set; } = true;
}

internal sealed class FinixCreateReversalRequest
{
    public long RefundAmount { get; set; }
}
