using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Core.Models;
using FinixApiReference.Errors;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.MerchantsApi;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record CardDetails(
    string Number,
    int ExpirationMonth,
    int ExpirationYear,
    string? SecurityCode,
    string Name);

public sealed record InstrumentInfo(
    string Id,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear,
    string? Fingerprint,
    bool Enabled);

public sealed record AuthorizationInfo(
    string Id,
    string? State,
    bool IsVoid,
    string? VoidState,
    long AmountCents,
    string? TransferId,
    string? FailureCode,
    string? FailureMessage,
    DateTimeOffset? ExpiresAt);

public sealed record TransferInfo(
    string Id,
    string? State,
    long AmountCents,
    string? Type,
    string? ParentTransferId,
    string? IdempotencyId);

public sealed record CaptureInfo(
    string AuthorizationId,
    string? AuthorizationState,
    string TransferId,
    string? TransferState,
    long CapturedAmountCents,
    long FeeAmountCents,
    long NetAmountCents,
    bool FeeTruncated);

public sealed record VoidInfo(
    string AuthorizationId,
    string? State,
    bool IsVoid,
    string? VoidState);

public sealed record RefundInfo(
    string ReversalId,
    string? State,
    long AmountCents);

public sealed record FinixTransactionInfo(
    string Id,
    string Kind,
    string? Type,
    string? State,
    long AmountCents,
    string? Currency,
    DateTimeOffset? CreatedAt,
    string? IdempotencyId,
    string? ParentTransferId,
    string? TransferId);

public sealed record MerchantInfo(string Id, IReadOnlyList<string> Currencies);

public sealed record ReconciliationRead(
    IReadOnlyList<FinixTransactionInfo> Transactions,
    bool Truncated);

public sealed record FeeRead(long Cents, bool Truncated);

/// <summary>
/// The single place the application talks to Finix. It translates the generated SDK surface into
/// small domain records, bounds every call with a total deadline, and converts provider failures
/// into <see cref="PaymentException"/> values with caller-safe messages.
/// </summary>
public sealed class FinixPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(30);
    private const int PageLimit = 100;
    private const int MaxPages = 500;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;

    public FinixPaymentGateway(
        FinixApiReferenceClient client,
        FinixOptions options,
        ILogger<FinixPaymentGateway> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public async Task<MerchantInfo> GetMerchantAsync(CancellationToken cancellationToken)
    {
        try
        {
            var merchant = await BoundedAsync(
                ct => _client.MerchantsApi.GetMerchant(
                    new GetMerchantRequest { MerchantId = _options.MerchantId },
                    cancellationToken: ct),
                cancellationToken);

            return new MerchantInfo(
                merchant.Id ?? _options.MerchantId,
                merchant.Currencies?.Select(c => c.Value).ToArray() ?? Array.Empty<string>());
        }
        catch (ApiException<GetMerchantError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading the Finix merchant");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading the Finix merchant"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading the Finix merchant"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading the Finix merchant"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading the Finix merchant"); }
    }

    public async Task<string> CreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var email = LooksLikeEmail(buyerId) ? buyerId : null;
        var body = new Buyer
        {
            Entity = new Entity5 { Email = email }
        };

        try
        {
            var identity = await BoundedAsync(
                ct => _client.Identities.CreateIdentity(
                    new CreateIdentityRequest { Body = IdentitiesRequest.Buyer(body) },
                    cancellationToken: ct),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(identity.Id))
                throw new PaymentException(502, "PROVIDER_BAD_RESPONSE", "Finix did not return an identity id.");

            return identity.Id!;
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Creating the Finix buyer identity");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Creating the Finix buyer identity"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Creating the Finix buyer identity"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Creating the Finix buyer identity"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Creating the Finix buyer identity"); }
    }

    public async Task<InstrumentInfo> CreateInstrumentAsync(
        string identityId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var body = new PaymentInstrumentCard1
        {
            Identity = identityId,
            Name = string.IsNullOrWhiteSpace(card.Name) ? identityId : card.Name,
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode
        };

        try
        {
            var response = await BoundedAsync(
                ct => _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                    new CreatePaymentInstrumentRequest
                    {
                        Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(body)
                    },
                    cancellationToken: ct),
                cancellationToken);

            if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
                string.IsNullOrWhiteSpace(instrument?.Id))
            {
                throw new PaymentException(502, "PROVIDER_BAD_RESPONSE",
                    "Finix did not return a payment instrument id.");
            }

            return Map(instrument!);
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Saving the card with Finix");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Saving the card with Finix"); }
        catch (SdkTimeoutException ex)
        {
            var settled = await TryFindInstrumentAsync(identityId, card.Number, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Saving the card with Finix");
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TryFindInstrumentAsync(identityId, card.Number, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Saving the card with Finix");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Saving the card with Finix"); }
    }

    public async Task DisableInstrumentAsync(string instrumentId, CancellationToken cancellationToken)
    {
        var body = new PaymentInstrumentDisableAPaymentInstrument { Enabled = false };

        try
        {
            await BoundedAsync(
                ct => _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                    new UpdatePaymentInstrumentRequest
                    {
                        PaymentInstrumentId = instrumentId,
                        Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(body)
                    },
                    cancellationToken: ct),
                cancellationToken);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Disabling the saved card with Finix");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Disabling the saved card with Finix"); }
        catch (SdkTimeoutException ex)
        {
            if (await IsInstrumentDisabledAsync(instrumentId, cancellationToken)) return;
            throw TranslateTransport(ex, "Disabling the saved card with Finix");
        }
        catch (SdkConnectionException ex)
        {
            if (await IsInstrumentDisabledAsync(instrumentId, cancellationToken)) return;
            throw TranslateTransport(ex, "Disabling the saved card with Finix");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Disabling the saved card with Finix"); }
    }

    public async Task<AuthorizationInfo> AuthorizeAsync(
        string paymentReference,
        int attemptNo,
        string sourceInstrumentId,
        long amountCents,
        Currency currency,
        CancellationToken cancellationToken)
    {
        var idempotencyId = $"eshop-{paymentReference}-pay-{attemptNo}";
        var body = new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = currency,
            Merchant = _options.MerchantId,
            Source = sourceInstrumentId,
            IdempotencyId = idempotencyId,
            StatementDescriptor = "ESHOP ORDER"
        };

        try
        {
            var authorization = await BoundedAsync(
                ct => _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest
                    {
                        Body = AuthorizationsRequest.AuthorizationAllFields(body)
                    },
                    cancellationToken: ct),
                cancellationToken);

            return Map(authorization);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Authorizing the order");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Authorizing the order"); }
        catch (SdkTimeoutException ex)
        {
            var settled = await TryFindAuthorizationAsync(idempotencyId, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Authorizing the order");
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TryFindAuthorizationAsync(idempotencyId, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Authorizing the order");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Authorizing the order"); }
    }

    public async Task<AuthorizationInfo> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await BoundedAsync(
                ct => _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = authorizationId },
                    cancellationToken: ct),
                cancellationToken);

            return Map(authorization);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading the authorization");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading the authorization"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading the authorization"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading the authorization"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading the authorization"); }
    }

    public async Task<CaptureInfo> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await BoundedAsync(
                ct => _client.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = authorizationId,
                        Body = AuthorizationsRequest1.CaptureAuthorization(
                            new CaptureAuthorization { CaptureAmount = amountCents })
                    },
                    cancellationToken: ct),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                var reread = await GetAuthorizationAsync(authorizationId, cancellationToken);
                if (string.IsNullOrWhiteSpace(reread.TransferId))
                {
                    throw new PaymentException(409, "AUTHORIZATION_NOT_CAPTURABLE",
                        $"Finix did not capture authorization {authorizationId}. " +
                        $"State: {reread.State ?? "unknown"}; void state: {reread.VoidState ?? "unknown"}; " +
                        $"failure: {reread.FailureCode ?? "none"} {reread.FailureMessage}".Trim());
                }
                authorization = authorization with { Transfer = reread.TransferId };
            }

            var transferId = authorization.Transfer!;
            var transferState = authorization.State?.Value;
            var captured = amountCents;
            var fees = new FeeRead(0, false);

            try
            {
                var transfer = await GetTransferAsync(transferId, cancellationToken);
                transferState = transfer.State ?? transferState;
                if (transfer.AmountCents > 0)
                    captured = transfer.AmountCents;
            }
            catch (PaymentException ex)
            {
                _logger.LogWarning(ex, "Captured transfer {TransferId} could not be read; reporting the requested amount.", transferId);
            }

            try
            {
                fees = await GetFeesAsync(transferId, cancellationToken);
            }
            catch (PaymentException ex)
            {
                _logger.LogWarning(ex, "Fees for captured transfer {TransferId} could not be read.", transferId);
            }

            return new CaptureInfo(
                authorizationId,
                authorization.State?.Value,
                transferId,
                transferState,
                captured,
                fees.Cents,
                captured - fees.Cents,
                fees.Truncated);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Capturing the authorization");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Capturing the authorization"); }
        catch (SdkTimeoutException ex)
        {
            var settled = await TrySettleCaptureAsync(authorizationId, amountCents, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Capturing the authorization");
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TrySettleCaptureAsync(authorizationId, amountCents, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Capturing the authorization");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Capturing the authorization"); }
    }

    public async Task<VoidInfo> VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await BoundedAsync(
                ct => _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = authorizationId,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: ct),
                cancellationToken);

            return new VoidInfo(
                authorizationId,
                authorization.State?.Value,
                authorization.IsVoid ?? false,
                authorization.VoidState?.Value);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Voiding the authorization");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Voiding the authorization"); }
        catch (SdkTimeoutException ex)
        {
            var settled = await TrySettleVoidAsync(authorizationId, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Voiding the authorization");
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TrySettleVoidAsync(authorizationId, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Voiding the authorization");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Voiding the authorization"); }
    }

    public async Task<RefundInfo> RefundAsync(
        string captureTransferId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (amountCents <= 0 || amountCents > int.MaxValue)
            throw PaymentException.BadRequest("INVALID_REFUND_AMOUNT", "The refund amount is outside the supported range.");

        var body = new RefundTransfer { RefundAmount = (int)amountCents };
        body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

        try
        {
            var reversal = await BoundedAsync(
                ct => _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = captureTransferId,
                        Body = TransfersReversalsRequest.RefundTransfer(body)
                    },
                    cancellationToken: ct),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(reversal.Id))
                throw new PaymentException(502, "PROVIDER_BAD_RESPONSE", "Finix did not return a reversal id.");

            return new RefundInfo(reversal.Id!, reversal.State?.Value, reversal.Amount ?? amountCents);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Refunding the payment");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Refunding the payment"); }
        catch (SdkTimeoutException ex)
        {
            var settled = await TryFindRefundAsync(captureTransferId, idempotencyKey, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Refunding the payment");
        }
        catch (SdkConnectionException ex)
        {
            var settled = await TryFindRefundAsync(captureTransferId, idempotencyKey, cancellationToken);
            if (settled is not null) return settled;
            throw TranslateTransport(ex, "Refunding the payment");
        }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Refunding the payment"); }
    }

    public async Task<TransferInfo> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await BoundedAsync(
                ct => _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: ct),
                cancellationToken);

            return Map(transfer);
        }
        catch (ApiException<GetTransferError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading the capture transfer");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading the capture transfer"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading the capture transfer"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading the capture transfer"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading the capture transfer"); }
    }

    public async Task<FeeRead> GetFeesAsync(string linkedTransferId, CancellationToken cancellationToken)
    {
        long total = 0;
        string? cursor = null;
        var pages = 0;

        try
        {
            while (true)
            {
                var page = await BoundedAsync(
                    ct => _client.FeesApi.ListFees(
                        new ListFeesRequest
                        {
                            LinkedTo = linkedTransferId,
                            Limit = PageLimit,
                            AfterCursor = cursor
                        },
                        cancellationToken: ct),
                    cancellationToken);

                if (page.Embedded?.Fees is not null)
                {
                    foreach (var fee in page.Embedded.Fees)
                        total += (long)Math.Round(fee.Amount ?? 0, MidpointRounding.AwayFromZero);
                }

                if ((page.Embedded?.Fees?.Count ?? 0) == 0)
                    return new FeeRead(total, false);

                var next = page.Page?.NextCursor;
                if (string.IsNullOrEmpty(next) || next == cursor)
                    return new FeeRead(total, false);

                if (++pages >= MaxPages)
                    return new FeeRead(total, true);

                cursor = next;
            }
        }
        catch (ApiException<ListFeesError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading the Finix fees");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading the Finix fees"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading the Finix fees"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading the Finix fees"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading the Finix fees"); }
    }

    public async Task<FeeRead> GetRefundedAsync(string captureTransferId, CancellationToken cancellationToken)
    {
        long total = 0;
        string? cursor = null;
        var pages = 0;

        try
        {
            while (true)
            {
                var page = await BoundedAsync(
                    ct => _client.TransfersApi.ListTransferReversals(
                        new ListTransferReversalsRequest
                        {
                            TransferId = captureTransferId,
                            Limit = PageLimit,
                            AfterCursor = cursor
                        },
                        cancellationToken: ct),
                    cancellationToken);

                if (page.Embedded?.Transfers is not null)
                {
                    foreach (var reversal in page.Embedded.Transfers)
                    {
                        if (reversal.State == State11.Failed || reversal.State == State11.Canceled)
                            continue;
                        total += reversal.Amount ?? 0;
                    }
                }

                if ((page.Embedded?.Transfers?.Count ?? 0) == 0)
                    return new FeeRead(total, false);

                var next = page.Page?.NextCursor;
                if (string.IsNullOrEmpty(next) || next == cursor)
                    return new FeeRead(total, false);

                if (++pages >= MaxPages)
                    return new FeeRead(total, true);

                cursor = next;
            }
        }
        catch (ApiException<ListTransferReversalsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading the refunds");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading the refunds"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading the refunds"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading the refunds"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading the refunds"); }
    }

    public async Task<ReconciliationRead> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var transactions = new List<FinixTransactionInfo>();
        var truncated = false;

        truncated |= await ReadTransferPagesAsync(from, to, transactions, cancellationToken);
        truncated |= await ReadAuthorizationPagesAsync(from, to, transactions, cancellationToken);

        return new ReconciliationRead(
            transactions.OrderBy(t => t.CreatedAt ?? DateTimeOffset.MinValue).ToArray(),
            truncated);
    }

    private async Task<bool> ReadTransferPagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<FinixTransactionInfo> transactions,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        var pages = 0;

        try
        {
            while (true)
            {
                var page = await BoundedAsync(
                    ct => _client.TransfersApi.ListTransfers(
                        new ListTransfersRequest
                        {
                            CreatedAtGte = WholeSeconds(from),
                            CreatedAtLte = WholeSeconds(to),
                            Merchant = _options.MerchantId,
                            Limit = PageLimit,
                            AfterCursor = cursor
                        },
                        cancellationToken: ct),
                    cancellationToken);

                if (page.Embedded?.Transfers is not null)
                {
                    foreach (var transfer in page.Embedded.Transfers)
                    {
                        transactions.Add(new FinixTransactionInfo(
                            transfer.Id ?? string.Empty,
                            "transfer",
                            transfer.Type?.Value,
                            transfer.State?.Value,
                            transfer.Amount ?? 0,
                            transfer.Currency?.Value,
                            transfer.CreatedAt,
                            transfer.IdempotencyId,
                            transfer.ParentTransfer,
                            transfer.Id));
                    }
                }

                if ((page.Embedded?.Transfers?.Count ?? 0) == 0)
                    return false;

                var next = page.Page?.NextCursor;
                if (string.IsNullOrEmpty(next) || next == cursor)
                    return false;

                if (++pages >= MaxPages)
                    return true;

                cursor = next;
            }
        }
        catch (ApiException<ListTransfersError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading Finix transfers");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading Finix transfers"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading Finix transfers"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading Finix transfers"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading Finix transfers"); }
    }

    private async Task<bool> ReadAuthorizationPagesAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        List<FinixTransactionInfo> transactions,
        CancellationToken cancellationToken)
    {
        string? cursor = null;
        var pages = 0;

        try
        {
            while (true)
            {
                var page = await BoundedAsync(
                    ct => _client.AuthorizationsApi.ListAuthorizations(
                        new ListAuthorizationsRequest
                        {
                            CreatedAtGte = WholeSeconds(from),
                            CreatedAtLte = WholeSeconds(to),
                            Merchant = _options.MerchantId,
                            Limit = PageLimit,
                            AfterCursor = cursor
                        },
                        cancellationToken: ct),
                    cancellationToken);

                if (page.Embedded?.Authorizations is not null)
                {
                    foreach (var authorization in page.Embedded.Authorizations)
                    {
                        transactions.Add(new FinixTransactionInfo(
                            authorization.Id ?? string.Empty,
                            "authorization",
                            "AUTHORIZATION",
                            authorization.State?.Value,
                            authorization.Amount ?? 0,
                            authorization.Currency?.Value,
                            authorization.CreatedAt,
                            authorization.IdempotencyId,
                            null,
                            authorization.Transfer));
                    }
                }

                if ((page.Embedded?.Authorizations?.Count ?? 0) == 0)
                    return false;

                var next = page.Page?.NextCursor;
                if (string.IsNullOrEmpty(next) || next == cursor)
                    return false;

                if (++pages >= MaxPages)
                    return true;

                cursor = next;
            }
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw Translate(ex, generic, "Reading Finix authorizations");
        }
        catch (ResponseDeserializationException ex) { throw TranslateDeserialization(ex, "Reading Finix authorizations"); }
        catch (SdkTimeoutException ex) { throw TranslateTransport(ex, "Reading Finix authorizations"); }
        catch (SdkConnectionException ex) { throw TranslateTransport(ex, "Reading Finix authorizations"); }
        catch (AuthSchemeException ex) { throw TranslateAuth(ex, "Reading Finix authorizations"); }
    }

    private async Task<AuthorizationInfo?> TryFindAuthorizationAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await BoundedAsync(
                ct => _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        CreatedAtGte = WholeSeconds(DateTimeOffset.UtcNow.AddDays(-1)),
                        CreatedAtLte = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(5)),
                        Limit = 10
                    },
                    cancellationToken: ct),
                cancellationToken);

            var match = page.Embedded?.Authorizations?
                .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));

            return match is null ? null : Map(match);
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the authorization outcome for idempotency id {IdempotencyId}.", idempotencyId);
            return null;
        }
    }

    private async Task<InstrumentInfo?> TryFindInstrumentAsync(
        string identityId,
        string cardNumber,
        CancellationToken cancellationToken)
    {
        var last4 = LastFour(cardNumber);
        try
        {
            var page = await BoundedAsync(
                ct => _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        OwnerIdentityId = identityId,
                        Last4 = last4,
                        CreatedAtGte = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-10)),
                        Limit = 10
                    },
                    cancellationToken: ct),
                cancellationToken);

            var match = page.Embedded?.PaymentInstruments?
                .Select(TryMapInstrument)
                .FirstOrDefault(i => i is not null);

            return match;
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the payment instrument outcome.");
            return null;
        }
    }

    private async Task<bool> IsInstrumentDisabledAsync(string instrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await BoundedAsync(
                ct => _client.PaymentInstrumentsApi.GetPaymentInstrument(
                    new GetPaymentInstrumentRequest { PaymentInstrumentId = instrumentId },
                    cancellationToken: ct),
                cancellationToken);

            return response.TryGetPaymentInstrumentCard(out var instrument) && instrument?.Enabled == false;
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the disable-instrument outcome for {InstrumentId}.", instrumentId);
            return false;
        }
    }

    private async Task<CaptureInfo?> TrySettleCaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorization.TransferId))
                return null;

            var transfer = await GetTransferAsync(authorization.TransferId, cancellationToken);
            var fees = await GetFeesAsync(authorization.TransferId, cancellationToken);
            var captured = transfer.AmountCents > 0 ? transfer.AmountCents : amountCents;

            return new CaptureInfo(
                authorizationId,
                authorization.State,
                authorization.TransferId,
                transfer.State,
                captured,
                fees.Cents,
                captured - fees.Cents,
                fees.Truncated);
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the capture outcome for {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<VoidInfo?> TrySettleVoidAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await GetAuthorizationAsync(authorizationId, cancellationToken);
            if (authorization.IsVoid || authorization.VoidState == VoidState.Succeeded.Value)
            {
                return new VoidInfo(
                    authorizationId,
                    authorization.State,
                    true,
                    authorization.VoidState);
            }

            return null;
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the void outcome for {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<RefundInfo?> TryFindRefundAsync(
        string captureTransferId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await BoundedAsync(
                ct => _client.TransfersApi.ListTransfers(
                    new ListTransfersRequest
                    {
                        ParentTransferId = captureTransferId,
                        IdempotencyId = idempotencyKey,
                        CreatedAtGte = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-10)),
                        Limit = 10
                    },
                    cancellationToken: ct),
                cancellationToken);

            var match = page.Embedded?.Transfers?
                .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyKey, StringComparison.Ordinal));

            return match?.Id is null
                ? null
                : new RefundInfo(match.Id, match.State?.Value, match.Amount ?? 0);
        }
        catch (Exception ex) when (ex is SdkException or PaymentException)
        {
            _logger.LogWarning(ex, "Could not settle the refund outcome for idempotency key {IdempotencyKey}.", idempotencyKey);
            return null;
        }
    }

    private async Task<T> BoundedAsync<T>(
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static DateTimeOffset WholeSeconds(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, utc.Second, TimeSpan.Zero);
    }

    private static InstrumentInfo Map(PaymentInstrumentCard instrument) =>
        new(
            instrument.Id ?? string.Empty,
            instrument.Brand?.Value,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Fingerprint,
            instrument.Enabled ?? true);

    private static InstrumentInfo? TryMapInstrument(PaymentInstruments3 response) =>
        response.TryGetPaymentInstrumentCard(out var instrument) && instrument is not null
            ? Map(instrument)
            : null;

    private static AuthorizationInfo Map(Authorization authorization) =>
        new(
            authorization.Id ?? string.Empty,
            authorization.State?.Value,
            authorization.IsVoid ?? false,
            authorization.VoidState?.Value,
            authorization.Amount ?? 0,
            authorization.Transfer,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.ExpiresAt);

    private static TransferInfo Map(Transfer transfer) =>
        new(
            transfer.Id ?? string.Empty,
            transfer.State?.Value,
            transfer.Amount ?? 0,
            transfer.Type?.Value,
            transfer.ParentTransfer,
            transfer.IdempotencyId);

    private static bool LooksLikeEmail(string value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 100 && value.Contains('@');

    private static string LastFour(string number) =>
        number.Length <= 4 ? number : number[^4..];

    private static PaymentException Translate(
        ApiException exception,
        GenericError? genericError,
        string action)
    {
        var first = genericError?.Embedded?.Errors?.FirstOrDefault();
        var code = string.IsNullOrWhiteSpace(first?.Code) ? "PROVIDER_ERROR" : first!.Code!;
        var detail = string.IsNullOrWhiteSpace(first?.Message) ? exception.Message : first!.Message!;
        var status = MapStatus((int)exception.StatusCode);

        return new PaymentException(
            status,
            code,
            $"{action} failed: {detail}",
            exception,
            providerLogref: first?.Logref,
            providerStatusCode: (int)exception.StatusCode);
    }

    private static PaymentException TranslateDeserialization(
        ResponseDeserializationException exception,
        string action)
    {
        var unknown = (int)exception.StatusCode is >= 200 and < 300;
        return new PaymentException(
            502,
            "PROVIDER_BAD_RESPONSE",
            $"{action} failed: Finix returned a response that could not be read.",
            exception,
            unknown);
    }

    private static PaymentException TranslateTransport(SdkConnectionException exception, string action)
    {
        if (exception is SdkTimeoutException timeout)
        {
            return new PaymentException(
                502,
                "PROVIDER_TIMEOUT",
                $"{action} failed: Finix did not answer within {timeout.Timeout.TotalSeconds:0} seconds. " +
                "The outcome is unknown; reconcile before retrying.",
                exception,
                unknownOutcome: true);
        }

        return new PaymentException(
            502,
            "PROVIDER_UNREACHABLE",
            $"{action} failed: Finix could not be reached. The outcome is unknown; reconcile before retrying.",
            exception,
            unknownOutcome: true);
    }

    private static PaymentException TranslateAuth(AuthSchemeException exception, string action) =>
        new(
            502,
            "PROVIDER_AUTH",
            $"{action} failed: the Finix credentials are missing or were rejected.",
            exception);

    private static int MapStatus(int providerStatus)
    {
        if (providerStatus is 401 or 403) return 502;
        if (providerStatus == 429) return 503;
        if (providerStatus >= 500) return 502;
        if (providerStatus is >= 400 and < 500) return providerStatus;
        return 502;
    }
}
