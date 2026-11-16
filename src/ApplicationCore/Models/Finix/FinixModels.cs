using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Finix;

/// <summary>
/// Hand-written DTOs for the subset of the Finix OpenAPI contract used by this integration.
/// Field names follow finix-spec/finix-openapi.yaml.
/// </summary>
public sealed class FinixAddress
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

public sealed class FinixCreateIdentityRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "PERSONAL";
    [JsonPropertyName("identity_roles")] public List<string> IdentityRoles { get; set; } = new() { "BUYER" };
    [JsonPropertyName("entity")] public FinixIdentityEntity Entity { get; set; } = new();
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixIdentityEntity
{
    [JsonPropertyName("email")] public string? Email { get; set; }
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
}

public sealed class FinixIdentityResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

public sealed class FinixCreatePaymentInstrumentRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "PAYMENT_CARD";
    [JsonPropertyName("identity")] public string Identity { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("number")] public string Number { get; set; } = string.Empty;
    [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("address")] public FinixAddress? Address { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixUpdatePaymentInstrumentRequest
{
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public sealed class FinixPaymentInstrumentResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_four")] public string? LastFour { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYear { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
}

public sealed class FinixCreateAuthorizationRequest
{
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
    [JsonPropertyName("merchant")] public string Merchant { get; set; } = string.Empty;
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")] public long CaptureAmount { get; set; }
}

public sealed class FinixVoidAuthorizationRequest
{
    [JsonPropertyName("void_me")] public bool VoidMe { get; set; } = true;
}

public sealed class FinixAuthorizationResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("transfer")] public string? TransferId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("is_void")] public bool? IsVoid { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixCreateReversalRequest
{
    [JsonPropertyName("refund_amount")] public long RefundAmount { get; set; }
}

public sealed class FinixTransferResponse
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("fee")] public long? Fee { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public sealed class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")] public FinixAuthorizationEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

public sealed class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")] public List<FinixAuthorizationResponse>? Authorizations { get; set; }
}

public sealed class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")] public FinixTransferEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

public sealed class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")] public List<FinixTransferResponse>? Transfers { get; set; }
}

public sealed class FinixErrorResponse
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("_embedded")] public FinixErrorEmbedded? Embedded { get; set; }
}

public sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")] public List<FinixError>? Errors { get; set; }
}

public sealed class FinixError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("logref")] public string? LogRef { get; set; }
}
