using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Finix;

public interface IFinixClient
{
    Task<string> GetMerchantIdentityAsync(CancellationToken cancellationToken = default);

    Task<FinixPaymentInstrument> CreatePaymentInstrumentAsync(
        string identity,
        ApplicationCore.Entities.PaymentAggregate.CardDetailsInput card,
        CancellationToken cancellationToken = default);

    Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken = default);

    Task<FinixAuthorization> CreateAuthorizationAsync(
        long amountCents,
        string currency,
        string source,
        string idempotencyId,
        IReadOnlyDictionary<string, string>? tags,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);
    Task<FinixAuthorization> CaptureAuthorizationAsync(
        string authorizationId,
        long captureAmountCents,
        CancellationToken cancellationToken = default);

    Task<FinixAuthorization> VoidAuthorizationAsync(string authorizationId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken = default);

    Task<FinixTransfer> CreateRefundAsync(
        string transferId,
        long refundAmountCents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}

public sealed class FinixPaymentInstrument
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("identity")] public string? Identity { get; set; }
    [JsonPropertyName("instrument_type")] public string? InstrumentType { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("brand")] public string? Brand { get; set; }
    [JsonPropertyName("last_four")] public string? LastFour { get; set; }
    [JsonPropertyName("expiration_month")] public int? ExpirationMonth { get; set; }
    [JsonPropertyName("expiration_year")] public int? ExpirationYear { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("fingerprint")] public string? Fingerprint { get; set; }
    [JsonPropertyName("enabled")] public bool Enabled { get; set; }
}

public sealed class FinixMerchant
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("identity")] public string? Identity { get; set; }
}

public sealed class FinixAuthorization
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("is_void")] public bool IsVoid { get; set; }
    [JsonPropertyName("void_state")] public string? VoidState { get; set; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
}

public sealed class FinixTransfer
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; set; }
    [JsonPropertyName("amount")] public long Amount { get; set; }
    [JsonPropertyName("amount_requested")] public long AmountRequested { get; set; }
    [JsonPropertyName("currency")] public string? Currency { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("operation_key")] public string? OperationKey { get; set; }
    [JsonPropertyName("fee")] public decimal? Fee { get; set; }
    [JsonPropertyName("merchant")] public string? Merchant { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("destination")] public string? Destination { get; set; }
    [JsonPropertyName("parent_transfer")] public string? ParentTransfer { get; set; }
    [JsonPropertyName("idempotency_id")] public string? IdempotencyId { get; set; }
    [JsonPropertyName("failure_code")] public string? FailureCode { get; set; }
    [JsonPropertyName("failure_message")] public string? FailureMessage { get; set; }
    [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
}

internal sealed class FinixTransferList
{
    [JsonPropertyName("_embedded")] public FinixTransferEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

internal sealed class FinixTransferEmbedded
{
    [JsonPropertyName("transfers")] public List<FinixTransfer>? Transfers { get; set; }
}

internal sealed class FinixAuthorizationList
{
    [JsonPropertyName("_embedded")] public FinixAuthorizationEmbedded? Embedded { get; set; }
    [JsonPropertyName("page")] public FinixPage? Page { get; set; }
}

internal sealed class FinixAuthorizationEmbedded
{
    [JsonPropertyName("authorizations")] public List<FinixAuthorization>? Authorizations { get; set; }
}
internal sealed class FinixPage
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; set; }
}

internal sealed class FinixErrorEnvelope
{
    [JsonPropertyName("_embedded")] public FinixErrorEmbedded? Embedded { get; set; }
}

internal sealed class FinixErrorEmbedded
{
    [JsonPropertyName("errors")] public List<FinixError>? Errors { get; set; }
}

internal sealed class FinixError
{
    [JsonPropertyName("code")] public string? Code { get; set; }
    [JsonPropertyName("message")] public string? Message { get; set; }
    [JsonPropertyName("authorization")] public string? Authorization { get; set; }
    [JsonPropertyName("transfer")] public string? Transfer { get; set; }
    [JsonPropertyName("_links")] public FinixErrorLinks? Links { get; set; }
}

internal sealed class FinixErrorLinks
{
    [JsonPropertyName("authorization")] public FinixErrorLink? Authorization { get; set; }
    [JsonPropertyName("transfer")] public FinixErrorLink? Transfer { get; set; }
}

internal sealed class FinixErrorLink
{
    [JsonPropertyName("href")] public string? Href { get; set; }
}




