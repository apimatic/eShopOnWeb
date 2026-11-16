using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public sealed class FinixAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public sealed class FinixCardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? SecurityCode { get; set; }
    public string? Name { get; set; }
    public FinixAddress? Address { get; set; }
}

public sealed class FinixIdentityEntity
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public FinixAddress? PersonalAddress { get; set; }
    public string? Phone { get; set; }
}

public sealed class FinixIdentity
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public FinixIdentityEntity? Entity { get; set; }
    public List<string>? IdentityRoles { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPaymentInstrument
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? InstrumentType { get; set; }
    public string? Brand { get; set; }
    public string? Bin { get; set; }
    public string? LastFour { get; set; }
    public string? CardType { get; set; }
    public int? ExpirationMonth { get; set; }
    public int? ExpirationYear { get; set; }
    public string? Name { get; set; }
    public string? Identity { get; set; }
    public string? Fingerprint { get; set; }
    public bool? Enabled { get; set; }
    public string? DisabledCode { get; set; }
    public string? DisabledMessage { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixAuthorization
{
    public string Id { get; set; } = string.Empty;
    public int? Amount { get; set; }
    public int? AmountRequested { get; set; }
    public string? Currency { get; set; }
    public string? State { get; set; }
    public bool? IsVoid { get; set; }
    public string? VoidState { get; set; }
    public string? Transfer { get; set; }
    public string? Source { get; set; }
    public string? Merchant { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? IdempotencyId { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixTransfer
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? State { get; set; }
    public int? Amount { get; set; }
    public int? AmountRequested { get; set; }
    public int? Fee { get; set; }
    public string? Currency { get; set; }
    public string? Source { get; set; }
    public string? Destination { get; set; }
    public string? ParentTransfer { get; set; }
    public string? OperationKey { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public string? IdempotencyId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixPage
{
    public int? Limit { get; set; }
    public string? NextCursor { get; set; }
}

public sealed class FinixTransferEmbedded
{
    public List<FinixTransfer> Transfers { get; set; } = new();
}

public sealed class FinixAuthorizationEmbedded
{
    public List<FinixAuthorization> Authorizations { get; set; } = new();
}

public sealed class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
    public FinixPage? Page { get; set; }
}

public sealed class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
    public FinixPage? Page { get; set; }
}

public sealed class FinixCreateIdentityRequest
{
    public FinixIdentityEntity Entity { get; set; } = new();
    public List<string> IdentityRoles { get; set; } = new() { "BUYER" };
    public string Type { get; set; } = "PERSONAL";
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCreatePaymentInstrumentRequest
{
    public FinixAddress? Address { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Identity { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string Type { get; set; } = "PAYMENT_CARD";
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixUpdatePaymentInstrumentRequest
{
    public bool Enabled { get; set; }
}

public sealed class FinixCreateAuthorizationRequest
{
    public int Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? IdempotencyId { get; set; }
    public string? SecurityCode { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCaptureAuthorizationRequest
{
    public int CaptureAmount { get; set; }
}

public sealed class FinixVoidAuthorizationRequest
{
    public bool VoidMe { get; set; } = true;
}

public sealed class FinixCreateReversalRequest
{
    public int RefundAmount { get; set; }
}

public sealed class FinixErrorItem
{
    public string? Code { get; set; }
    public string? Logref { get; set; }
    public string? Message { get; set; }
}

public sealed class FinixErrorEmbedded
{
    public List<FinixErrorItem> Errors { get; set; } = new();
}

public sealed class FinixErrorEnvelope
{
    public int? Total { get; set; }
    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}
