using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixAddress
{
    [JsonPropertyName("line1")] public string Line1 { get; set; } = string.Empty;
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string City { get; set; } = string.Empty;
    [JsonPropertyName("region")] public string Region { get; set; } = string.Empty;
    [JsonPropertyName("postal_code")] public string PostalCode { get; set; } = string.Empty;
    [JsonPropertyName("country")] public string Country { get; set; } = "USA";
}

public sealed class FinixIdentity
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; set; }
}

public sealed class FinixPaymentInstrument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("card_type")] public string? CardType { get; set; }
    [JsonPropertyName("last_four")] public string? LastFour { get; set; }
    [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
}

public sealed class FinixAuthorization
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("is_void")] public bool IsVoid { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixTransfer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("amount")] public int Amount { get; set; }
    [JsonPropertyName("amount_requested")] public int AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("fee")] public int Fee { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixFee
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public decimal Amount { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("linked_to")] public string? LinkedTo { get; set; }
    [JsonPropertyName("linked_type")] public string? LinkedType { get; set; }
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("fee_type")] public string? FeeType { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public sealed class FinixTransferList
{
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
    [JsonPropertyName("_embedded")] public FinixTransferEmbedded? Embedded { get; set; }
    [JsonPropertyName("unreversed_amount")] public int? UnreversedAmount { get; set; }
}

public sealed class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")] public List<FinixTransfer>? Transfers { get; set; }
}

public sealed class FinixAuthorizationList
{
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
    [JsonPropertyName("_embedded")] public FinixAuthorizationEmbedded? Embedded { get; set; }
}

public sealed class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")] public List<FinixAuthorization>? Authorizations { get; set; }
}

public sealed class FinixFeeList
{
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
    [JsonPropertyName("_embedded")] public FinixFeeEmbedded? Embedded { get; set; }
}

public sealed class FinixFeeEmbedded
{
    [JsonPropertyName("fees")] public List<FinixFee>? Fees { get; set; }
}

public sealed class FinixErrorEnvelope
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
    [JsonPropertyName("logref")] public string? Logref { get; set; }
    [JsonPropertyName("authorization")] public string? Authorization { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
}
