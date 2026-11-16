using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class FinixIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

public sealed class FinixPaymentInstrument
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

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }
}

public sealed class FinixAuthorization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }
}

public sealed class FinixTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("tags")]
    public Dictionary<string, string>? Tags { get; set; }
}

public sealed class FinixFee
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("fee_type")]
    public string? FeeType { get; set; }

    [JsonPropertyName("linked_to")]
    public string? LinkedTo { get; set; }
}

public sealed class FinixPage
{
    [JsonPropertyName("limit")]
    public int? Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public sealed class FinixTransferList
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }
}

public sealed class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")]
    public List<FinixTransfer>? Transfers { get; set; }
}

public sealed class FinixFeeList
{
    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }
}

public sealed class FinixFeeEmbedded
{
    [JsonPropertyName("fees")]
    public List<FinixFee>? Fees { get; set; }
}

public sealed class FinixErrorResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }
}

public sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError>? Errors { get; set; }
}

public sealed class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("logref")]
    public string? LogRef { get; set; }
}
