using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Authorization = FinixApiReference.Models.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The only place the application talks to Finix. Every call is bounded by a total 30 second
/// budget, every provider failure is translated into <see cref="PaymentProviderException"/>, and
/// no card field is ever logged.
/// </summary>
public sealed class FinixGateway
{
    private const int MaxPageSize = 100;
    private const int MaxPages = 1000;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixGateway> _logger;
    private readonly Currency _currency;
    private readonly TimeSpan _callBudget = TimeSpan.FromSeconds(30);

    public FinixGateway(
        FinixApiReferenceClient client,
        IOptions<FinixOptions> options,
        ILogger<FinixGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;

        if (!FinixApiReference.Models.Enums.Currency.TryGetKnownValue(_options.Currency, out var currency))
        {
            throw new InvalidOperationException(
                "Finix:Currency must be one of the currencies this integration supports (USD or CAD).");
        }

        _currency = currency;
    }

    public string Currency => _currency.Value;
    public string MerchantId => _options.MerchantId;

    public Task<string> CreateBuyerIdentityAsync(
        string email,
        string? firstName,
        string? lastName,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var body = IdentitiesRequest.Buyer(new Buyer
                {
                    Entity = new Entity5
                    {
                        Email = email,
                        FirstName = firstName,
                        LastName = lastName
                    },
                    Type = Type10.Personal,
                    IdentityRoles = new[] { IdentityRole5.Buyer }
                });

                var response = await _client.Identities.CreateIdentity(
                    new CreateIdentityRequest { Body = body },
                    cancellationToken: token);

                if (string.IsNullOrWhiteSpace(response.Id))
                {
                    throw new PaymentProviderException(
                        "Finix created an identity but did not return its id.",
                        HttpStatusCode.BadGateway);
                }

                return response.Id!;
            }
            catch (ApiException<CreateIdentityError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<string?> FindBuyerIdentityAsync(string email, CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.Identities.ListIdentities(
                    new ListIdentitiesRequest { Email = email, Limit = 10 },
                    cancellationToken: token);

                return response.Embedded?.Identities?
                    .FirstOrDefault(identity => string.Equals(identity.Entity?.Email, email, StringComparison.OrdinalIgnoreCase))?
                    .Id;
            }
            catch (ApiException<ListIdentitiesError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixInstrumentResult> CreatePaymentInstrumentAsync(
        string identityId,
        FinixCardDetails card,
        string? idempotencyTag,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                {
                    Identity = identityId,
                    Name = card.Name,
                    Number = card.Number,
                    ExpirationMonth = card.ExpirationMonth,
                    ExpirationYear = card.ExpirationYear,
                    SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode,
                    Tags = string.IsNullOrWhiteSpace(idempotencyTag)
                        ? null
                        : new Dictionary<string, string> { ["app_save_key"] = idempotencyTag }
                });

                var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                    new CreatePaymentInstrumentRequest { Body = body },
                    cancellationToken: token);

                if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
                    string.IsNullOrWhiteSpace(instrument?.Id))
                {
                    throw new PaymentProviderException(
                        "Finix created a payment instrument but did not return its card details.",
                        HttpStatusCode.BadGateway);
                }

                return MapInstrument(instrument!);
            }
            catch (ApiException<CreatePaymentInstrumentError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixInstrumentResult> GetPaymentInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                    new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                    cancellationToken: token);

                if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
                    string.IsNullOrWhiteSpace(instrument?.Id))
                {
                    throw new PaymentProviderException(
                        "Finix returned a payment instrument that was not a card.",
                        HttpStatusCode.BadGateway);
                }

                return MapInstrument(instrument!);
            }
            catch (ApiException<GetPaymentInstrumentError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task DisablePaymentInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                    new PaymentInstrumentDisableAPaymentInstrument { Enabled = false });

                await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                    new UpdatePaymentInstrumentRequest
                    {
                        PaymentInstrumentId = paymentInstrumentId,
                        Body = body
                    },
                    cancellationToken: token);

                return true;
            }
            catch (ApiException<UpdatePaymentInstrumentError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixPage<FinixInstrumentResult>> ListPaymentInstrumentsAsync(
        string identityId,
        string? tagsKey,
        string? tagsValue,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            var items = new List<FinixInstrumentResult>();
            string? cursor = null;
            var pages = 0;
            var truncated = false;

            do
            {
                try
                {
                    var response = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                        new ListPaymentInstrumentsRequest
                        {
                            OwnerIdentityId = identityId,
                            TagsKey = tagsKey,
                            TagsValue = tagsValue,
                            AfterCursor = cursor,
                            Limit = MaxPageSize
                        },
                        cancellationToken: token);

                    foreach (var entry in response.Embedded?.PaymentInstruments ?? [])
                    {
                        if (entry.TryGetPaymentInstrumentCard(out var card) && card is not null)
                        {
                            items.Add(MapInstrument(card));
                        }
                    }

                    cursor = response.Page?.NextCursor;
                    if (++pages >= MaxPages && !string.IsNullOrEmpty(cursor))
                    {
                        truncated = true;
                        break;
                    }
                }
                catch (ApiException<ListPaymentInstrumentsError> ex)
                {
                    throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            } while (!string.IsNullOrEmpty(cursor));

            return new FinixPage<FinixInstrumentResult>(items, cursor, truncated);
        }, cancellationToken);

    public Task<FinixAuthorizationResult> CreateAuthorizationAsync(
        long amountCents,
        string sourcePaymentInstrumentId,
        string idempotencyId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = _currency,
                    Merchant = _options.MerchantId,
                    Source = sourcePaymentInstrumentId,
                    IdempotencyId = idempotencyId,
                    PartialAuthorization = false
                });

                var response = await _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest { Body = body },
                    cancellationToken: token);

                return MapAuthorization(response);
            }
            catch (ApiException<CreateAuthorizationError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixAuthorizationResult> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = authorizationId },
                    cancellationToken: token);

                return MapAuthorization(response);
            }
            catch (ApiException<GetAuthorizationError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixAuthorizationResult> CaptureAuthorizationAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountCents
                });

                var response = await _client.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = authorizationId,
                        Body = body
                    },
                    cancellationToken: token);

                return MapAuthorization(response);
            }
            catch (ApiException<CaptureAuthorizationError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixAuthorizationResult> VoidAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = authorizationId,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: token);

                return MapAuthorization(response);
            }
            catch (ApiException<VoidAuthorizationError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixAuthorizationResult?> FindAuthorizationByIdempotencyIdAsync(
        string idempotencyId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest { IdempotencyId = idempotencyId, Limit = MaxPageSize },
                    cancellationToken: token);

                var authorization = response.Embedded?.Authorizations?
                    .FirstOrDefault(item => string.Equals(item.IdempotencyId, idempotencyId, StringComparison.Ordinal));

                return authorization is null ? null : MapAuthorization(authorization);
            }
            catch (ApiException<ListAuthorizationsError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixTransferResult> GetTransferAsync(
        string transferId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            try
            {
                var response = await _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: token);

                return MapTransfer(response);
            }
            catch (ApiException<GetTransferError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixTransferResult> CreateRefundAsync(
        string captureTransferId,
        long amountCents,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            if (amountCents > int.MaxValue)
            {
                throw new PaymentValidationException("The refund amount is larger than Finix accepts for a single reversal.");
            }

            try
            {
                var body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
                {
                    RefundAmount = (int)amountCents
                });

                var response = await _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = captureTransferId,
                        Body = body
                    },
                    cancellationToken: token);

                return MapTransfer(response);
            }
            catch (ApiException<CreateTransferReversalError> ex)
            {
                throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
            }
        }, cancellationToken);

    public Task<FinixPage<FinixTransferResult>> ListRefundsAsync(
        string captureTransferId,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            var items = new List<FinixTransferResult>();
            string? cursor = null;
            var pages = 0;
            var truncated = false;

            do
            {
                try
                {
                    var response = await _client.TransfersApi.ListTransferReversals(
                        new ListTransferReversalsRequest
                        {
                            TransferId = captureTransferId,
                            AfterCursor = cursor,
                            Limit = MaxPageSize
                        },
                        cancellationToken: token);

                    foreach (var transfer in response.Embedded?.Transfers ?? [])
                    {
                        items.Add(MapTransfer(transfer));
                    }

                    cursor = response.Page?.NextCursor;
                    if (++pages >= MaxPages && !string.IsNullOrEmpty(cursor))
                    {
                        truncated = true;
                        break;
                    }
                }
                catch (ApiException<ListTransferReversalsError> ex)
                {
                    throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            } while (!string.IsNullOrEmpty(cursor));

            return new FinixPage<FinixTransferResult>(items, cursor, truncated);
        }, cancellationToken);

    public Task<FinixFeeResult> GetFeesAsync(string captureTransferId, CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            var fees = new List<Fee>();
            string? cursor = null;
            var pages = 0;
            var truncated = false;

            do
            {
                try
                {
                    var response = await _client.FeesApi.ListFees(
                        new ListFeesRequest
                        {
                            LinkedTo = captureTransferId,
                            AfterCursor = cursor,
                            Limit = MaxPageSize
                        },
                        cancellationToken: token);

                    fees.AddRange(response.Embedded?.Fees ?? []);

                    cursor = response.Page?.NextCursor;
                    if (++pages >= MaxPages && !string.IsNullOrEmpty(cursor))
                    {
                        truncated = true;
                        break;
                    }
                }
                catch (ApiException<ListFeesError> ex)
                {
                    throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            } while (!string.IsNullOrEmpty(cursor));

            var total = fees.Sum(fee => (decimal)(fee.Amount ?? 0d));
            var cents = (long)Math.Round(total, MidpointRounding.AwayFromZero);
            return new FinixFeeResult(cents, !truncated);
        }, cancellationToken);

    public Task<FinixPage<FinixTransferResult>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            var items = new List<FinixTransferResult>();
            string? cursor = null;
            var pages = 0;
            var truncated = false;

            do
            {
                try
                {
                    var response = await _client.TransfersApi.ListTransfers(
                        new ListTransfersRequest
                        {
                            CreatedAtGte = from,
                            CreatedAtLte = to,
                            AfterCursor = cursor,
                            Limit = MaxPageSize
                        },
                        cancellationToken: token);

                    foreach (var transfer in response.Embedded?.Transfers ?? [])
                    {
                        items.Add(MapTransfer(transfer));
                    }

                    cursor = response.Page?.NextCursor;
                    if (++pages >= MaxPages && !string.IsNullOrEmpty(cursor))
                    {
                        truncated = true;
                        break;
                    }
                }
                catch (ApiException<ListTransfersError> ex)
                {
                    throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            } while (!string.IsNullOrEmpty(cursor));

            return new FinixPage<FinixTransferResult>(items, cursor, truncated);
        }, cancellationToken);

    public Task<FinixPage<FinixAuthorizationResult>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken) =>
        BoundedAsync(async token =>
        {
            var items = new List<FinixAuthorizationResult>();
            string? cursor = null;
            var pages = 0;
            var truncated = false;

            do
            {
                try
                {
                    var response = await _client.AuthorizationsApi.ListAuthorizations(
                        new ListAuthorizationsRequest
                        {
                            CreatedAtGte = from,
                            CreatedAtLte = to,
                            AfterCursor = cursor,
                            Limit = MaxPageSize
                        },
                        cancellationToken: token);

                    foreach (var authorization in response.Embedded?.Authorizations ?? [])
                    {
                        items.Add(MapAuthorization(authorization));
                    }

                    cursor = response.Page?.NextCursor;
                    if (++pages >= MaxPages && !string.IsNullOrEmpty(cursor))
                    {
                        truncated = true;
                        break;
                    }
                }
                catch (ApiException<ListAuthorizationsError> ex)
                {
                    throw ToProviderException(
                    ex,
                    ex.Error.TryGetGenericError(out var generic) ? generic : null,
                    ex.Error.TryGetRawError(out var raw) ? raw : null);
                }
            } while (!string.IsNullOrEmpty(cursor));

            return new FinixPage<FinixAuthorizationResult>(items, cursor, truncated);
        }, cancellationToken);

    private async Task<T> BoundedAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_callBudget);

        try
        {
            return await call(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new PaymentProviderException(
                "Finix did not answer within the 30 second call budget.",
                HttpStatusCode.GatewayTimeout,
                outcomeUnknown: true,
                innerException: ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw new PaymentProviderException(
                $"Finix did not answer within {ex.Timeout.TotalSeconds:0} seconds.",
                HttpStatusCode.GatewayTimeout,
                outcomeUnknown: true,
                innerException: ex);
        }
        catch (SdkConnectionException ex)
        {
            throw new PaymentProviderException(
                "Finix could not be reached; the outcome of the request is not known.",
                HttpStatusCode.BadGateway,
                outcomeUnknown: true,
                innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected.",
                HttpStatusCode.BadGateway,
                innerException: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            var outcomeUnknown = (int)ex.StatusCode is >= 200 and < 300;
            throw new PaymentProviderException(
                outcomeUnknown
                    ? "Finix returned a response that could not be read; the outcome of the request is not known."
                    : "Finix rejected the request with a response that could not be read.",
                ex.StatusCode,
                outcomeUnknown: outcomeUnknown,
                innerException: ex);
        }
    }

    private PaymentProviderException ToProviderException(
        ApiException exception,
        GenericError? genericError,
        RawError? rawError = null)
    {
        var detail = genericError?.Embedded?.Errors?.FirstOrDefault();
        var statusCode = exception.StatusCode;
        var outcomeUnknown = (int)statusCode >= 500;

        string? rawMessage = null;
        string? rawCode = null;
        string? rawLogRef = null;

        if (detail is null && rawError is not null)
        {
            try
            {
                var parsed = rawError.ReadAsJson<GenericError>();
                var rawDetail = parsed?.Embedded?.Errors?.FirstOrDefault();
                rawMessage = rawDetail?.Message;
                rawCode = rawDetail?.Code;
                rawLogRef = rawDetail?.Logref;
            }
            catch (System.Text.Json.JsonException)
            {
                var body = rawError.ReadAsString();
                rawMessage = body.Length > 1000 ? body[..1000] : body;
            }
        }

        var providerCode = detail?.Code ?? rawCode;
        var logRef = detail?.Logref ?? rawLogRef;

        _logger.LogWarning(
            "Finix call {Method} {RequestUri} failed with HTTP {StatusCode}; code={ProviderCode}; logref={LogRef}",
            exception.Method,
            exception.RequestUri?.GetLeftPart(UriPartial.Path),
            (int)statusCode,
            providerCode,
            logRef);

        var message = detail?.Message ?? rawMessage;
        if (string.IsNullOrWhiteSpace(message))
        {
            message = $"Finix rejected the request with HTTP {(int)statusCode}.";
        }

        var responseStatus = statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => HttpStatusCode.BadGateway,
            (HttpStatusCode)429 => HttpStatusCode.ServiceUnavailable,
            _ => statusCode
        };

        return new PaymentProviderException(
            message!,
            responseStatus,
            providerCode,
            logRef,
            outcomeUnknown,
            exception);
    }

    private static FinixInstrumentResult MapInstrument(PaymentInstrumentCard card) =>
        new(
            card.Id ?? string.Empty,
            card.Brand?.Value,
            card.LastFour,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Name,
            card.Identity,
            card.Enabled,
            card.Fingerprint,
            card.DisabledCode?.Value,
            card.DisabledMessage);

    private static FinixAuthorizationResult MapAuthorization(Authorization authorization) =>
        new(
            authorization.Id ?? string.Empty,
            authorization.Amount,
            authorization.State?.Value,
            authorization.Currency?.Value,
            authorization.Transfer,
            authorization.Source,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.IsVoid,
            authorization.VoidState?.Value,
            authorization.IdempotencyId,
            authorization.ExpiresAt);

    private static FinixTransferResult MapTransfer(Transfer transfer) =>
        new(
            transfer.Id ?? string.Empty,
            transfer.Amount,
            transfer.Fee,
            transfer.State?.Value,
            transfer.Type?.Value,
            transfer.ParentTransfer,
            transfer.CreatedAt,
            transfer.FailureCode,
            transfer.FailureMessage,
            transfer.IdempotencyId);
}
