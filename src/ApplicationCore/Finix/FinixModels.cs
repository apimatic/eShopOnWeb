using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Finix;

public class FinixSettings
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MerchantId { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}

public record CardAddress(
    string Line1,
    string? Line2,
    string City,
    string Region,
    string PostalCode,
    string Country);

public record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string SecurityCode,
    string Name,
    CardAddress? Address);

public class FinixIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class FinixPaymentInstrument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("instrument_type")]
    public string? InstrumentType { get; set; }

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int? ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int? ExpirationYear { get; set; }

    [JsonPropertyName("card_type")]
    public string? CardType { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class FinixAuthorization
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

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("fee")]
    public decimal Fee { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; set; }

    [JsonPropertyName("processed_at")]
    public DateTimeOffset? ProcessedAt { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public class FinixFee
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("fee_type")]
    public string? FeeType { get; set; }

    [JsonPropertyName("fee_subtype")]
    public string? FeeSubtype { get; set; }

    [JsonPropertyName("linked_to")]
    public string? LinkedTo { get; set; }

    [JsonPropertyName("linked_type")]
    public string? LinkedType { get; set; }
}

public class FinixFeeList
{
    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    public IEnumerable<FinixFee> Fees => Embedded?.Fees ?? Enumerable.Empty<FinixFee>();
}

public class FinixFeeEmbedded
{
    [JsonPropertyName("fees")]
    public List<FinixFee> Fees { get; set; } = new();
}

public class FinixTransferList
{
    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    public IEnumerable<FinixTransfer> Transfers => Embedded?.Transfers ?? Enumerable.Empty<FinixTransfer>();
}

public class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")]
    public List<FinixTransfer> Transfers { get; set; } = new();
}

public class FinixAuthorizationList
{
    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    public IEnumerable<FinixAuthorization> Authorizations => Embedded?.Authorizations ?? Enumerable.Empty<FinixAuthorization>();
}

public class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")]
    public List<FinixAuthorization> Authorizations { get; set; } = new();
}

public class FinixPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public class FinixErrorEnvelope
{
    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError> Errors { get; set; } = new();
}

public class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("logref")]
    public string? LogReference { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("authorization")]
    public string? Authorization { get; set; }
}
