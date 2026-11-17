using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Finix;

public class FinixIdentity
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
}

public class FinixAddress
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

public class FinixCreateIdentityRequest
{
    [JsonPropertyName("entity")] public FinixEntity Entity { get; set; } = new();
    [JsonPropertyName("type")] public string Type { get; set; } = "PERSONAL";
}

public class FinixEntity
{
    [JsonPropertyName("first_name")] public string? FirstName { get; set; }
    [JsonPropertyName("last_name")] public string? LastName { get; set; }
    [JsonPropertyName("email")] public string? Email { get; set; }
}

public class FinixCreatePaymentInstrumentRequest
{
    [JsonPropertyName("type")] public string Type { get; set; } = "PAYMENT_CARD";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("number")] public string? Number { get; set; }
    [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
    [JsonPropertyName("security_code")] public string? SecurityCode { get; set; }
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("address")] public FinixAddress? Address { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixPaymentInstrument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_four")] public string? LastFour { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYear { get; set; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public class FinixCreateAuthorizationRequest
{
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("currency")] public string Currency { get; set; } = string.Empty;
    [JsonPropertyName("merchant")] public string Merchant { get; set; } = string.Empty;
    [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("statement_descriptor")] public string? StatementDescriptor { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixAuthorization
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("is_void")] public bool? IsVoid { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixCaptureAuthorizationRequest
{
    [JsonPropertyName("capture_amount")] public long CaptureAmount { get; set; }
}

public class FinixVoidAuthorizationRequest
{
    [JsonPropertyName("void_me")] public bool VoidMe { get; set; } = true;
}

public class FinixCreateRefundRequest
{
    [JsonPropertyName("refund_amount")] public long RefundAmount { get; set; }
}

public class FinixTransfer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("fee")] public long? Fee { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

public class FinixPage
{
    [JsonPropertyName("limit")] public int? Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")] public FinixTransferEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

public class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")] public List<FinixTransfer>? Transfers { get; set; }
}

public class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")] public FinixAuthorizationEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

public class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")] public List<FinixAuthorization>? Authorizations { get; set; }
}

