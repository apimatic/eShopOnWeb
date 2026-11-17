using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixAuthorization
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Transfer { get; set; }
    public string? Source { get; set; }
    public string? IdempotencyId { get; set; }
    public bool IsVoid { get; set; }
    public string? VoidState { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, object>? Tags { get; set; }
}

public sealed class FinixTransfer
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal? Fee { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public string? ParentTransfer { get; set; }
    public string? Source { get; set; }
    public string? IdempotencyId { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public Dictionary<string, object>? Tags { get; set; }
}

public sealed class FinixFee
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? LinkedTo { get; set; }
    public string? LinkedType { get; set; }
    public string? FeeType { get; set; }
    public string? Category { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPaymentInstrument
{
    public string Id { get; set; } = string.Empty;
    public string? Brand { get; set; }
    public string? LastFour { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public bool? Enabled { get; set; }
    public string? Fingerprint { get; set; }
    public string? Type { get; set; }
    public string? InstrumentType { get; set; }
    public string? Identity { get; set; }
    public string? DisabledCode { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixIdentity
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public List<string>? IdentityRoles { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPage
{
    public int? Limit { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class FinixLinks
{
    public FinixLink? Next { get; set; }
    public FinixLink? Self { get; set; }
}

public sealed class FinixLink
{
    public string? Href { get; set; }
}

public sealed class FinixAuthorizationList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
}

public sealed class FinixAuthorizationEmbedded
{
    public List<FinixAuthorization> Authorizations { get; set; } = new();
}

public sealed class FinixTransferList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
}

public sealed class FinixTransferEmbedded
{
    public List<FinixTransfer> Transfers { get; set; } = new();
}

public sealed class FinixFeeList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }
}

public sealed class FinixFeeEmbedded
{
    public List<FinixFee> Fees { get; set; } = new();
}

public sealed class FinixPaymentInstrumentList
{
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixPaymentInstrumentEmbedded? Embedded { get; set; }
}

public sealed class FinixPaymentInstrumentEmbedded
{
    public List<FinixPaymentInstrument> PaymentInstruments { get; set; } = new();
}

public sealed class FinixCreateAuthorizationRequest
{
    public long Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string IdempotencyId { get; set; } = string.Empty;
    public IReadOnlyDictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCaptureAuthorizationRequest
{
    public long CaptureAmount { get; set; }
}

public sealed class FinixVoidAuthorizationRequest
{
    public bool VoidMe { get; set; } = true;
}

public sealed class FinixCreateReversalRequest
{
    public long RefundAmount { get; set; }
}

public sealed class FinixCreateIdentityRequest
{
    public string Type { get; set; } = "PERSONAL";
    public List<string> IdentityRoles { get; set; } = new() { "BUYER" };
    public FinixIdentityEntity Entity { get; set; } = new();
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixIdentityEntity
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

public sealed class FinixCreatePaymentInstrumentRequest
{
    public string Type { get; set; } = "PAYMENT_CARD";
    public string Identity { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public FinixAddress? Address { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class FinixDisablePaymentInstrumentRequest
{
    public bool Enabled { get; set; }
}

public sealed class FinixGenericError
{
    public int Total { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

public sealed class FinixErrorEmbedded
{
    public List<FinixError> Errors { get; set; } = new();
}

public sealed class FinixError
{
    public string? Code { get; set; }
    public string? Message { get; set; }
    public string? Logref { get; set; }
}
