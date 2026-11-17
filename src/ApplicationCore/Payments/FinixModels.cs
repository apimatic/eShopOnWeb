using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class FinixAddress
{
    public string? Line1 { get; set; }
    public string? Line2 { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
}

public class FinixIdentityEntity
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Phone { get; set; }
    public FinixAddress? PersonalAddress { get; set; }
}

public class FinixCreateIdentityRequest
{
    public FinixIdentityEntity Entity { get; set; } = new();
    public List<string> IdentityRoles { get; set; } = new() { "BUYER" };
    public string Type { get; set; } = "PERSONAL";
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixIdentityResponse
{
    public string Id { get; set; } = string.Empty;
    public string? Type { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixCreatePaymentInstrumentRequest
{
    public FinixAddress? Address { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Identity { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Number { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string Type { get; set; } = "PAYMENT_CARD";
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixPaymentInstrumentResponse
{
    public string Id { get; set; } = string.Empty;
    public string? Brand { get; set; }
    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? InstrumentType { get; set; }
    public string? Identity { get; set; }
    public bool Enabled { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixUpdatePaymentInstrumentRequest
{
    public bool Enabled { get; set; }
}

public class FinixCreateAuthorizationRequest
{
    public long Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Merchant { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
    public string? IdempotencyId { get; set; }
    public string? StatementDescriptor { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixAuthorizationResponse
{
    public string Id { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string? Currency { get; set; }
    public string State { get; set; } = string.Empty;
    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }
    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }
    public string? Transfer { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? Source { get; set; }
    public string? Merchant { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FinixCaptureAuthorizationRequest
{
    public long CaptureAmount { get; set; }
}

public class FinixVoidAuthorizationRequest
{
    public bool VoidMe { get; set; } = true;
}

public class FinixTransferResponse
{
    public string Id { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long Fee { get; set; }
    public string State { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? OperationKey { get; set; }
    public string? ParentTransfer { get; set; }
    public string? Source { get; set; }
    public string? Currency { get; set; }
    public string? FailureCode { get; set; }
    public string? FailureMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixCreateReversalRequest
{
    public long RefundAmount { get; set; }
}

public class FinixPage
{
    public int Limit { get; set; }
    public string? NextCursor { get; set; }
}

public class FinixLink
{
    public string? Href { get; set; }
}

public class FinixLinks
{
    public FinixLink? Self { get; set; }
    public FinixLink? Next { get; set; }
}

public class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }
    [JsonPropertyName("_links")]
    public FinixLinks? Links { get; set; }
    public FinixPage? Page { get; set; }
}

public class FinixAuthorizationEmbedded
{
    public List<FinixAuthorizationResponse>? Authorizations { get; set; }
}

public class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
    [JsonPropertyName("_links")]
    public FinixLinks? Links { get; set; }
    public FinixPage? Page { get; set; }
}

public class FinixTransferEmbedded
{
    public List<FinixTransferResponse>? Transfers { get; set; }
}

public class FinixFeeResponse
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Currency { get; set; }
    public string? Category { get; set; }
    public string? FeeType { get; set; }
    public string? FeeSubtype { get; set; }
    public string? LinkedTo { get; set; }
    public string? LinkedType { get; set; }
    public string? Merchant { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class FinixFeeListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }
    [JsonPropertyName("_links")]
    public FinixLinks? Links { get; set; }
    public FinixPage? Page { get; set; }
}

public class FinixFeeEmbedded
{
    public List<FinixFeeResponse>? Fees { get; set; }
}
public class FinixErrorResponse
{
    public int Total { get; set; }
    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

public class FinixErrorEmbedded
{
    public List<FinixErrorItem>? Errors { get; set; }
}

public class FinixErrorItem
{
    public string? Code { get; set; }
    public string? Logref { get; set; }
    public string? Message { get; set; }
}
