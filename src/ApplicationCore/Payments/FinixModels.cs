using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public sealed class FinixAddress
{
    [JsonPropertyName("line1")] public string? Line1 { get; set; }
    [JsonPropertyName("line2")] public string? Line2 { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("region")] public string? Region { get; set; }
    [JsonPropertyName("postal_code")] public string? PostalCode { get; set; }
    [JsonPropertyName("country")] public string? Country { get; set; }
}

public sealed class FinixIdentity
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixPaymentInstrument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_four")] public string? Last4 { get; set; }
    [JsonPropertyName("expiration_month")] public int ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int ExpirationYear { get; set; }
    [JsonPropertyName("card_type")] public string? CardType { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("identity")] public string? Identity { get; set; }
}

public sealed class FinixAuthorization
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("is_void")] public bool IsVoid { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
}

public sealed class FinixTransfer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long? AmountRequested { get; set; }
    [JsonPropertyName("fee")] public long Fee { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("processed_at")] public DateTimeOffset? ProcessedAt { get; set; }
    [JsonPropertyName("ready_to_settle_at")] public DateTimeOffset? ReadyToSettleAt { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
}

public sealed class FinixListResponse<T>
{
    [JsonPropertyName("_embedded")] public FinixEmbedded<T>? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

public sealed class FinixEmbedded<T>
{
    [JsonPropertyName("transfers")] public List<T>? Transfers { get; set; }
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

public sealed class FinixErrorResponse
{
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("_embedded")] public FinixErrorContainer? Embedded { get; set; }
}

public sealed class FinixErrorContainer
{
    [JsonPropertyName("errors")] public List<FinixError>? Errors { get; set; }
}

public sealed class FinixError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("logref")] public string? Logref { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
}
