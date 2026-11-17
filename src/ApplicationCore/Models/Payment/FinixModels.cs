using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.ApplicationCore.Models.Payment;

public class FinixIdentity
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
}

public class FinixPaymentInstrument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("brand")]
    public string? Brand { get; set; }

    [JsonPropertyName("last_four")]
    public string? LastFour { get; set; }

    [JsonPropertyName("expiration_month")]
    public int ExpirationMonth { get; set; }

    [JsonPropertyName("expiration_year")]
    public int ExpirationYear { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class FinixAuthorization
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }

    [JsonPropertyName("is_void")]
    public bool IsVoid { get; set; }

    [JsonPropertyName("void_state")]
    public string? VoidState { get; set; }

    [JsonPropertyName("expires_at")]
    public DateTimeOffset? ExpiresAt { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }
}

public class FinixTransfer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public long Amount { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("operation_key")]
    public string? OperationKey { get; set; }

    [JsonPropertyName("parent_transfer")]
    public string? ParentTransfer { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("idempotency_id")]
    public string? IdempotencyId { get; set; }

    [JsonPropertyName("failure_code")]
    public string? FailureCode { get; set; }

    [JsonPropertyName("failure_message")]
    public string? FailureMessage { get; set; }
}

public class FinixFee
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("category")]
    public string? Category { get; set; }

    [JsonPropertyName("fee_type")]
    public string? FeeType { get; set; }

    [JsonPropertyName("linked_to")]
    public string? LinkedTo { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }
}

public class FinixPage
{
    [JsonPropertyName("limit")]
    public int Limit { get; set; }

    [JsonPropertyName("next_cursor")]
    public string? NextCursor { get; set; }
}

public class FinixTransferListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixTransferEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")]
    public List<FinixTransfer>? Transfers { get; set; }
}

public class FinixFeeListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixFeeEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public class FinixFeeEmbedded
{
    [JsonPropertyName("fees")]
    public List<FinixFee>? Fees { get; set; }
}

public class FinixAuthorizationListResponse
{
    [JsonPropertyName("_embedded")]
    public FinixAuthorizationEmbedded? Embedded { get; set; }

    [JsonPropertyName("page")]
    public FinixPage? Page { get; set; }
}

public class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")]
    public List<FinixAuthorization>? Authorizations { get; set; }
}

public class FinixErrorResponse
{
    [JsonPropertyName("_embedded")]
    public FinixErrorEmbedded? Embedded { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class FinixErrorEmbedded
{
    [JsonPropertyName("errors")]
    public List<FinixError>? Errors { get; set; }
}

public class FinixError
{
    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("logref")]
    public string? LogRef { get; set; }

    [JsonPropertyName("transfer")]
    public string? Transfer { get; set; }
}
