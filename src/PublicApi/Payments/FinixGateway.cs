using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// Card data for a one-off payment. Deliberately a class (not a record) so no
/// generated <c>ToString()</c> can echo the PAN or security code into a log.
/// </summary>
public sealed class CardDetails
{
    public string Number { get; set; } = string.Empty;
    public int ExpirationMonth { get; set; }
    public int ExpirationYear { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SecurityCode { get; set; }
}

public sealed record FinixIdentityInfo(string Id);

public sealed record FinixCardInfo(
    string Id,
    string? LastFour,
    string? Brand,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Fingerprint,
    bool Enabled,
    string? IdentityId);

public sealed record FinixAuthorizationInfo(
    string Id,
    string? State,
    bool IsVoid,
    string? VoidState,
    long Amount,
    string? Source,
    string? TransferId,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransferInfo(
    string Id,
    long Amount,
    long Fee,
    string? State,
    string? Type,
    string? ParentTransferId,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? CreatedAt,
    string? IdempotencyId);

public sealed record FinixPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed class PaymentProviderException : Exception
{
    public PaymentProviderException(
        string message,
        int? statusCode,
        string? code,
        string? logref,
        bool outcomeUnknown,
        Exception? innerException = null,
        string? referenceId = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        Code = code;
        Logref = logref;
        OutcomeUnknown = outcomeUnknown;
        ReferenceId = referenceId;
    }

    public int? StatusCode { get; }
    public string? Code { get; }
    public string? Logref { get; }
    public bool OutcomeUnknown { get; }
    public string? ReferenceId { get; }
}

public interface IFinixGateway
{
    Task<FinixIdentityInfo> CreateIdentityAsync(string email, CancellationToken cancellationToken);
    Task<FinixIdentityInfo?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken);

    Task<FinixCardInfo> CreateCardAsync(string identityId, CardDetails card, CancellationToken cancellationToken);
    Task<FinixCardInfo?> GetCardAsync(string paymentInstrumentId, CancellationToken cancellationToken);
    Task<FinixCardInfo?> FindCardAsync(
        string identityId,
        string lastFour,
        int expirationMonth,
        int expirationYear,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken);
    Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken);

    Task<FinixAuthorizationInfo> AuthorizeAsync(
        string paymentInstrumentId,
        long amountCents,
        string idempotencyId,
        string? securityCode,
        CancellationToken cancellationToken);
    Task<FinixAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken);
    Task<FinixAuthorizationInfo?> FindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken);
    Task<FinixAuthorizationInfo> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken);
    Task<FinixAuthorizationInfo> VoidAsync(string authorizationId, CancellationToken cancellationToken);

    Task<FinixTransferInfo> RefundAsync(
        string transferId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken);
    Task<FinixTransferInfo?> GetTransferAsync(string transferId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FinixTransferInfo>> FindRefundsAsync(
        string parentTransferId,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken);

    Task<FinixPage<FinixTransferInfo>> ListTransfersPageAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        CancellationToken cancellationToken);
    Task<FinixPage<FinixAuthorizationInfo>> ListAuthorizationsPageAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        CancellationToken cancellationToken);
}

public sealed class FinixGateway : IFinixGateway
{
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly Currency _currency;

    public FinixGateway(FinixApiReferenceClient client, FinixOptions options, Currency currency)
    {
        _client = client;
        _options = options;
        _currency = currency;
    }

    public async Task<FinixIdentityInfo> CreateIdentityAsync(string email, CancellationToken cancellationToken)
    {
        var (firstName, lastName) = NamesFromEmail(email);
        var body = IdentitiesRequest.Buyer(new Buyer
        {
            Entity = new Entity5
            {
                Email = email,
                FirstName = firstName,
                LastName = lastName
            },
            Type = Type10.Personal
        });

        try
        {
            var identity = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = body },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new PaymentProviderException(
                    "Finix created an identity without an id.", null, "MISSING_IDENTITY_ID", null, true);
            }

            return new FinixIdentityInfo(identity.Id!);
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixIdentityInfo?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Identities.ListIdentities(
                new ListIdentitiesRequest { Email = email, Limit = 10 },
                cancellationToken: cancellationToken);

            var identity = response.Embedded?.Identities?.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Id));
            return identity is null ? null : new FinixIdentityInfo(identity.Id!);
        }
        catch (ApiException<ListIdentitiesError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixCardInfo> CreateCardAsync(
        string identityId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
        {
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            SecurityCode = card.SecurityCode
        });

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest { Body = body },
                cancellationToken: cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var created) || created is null)
            {
                throw new PaymentProviderException(
                    "Finix returned an unrecognised payment-instrument payload.",
                    null,
                    "UNRECOGNISED_PAYMENT_INSTRUMENT",
                    null,
                    true);
            }

            return MapCard(created);
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixCardInfo?> GetCardAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var card) || card is null)
            {
                return null;
            }

            return MapCard(card);
        }
        catch (ApiException<GetPaymentInstrumentError> ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixCardInfo?> FindCardAsync(
        string identityId,
        string lastFour,
        int expirationMonth,
        int expirationYear,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages++ < 20)
            {
                var response = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        OwnerIdentityId = identityId,
                        Last4 = lastFour,
                        ExpirationMonth = expirationMonth.ToString(),
                        ExpirationYear = expirationYear.ToString(),
                        CreatedAtGte = createdAfter,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    cancellationToken: cancellationToken);

                var match = response.Embedded?.PaymentInstruments?
                    .Select(item => item.TryGetPaymentInstrumentCard(out var card) ? card : null)
                    .Where(card => card is not null)
                    .Select(card => MapCard(card!))
                    .FirstOrDefault(card =>
                        card.LastFour == lastFour &&
                        card.ExpirationMonth == expirationMonth &&
                        card.ExpirationYear == expirationYear);

                if (match is not null)
                {
                    return match;
                }

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return null;
                }
            }

            return null;
        }
        catch (ApiException<ListPaymentInstrumentsError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        var body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
            new PaymentInstrumentDisableAPaymentInstrument { Enabled = false });

        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentInstrumentId,
                    Body = body
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }

            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixAuthorizationInfo> AuthorizeAsync(
        string paymentInstrumentId,
        long amountCents,
        string idempotencyId,
        string? securityCode,
        CancellationToken cancellationToken)
    {
        var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = _currency,
            Merchant = _options.MerchantId!,
            Source = paymentInstrumentId,
            IdempotencyId = idempotencyId,
            SecurityCode = securityCode
        });

        try
        {
            var authorization = await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest { Body = body },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixAuthorizationInfo> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixAuthorizationInfo?> FindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    IdempotencyId = idempotencyId,
                    CreatedAtGte = createdAfter,
                    Limit = 100
                },
                cancellationToken: cancellationToken);

            var authorization = response.Embedded?.Authorizations?
                .FirstOrDefault(item => item.IdempotencyId == idempotencyId);

            return authorization is null ? null : MapAuthorization(authorization);
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixAuthorizationInfo> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        var body = AuthorizationsRequest1.CaptureAuthorization(
            new CaptureAuthorization { CaptureAmount = amountCents });

        try
        {
            var authorization = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = authorizationId,
                    Body = body
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixAuthorizationInfo> VoidAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixTransferInfo> RefundAsync(
        string transferId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (amountCents > int.MaxValue)
        {
            throw new PaymentProviderException(
                "A single refund cannot exceed the Finix integer limit.",
                null,
                "REFUND_TOO_LARGE",
                null,
                false);
        }

        var body = new RefundTransfer { RefundAmount = (int)amountCents };
        body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

        try
        {
            var transfer = await _client.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest
                {
                    TransferId = transferId,
                    Body = TransfersReversalsRequest.RefundTransfer(body)
                },
                cancellationToken: cancellationToken);

            return MapTransfer(transfer);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixTransferInfo?> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: cancellationToken);

            return MapTransfer(transfer);
        }
        catch (ApiException<GetTransferError> ex)
        {
            if (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<IReadOnlyList<FinixTransferInfo>> FindRefundsAsync(
        string parentTransferId,
        DateTimeOffset createdAfter,
        CancellationToken cancellationToken)
    {
        var matches = new List<FinixTransferInfo>();
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages++ < 20)
            {
                var response = await _client.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = parentTransferId,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    cancellationToken: cancellationToken);

                var transfers = response.Embedded?.Transfers;
                if (transfers is not null)
                {
                    matches.AddRange(transfers
                        .Select(MapTransfer)
                        .Where(transfer =>
                            transfer.CreatedAt is null || transfer.CreatedAt >= createdAfter));
                }

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    break;
                }
            }

            return matches;
        }
        catch (ApiException<ListTransferReversalsError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixPage<FinixTransferInfo>> ListTransfersPageAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    AfterCursor = cursor,
                    Limit = 100
                },
                cancellationToken: cancellationToken);

            var items = response.Embedded?.Transfers?.Select(MapTransfer).ToList()
                        ?? new List<FinixTransferInfo>();

            return new FinixPage<FinixTransferInfo>(items, response.Page?.NextCursor);
        }
        catch (ApiException<ListTransfersError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    public async Task<FinixPage<FinixAuthorizationInfo>> ListAuthorizationsPageAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string? cursor,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    AfterCursor = cursor,
                    Limit = 100
                },
                cancellationToken: cancellationToken);

            var items = response.Embedded?.Authorizations?.Select(MapAuthorization).ToList()
                        ?? new List<FinixAuthorizationInfo>();

            return new FinixPage<FinixAuthorizationInfo>(items, response.Page?.NextCursor);
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            throw ToProviderException(ex, ex.Error.TryGetGenericError(out var generic) ? generic : null, ex.Error.TryGetRawError(out var raw) ? raw : null);
        }
        catch (SdkException ex)
        {
            throw ToProviderException(ex);
        }
    }

    private static FinixCardInfo MapCard(PaymentInstrumentCard card) =>
        new(
            card.Id ?? string.Empty,
            card.LastFour,
            card.Brand?.Value,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Fingerprint,
            card.Enabled ?? true,
            card.Identity);

    private static FinixAuthorizationInfo MapAuthorization(Authorization authorization) =>
        new(
            authorization.Id ?? string.Empty,
            authorization.State?.Value,
            authorization.IsVoid ?? false,
            authorization.VoidState?.Value,
            authorization.Amount ?? 0,
            authorization.Source,
            authorization.Transfer,
            authorization.FailureCode,
            authorization.FailureMessage);

    private static FinixTransferInfo MapTransfer(Transfer transfer) =>
        new(
            transfer.Id ?? string.Empty,
            transfer.Amount ?? 0,
            transfer.Fee ?? 0,
            transfer.State?.Value,
            transfer.Type?.Value,
            transfer.ParentTransfer,
            transfer.FailureCode,
            transfer.FailureMessage,
            transfer.CreatedAt,
            transfer.IdempotencyId);

    private static PaymentProviderException ToProviderException(ApiException exception, GenericError? generic, RawError? raw = null)
    {
        var error = generic?.Embedded?.Errors?.FirstOrDefault();
        var rawBody = raw?.ReadAsString();
        var (rawMessage, rawCode, rawReferenceId) = ParseRawError(rawBody);
        var message = error?.Message ?? rawMessage ?? exception.Message;
        var code = error?.Code ?? rawCode;
        var referenceId = error?.AdditionalProperties.TryGetValue<string>("transfer", out var transferId) == true
                          && !string.IsNullOrWhiteSpace(transferId)
            ? transferId
            : rawReferenceId;

        return new PaymentProviderException(
            message,
            (int)exception.StatusCode,
            code,
            error?.Logref,
            false,
            exception,
            referenceId);
    }

    private static (string? Message, string? Code, string? ReferenceId) ParseRawError(string? rawBody)
    {
        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return (null, null, null);
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(rawBody);
            if (!document.RootElement.TryGetProperty("_embedded", out var embedded)
                || !embedded.TryGetProperty("errors", out var errors)
                || errors.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                return (null, null, null);
            }

            foreach (var item in errors.EnumerateArray())
            {
                var message = item.TryGetProperty("message", out var messageElement)
                              && messageElement.ValueKind == System.Text.Json.JsonValueKind.String
                    ? messageElement.GetString()
                    : null;
                var code = item.TryGetProperty("code", out var codeElement)
                           && codeElement.ValueKind == System.Text.Json.JsonValueKind.String
                    ? codeElement.GetString()
                    : null;
                var referenceId = ExtractReferenceId(item);
                return (message, code, referenceId);
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return (null, null, null);
    }

    private static string? ExtractReferenceId(System.Text.Json.JsonElement item)
    {
        if (item.TryGetProperty("transfer", out var transfer)
            && transfer.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            return transfer.GetString();
        }

        if (item.TryGetProperty("_links", out var links)
            && links.TryGetProperty("transfer", out var transferLink)
            && transferLink.TryGetProperty("href", out var href)
            && href.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var value = href.GetString();
            var marker = value?.LastIndexOf("/transfers/", StringComparison.Ordinal) ?? -1;
            if (marker >= 0)
            {
                var id = value![(marker + "/transfers/".Length)..];
                var slash = id.IndexOf('/');
                return slash >= 0 ? id[..slash] : id;
            }
        }

        return null;
    }

    private static PaymentProviderException ToProviderException(SdkException exception)
    {
        return exception switch
        {
            SdkTimeoutException timeout => new PaymentProviderException(
                $"Finix did not answer within {timeout.Timeout}.",
                null,
                "TIMEOUT",
                null,
                true,
                timeout),
            SdkConnectionException connection => new PaymentProviderException(
                "Finix could not be reached; the operation outcome is unknown.",
                null,
                "CONNECTION",
                null,
                true,
                connection),
            AuthSchemeException auth => new PaymentProviderException(
                "Finix credentials are missing or were rejected.",
                null,
                "AUTH_CONFIG",
                null,
                false,
                auth),
            ResponseDeserializationException deserialization => new PaymentProviderException(
                "Finix returned a response that could not be processed.",
                (int)deserialization.StatusCode,
                "RESPONSE_DESERIALIZATION",
                null,
                (int)deserialization.StatusCode is >= 200 and < 300,
                deserialization),
            _ => new PaymentProviderException(
                "The Finix request failed.",
                null,
                "SDK_ERROR",
                null,
                false,
                exception)
        };
    }

    private static (string FirstName, string LastName) NamesFromEmail(string email)
    {
        var local = email.Split('@')[0];
        var parts = local.Split(['.', '_', '-'], StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : "eShop";
        var last = parts.Length > 1 ? parts[^1] : "Shopper";

        return (Trim(first, 20), Trim(last, 20));
    }

    private static string Trim(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
