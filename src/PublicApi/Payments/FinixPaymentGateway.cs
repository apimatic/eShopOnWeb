using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Logging;
using FinixBuyer = FinixApiReference.Models.Buyer;
using FinixCard = FinixApiReference.Models.PaymentInstrumentCard;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public class FinixPaymentGateway : IFinixPaymentGateway
{
    public const string HttpClientName = "Finix";

    private const int MaxListPages = 50;
    private const int PageSize = 100;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;
    private readonly Currency _currency;

    public FinixPaymentGateway(
        FinixApiReferenceClient client,
        FinixOptions options,
        ILogger<FinixPaymentGateway> logger)
    {
        _client = client;
        _options = options;
        _logger = logger;
        _currency = Currency.TryGetKnownValue(options.Currency.Trim().ToUpperInvariant(), out var currency)
            ? currency!
            : throw new InvalidOperationException("Finix:Currency is not a currency code this SDK declares (USD or CAD).");
    }

    public Task<string> EnsureShopperIdentityAsync(string buyerId, string? email, CancellationToken cancellationToken) =>
        ExecuteAsync<string, CreateIdentityError>(
            "creating the shopper identity",
            async ct =>
            {
                var body = IdentitiesRequest.Buyer(new FinixBuyer
                {
                    Entity = new Entity5
                    {
                        Email = string.IsNullOrWhiteSpace(email) ? null : email
                    }
                });

                var identity = await _client.Identities.CreateIdentity(
                    new CreateIdentityRequest { Body = body },
                    cancellationToken: ct);

                if (string.IsNullOrWhiteSpace(identity.Id))
                {
                    throw new PaymentOperationException("Finix did not return an identity id.", 502);
                }

                return identity.Id!;
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<string?> FindIdentityByEmailAsync(string email, CancellationToken cancellationToken) =>
        ExecuteAsync<string?, ListIdentitiesError>(
            "finding the shopper identity",
            async ct =>
            {
                var response = await _client.Identities.ListIdentities(
                    new ListIdentitiesRequest { Email = email, Limit = PageSize },
                    cancellationToken: ct);

                return response.Embedded?.Identities?.FirstOrDefault()?.Id;
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixCardInfo> SaveCardAsync(string identityId, FinixCardInput card, CancellationToken cancellationToken) =>
        ExecuteAsync<FinixCardInfo, CreatePaymentInstrumentError>(
            "saving the card",
            async ct =>
            {
                var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                {
                    Identity = identityId,
                    Name = card.Name,
                    Number = card.Number,
                    ExpirationMonth = card.ExpirationMonth,
                    ExpirationYear = card.ExpirationYear,
                    SecurityCode = card.SecurityCode,
                    Address = BuildAddress(card)
                });

                var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                    new CreatePaymentInstrumentRequest { Body = body },
                    cancellationToken: ct);

                if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
                    string.IsNullOrWhiteSpace(instrument.Id))
                {
                    throw new PaymentOperationException("Finix did not return a saved card identifier.", 502);
                }

                return MapCard(instrument);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<IReadOnlyList<FinixCardInfo>> ListCardsAsync(string identityId, CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<FinixCardInfo>, ListPaymentInstrumentsError>(
            "listing saved cards",
            async ct =>
            {
                var cards = new List<FinixCardInfo>();
                string? cursor = null;

                for (var page = 0; page < MaxListPages; page++)
                {
                    var response = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                        new ListPaymentInstrumentsRequest
                        {
                            OwnerIdentityId = identityId,
                            AfterCursor = cursor,
                            Limit = PageSize
                        },
                        cancellationToken: ct);

                    if (response.Embedded?.PaymentInstruments is { } instruments)
                    {
                        foreach (var union in instruments)
                        {
                            if (union.TryGetPaymentInstrumentCard(out var instrument) &&
                                !string.IsNullOrWhiteSpace(instrument.Id))
                            {
                                cards.Add(MapCard(instrument));
                            }
                        }
                    }

                    cursor = response.Page?.NextCursor;
                    if (string.IsNullOrWhiteSpace(cursor))
                    {
                        break;
                    }
                }

                return cards;
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public async Task<bool> CardBelongsToIdentityAsync(string identityId, string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            return await ExecuteAsync<bool, GetPaymentInstrumentError>(
                "reading the saved card",
                async ct =>
                {
                    var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                        new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                        cancellationToken: ct);

                    return response.TryGetPaymentInstrumentCard(out var card) &&
                           string.Equals(card.Id, paymentInstrumentId, StringComparison.Ordinal) &&
                           string.Equals(card.Identity, identityId, StringComparison.Ordinal);
                },
                error => error.TryGetGenericError(out var generic) ? generic : null,
                cancellationToken);
        }
        catch (PaymentOperationException exception) when (exception.StatusCode == 400)
        {
            return false;
        }
    }

    public async Task<FinixCardInfo?> FindCardAsync(
        string identityId,
        string last4,
        int expirationMonth,
        int expirationYear,
        CancellationToken cancellationToken)
    {
        var cards = await ListCardsAsync(identityId, cancellationToken);
        return cards.FirstOrDefault(card =>
            string.Equals(card.Last4, last4, StringComparison.Ordinal) &&
            card.ExpirationMonth == expirationMonth &&
            card.ExpirationYear == expirationYear);
    }

    public Task<FinixAuthorizationResult> AuthorizeAsync(
        string paymentInstrumentId,
        long amountCents,
        string idempotencyId,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixAuthorizationResult, CreateAuthorizationError>(
            "authorizing the order",
            async ct =>
            {
                _logger.LogInformation(
                    "Authorizing {AmountCents} cents with Finix instrument {InstrumentId}.",
                    amountCents,
                    paymentInstrumentId);

                var body = new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = _currency,
                    Merchant = _options.MerchantId,
                    Source = paymentInstrumentId,
                    IdempotencyId = idempotencyId
                };

                var authorization = await _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest { Body = body },
                    cancellationToken: ct);

                return MapAuthorization(authorization);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixAuthorizationResult?, ListAuthorizationsError>(
            "looking up the authorization",
            async ct =>
            {
                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        Merchant = _options.MerchantId,
                        Limit = PageSize
                    },
                    cancellationToken: ct);

                var authorization = response.Embedded?.Authorizations?.FirstOrDefault();
                return authorization is null ? null : MapAuthorization(authorization);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixAuthorizationResult, GetAuthorizationError>(
            "reading the authorization",
            async ct =>
            {
                var authorization = await _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = authorizationId },
                    cancellationToken: ct);

                return MapAuthorization(authorization);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixCaptureResult> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixCaptureResult, CaptureAuthorizationError>(
            "capturing the authorization",
            async ct =>
            {
                var body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountCents
                });

                var captured = await _client.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = authorizationId,
                        Body = body
                    },
                    cancellationToken: ct);

                var transferId = captured.Transfer;
                if (string.IsNullOrWhiteSpace(transferId))
                {
                    var refreshed = await GetAuthorizationCoreAsync(authorizationId, ct);
                    transferId = refreshed.Transfer;
                }

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    throw new PaymentOperationException(
                        "Finix captured the authorization but did not return the resulting transfer id.",
                        502);
                }

                var transfer = await WaitForTransferSettledAsync(transferId, ct);
                var capturedAmount = transfer.Amount ?? amountCents;
                var fee = transfer.Fee ?? 0;

                _logger.LogWarning(
                    "Finix capture transfer {TransferId} is {State} with amount {AmountCents} and fee {FeeCents}.",
                    transferId,
                    transfer.State?.Value,
                    capturedAmount,
                    fee);

                return new FinixCaptureResult(
                    captured.Id ?? authorizationId,
                    captured.State?.Value,
                    transferId,
                    transfer.State?.Value,
                    capturedAmount,
                    fee,
                    capturedAmount - fee);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixTransaction?> GetTransferAsync(string transferId, CancellationToken cancellationToken) =>
        ExecuteAsync<FinixTransaction?, GetTransferError>(
            "reading the capture",
            async ct =>
            {
                var transfer = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: ct);

                return MapTransaction(transfer);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken,
            TimeSpan.FromSeconds(60));

    public Task<FinixVoidResult> VoidAsync(string authorizationId, CancellationToken cancellationToken) =>        ExecuteAsync<FinixVoidResult, VoidAuthorizationError>(
            "voiding the authorization",
            async ct =>
            {
                var authorization = await _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = authorizationId,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: ct);

                return new FinixVoidResult(
                    authorization.Id ?? authorizationId,
                    authorization.State?.Value,
                    authorization.IsVoid ?? false,
                    authorization.VoidState?.Value);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixRefundResult> RefundAsync(
        string transferId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixRefundResult, CreateTransferReversalError>(
            "refunding the capture",
            async ct =>
            {
                if (amountCents > int.MaxValue)
                {
                    throw new PaymentOperationException("The refund amount is too large.", 400);
                }

                var captured = await WaitForTransferSettledAsync(transferId, ct);
                if (captured.State != State11.Succeeded)
                {
                    throw new PaymentOperationException(
                        $"The capture is not refundable yet (Finix transfer state {captured.State?.Value ?? "unknown"}).",
                        409);
                }

                var body = new RefundTransfer { RefundAmount = (int)amountCents };
                body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

                var reversal = await _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = transferId,
                        Body = TransfersReversalsRequest.RefundTransfer(body)
                    },
                    cancellationToken: ct);

                if (string.IsNullOrWhiteSpace(reversal.Id))
                {
                    throw new PaymentOperationException("Finix did not return a refund identifier.", 502);
                }

                return new FinixRefundResult(
                    reversal.Id!,
                    reversal.Amount ?? amountCents,
                    reversal.State?.Value,
                    reversal.ParentTransfer);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken,
            TimeSpan.FromSeconds(60));

    public Task<IReadOnlyList<FinixTransaction>> ListReversalsAsync(
        string transferId,
        CancellationToken cancellationToken) =>
        ExecuteAsync<IReadOnlyList<FinixTransaction>, ListTransferReversalsError>(
            "listing refunds",
            async ct =>
            {
                var reversals = new List<FinixTransaction>();
                string? cursor = null;

                for (var page = 0; page < MaxListPages; page++)
                {
                    var response = await _client.TransfersApi.ListTransferReversals(
                        new ListTransferReversalsRequest
                        {
                            TransferId = transferId,
                            AfterCursor = cursor,
                            Limit = PageSize
                        },
                        cancellationToken: ct);

                    if (response.Embedded?.Transfers is { } transfers)
                    {
                        reversals.AddRange(transfers.Select(MapTransaction));
                    }

                    cursor = response.Page?.NextCursor;
                    if (string.IsNullOrWhiteSpace(cursor))
                    {
                        break;
                    }
                }

                return reversals;
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    public Task<FinixTransactionPage> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FinixTransactionPage, ListTransfersError>(
            "listing transactions for reconciliation",
            async ct =>
            {
                var transactions = new List<FinixTransaction>();
                string? cursor = null;
                var truncated = false;
                DateTimeOffset? coveredTo = null;

                for (var page = 0; page < MaxListPages; page++)
                {
                    var response = await _client.TransfersApi.ListTransfers(
                        new ListTransfersRequest
                        {
                            CreatedAtGte = from,
                            CreatedAtLte = to,
                            Merchant = _options.MerchantId,
                            AfterCursor = cursor,
                            Limit = PageSize
                        },
                        cancellationToken: ct);

                    if (response.Embedded?.Transfers is { } transfers)
                    {
                        foreach (var transfer in transfers)
                        {
                            var mapped = MapTransaction(transfer);
                            transactions.Add(mapped);
                            if (mapped.CreatedAt is { } createdAt &&
                                (coveredTo is null || createdAt > coveredTo))
                            {
                                coveredTo = createdAt;
                            }
                        }
                    }

                    cursor = response.Page?.NextCursor;
                    if (string.IsNullOrWhiteSpace(cursor))
                    {
                        break;
                    }

                    if (page == MaxListPages - 1)
                    {
                        truncated = true;
                    }
                }

                return new FinixTransactionPage(transactions, truncated, cursor, coveredTo);
            },
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken,
            TimeSpan.FromSeconds(60));

    private async Task<Authorization> GetAuthorizationCoreAsync(string authorizationId, CancellationToken cancellationToken) =>
        await ExecuteAsync<Authorization, GetAuthorizationError>(
            "reading the authorization",
            ct => _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: ct),
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    private async Task<Transfer> GetTransferCoreAsync(string transferId, CancellationToken cancellationToken) =>
        await ExecuteAsync<Transfer, GetTransferError>(
            "reading the capture",
            ct => _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: ct),
            error => error.TryGetGenericError(out var generic) ? generic : null,
            cancellationToken);

    private async Task<Transfer> WaitForTransferSettledAsync(string transferId, CancellationToken cancellationToken)
    {
        var transfer = await GetTransferCoreAsync(transferId, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);

        while (transfer.State == State11.Pending && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await GetTransferCoreAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<T> ExecuteAsync<T, TError>(
        string operation,
        Func<CancellationToken, Task<T>> call,
        Func<TError, GenericError?> genericError,
        CancellationToken outerToken,
        TimeSpan? budget = null)
        where TError : ApiError
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        cts.CancelAfter(budget ?? TimeSpan.FromSeconds(20));

        try
        {
            return await call(cts.Token);
        }
        catch (ApiException<TError> ex)
        {
            throw Translate(ex, genericError(ex.Error), operation);
        }
        catch (ResponseDeserializationException ex)
        {
            throw TranslateDeserialization(ex, operation);
        }
        catch (SdkTimeoutException ex)
        {
            throw TranslateConnection(ex, operation);
        }
        catch (SdkConnectionException ex)
        {
            throw TranslateConnection(ex, operation);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are missing or rejected while {Operation}.", operation);
            throw new PaymentOperationException(
                "Finix credentials are missing or were rejected. Check Finix:Username and Finix:Password.",
                502,
                innerException: ex);
        }
        catch (OperationCanceledException ex) when (!outerToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Finix did not answer within the payment budget while {Operation}.", operation);
            throw new PaymentOperationException(
                "Finix did not answer within the payment budget; the outcome is unknown.",
                504,
                outcomeUnknown: true,
                innerException: ex);
        }
    }

    private PaymentOperationException Translate<TError>(
        ApiException<TError> exception,
        GenericError? genericError,
        string operation)
        where TError : ApiError
    {
        if (genericError is null && exception.Error.TryGetRawError(out var rawError))
        {
            try
            {
                genericError = rawError.ReadAsJson<GenericError>();
            }
            catch (JsonException)
            {
                // The provider returned a non-GenericError body; the typed error is still usable.
            }
        }

        var first = genericError?.Embedded?.Errors?.FirstOrDefault();
        var status = (int)exception.StatusCode;
        var clientStatus = status switch
        {
            400 or 404 or 406 or 422 => 400,
            402 => 402,
            409 => 409,
            401 or 403 => 502,
            429 => 503,
            >= 500 => 502,
            _ => 502
        };
        var message = RedactCardData(!string.IsNullOrWhiteSpace(first?.Message)
            ? first!.Message!
            : $"Finix rejected the request while {operation} (HTTP {status}).");
        var duplicate = ContainsIdempotency(first?.Code) || ContainsIdempotency(first?.Message);

        _logger.LogWarning(
            "Finix {Operation} failed with HTTP {Status}, code {Code}, logref {LogRef}.",
            operation,
            status,
            first?.Code,
            first?.Logref);

        return new PaymentOperationException(
            message,
            clientStatus,
            first?.Code,
            first?.Logref,
            duplicateIdempotency: duplicate,
            innerException: exception);
    }

    private PaymentOperationException TranslateDeserialization(
        ResponseDeserializationException exception,
        string operation)
    {
        var status = (int)exception.StatusCode;
        var successStatus = status is >= 200 and < 300;

        _logger.LogError(
            exception,
            "Finix returned an unreadable body while {Operation} (HTTP {Status}).",
            operation,
            status);

        return new PaymentOperationException(
            successStatus
                ? "Finix returned a response that could not be read; the outcome is unknown."
                : "Finix returned an error response that could not be read.",
            successStatus ? 504 : 400,
            outcomeUnknown: successStatus,
            innerException: exception);
    }

    private PaymentOperationException TranslateConnection(SdkException exception, string operation)
    {
        _logger.LogWarning(exception, "Finix did not answer while {Operation}.", operation);
        return new PaymentOperationException(
            "Finix did not answer; the outcome is unknown. Reconcile before retrying with a new reference.",
            504,
            outcomeUnknown: true,
            innerException: exception);
    }

    private static bool ContainsIdempotency(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains("idempoten", StringComparison.OrdinalIgnoreCase);

    private static string RedactCardData(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\b\d{12,19}\b", "***");

    private static FinixAuthorizationResult MapAuthorization(Authorization authorization) =>
        new(
            authorization.Id ?? string.Empty,
            authorization.State?.Value,
            authorization.Amount ?? 0,
            authorization.Transfer,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.IsVoid,
            authorization.ExpiresAt);

    private static FinixTransaction MapTransaction(Transfer transfer) =>
        new(
            transfer.Id ?? string.Empty,
            transfer.Type?.Value,
            transfer.State?.Value,
            transfer.Amount,
            transfer.Fee,
            transfer.CreatedAt,
            transfer.ParentTransfer,
            transfer.IdempotencyId);

    private static FinixCardInfo MapCard(FinixCard instrument) =>
        new(
            instrument.Id ?? string.Empty,
            instrument.Brand?.Value,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Fingerprint);

    private static FinixApiReference.Models.Address? BuildAddress(FinixCardInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Street) ||
            string.IsNullOrWhiteSpace(card.City) ||
            string.IsNullOrWhiteSpace(card.State) ||
            string.IsNullOrWhiteSpace(card.ZipCode) ||
            string.IsNullOrWhiteSpace(card.Country))
        {
            return null;
        }

        var country = ResolveCountry(card.Country!);
        if (country is null)
        {
            return null;
        }

        return new FinixApiReference.Models.Address
        {
            Line1 = card.Street,
            City = card.City,
            Region = card.State,
            PostalCode = card.ZipCode,
            Country = country
        };
    }

    private static Country? ResolveCountry(string code)
    {
        var normalized = code.Trim().ToUpperInvariant();
        if (Country.TryGetKnownValue(normalized, out var known))
        {
            return known;
        }

        if (normalized.Length == 2)
        {
            try
            {
                var threeLetter = new RegionInfo(normalized).ThreeLetterISORegionName;
                if (Country.TryGetKnownValue(threeLetter, out known))
                {
                    return known;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        return null;
    }
}
