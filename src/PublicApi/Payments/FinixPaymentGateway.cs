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
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.Extensions.Logging;
using Authorization = FinixApiReference.Models.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The single seam over the Finix SDK. Every Finix call the application makes lives here, so the
/// rest of the app never handles SDK types or provider errors directly.
/// </summary>
public sealed class FinixPaymentGateway
{
    private const int MaxPages = 200;
    private const int PageSize = 100;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixSettings _settings;
    private readonly ILogger<FinixPaymentGateway> _logger;

    public FinixPaymentGateway(FinixApiReferenceClient client, FinixSettings settings, ILogger<FinixPaymentGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;
    }

    public FinixSettings Settings => _settings;

    public Currency Currency => _settings.ResolveCurrency();

    // --------------------------------------------------------------------- identities

    public async Task<FinixApiReference.Models.Identity> CreateBuyerIdentityAsync(string buyerId, string? email, CancellationToken ct)
    {
        var buyer = new Buyer
        {
            Entity = new Entity5
            {
                Email = email,
                FirstName = email,
                LastName = "Shopper"
            },
            IdentityRoles = new[] { IdentityRole5.Buyer },
            Type = Type10.Personal,
            Tags = new Dictionary<string, string> { ["buyer_id"] = buyerId }
        };

        var body = IdentitiesRequest.Buyer(buyer);
        try
        {
            var identity = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = body }, cancellationToken: ct);
            if (string.IsNullOrWhiteSpace(identity?.Id))
            {
                throw new PaymentOperationException(502, "Finix created an identity without an id.");
            }
            return identity!;
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix rejected the Buyer identity.", "create identity", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "create identity", isWrite: true);
        }
    }

    public async Task<(IReadOnlyList<FinixApiReference.Models.Identity> Items, bool Truncated)> FindBuyerIdentitiesAsync(
        string buyerId, CancellationToken ct)
    {
        var results = new List<FinixApiReference.Models.Identity>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.Identities.ListIdentities(
                    new ListIdentitiesRequest
                    {
                        TagsKey = "buyer_id",
                        TagsValue = buyerId,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                if (response.Embedded?.Identities is { } items)
                {
                    results.AddRange(items);
                }

                var next = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next)) return (results, false);
                if (string.Equals(next, cursor, StringComparison.Ordinal)) break;
                cursor = next;
            }
            catch (ApiException<ListIdentitiesError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not list identities.", "list identities", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "list identities", isWrite: false);
            }
        }
        return (results, true);
    }

    // --------------------------------------------------------------------- saved cards

    public async Task<PaymentInstrumentCard> CreateCardAsync(
        string identityId, CardInput card, string buyerId, CancellationToken ct)
    {
        var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
        {
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            Number = NormalizeCardNumber(card.Number),
            SecurityCode = card.SecurityCode,
            Tags = new Dictionary<string, string> { ["buyer_id"] = buyerId }
        });

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest { Body = body }, cancellationToken: ct);

            if (!response.TryGetPaymentInstrumentCard(out var created) || string.IsNullOrWhiteSpace(created?.Id))
            {
                throw new PaymentOperationException(502, "Finix created a card without a payment-instrument id.");
            }
            return created!;
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix rejected the card details.", "create card", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "create card", isWrite: true);
        }
    }

    public async Task<PaymentInstrumentCard> GetCardAsync(string paymentInstrumentId, CancellationToken ct)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: ct);

            if (!response.TryGetPaymentInstrumentCard(out var card) || string.IsNullOrWhiteSpace(card?.Id))
            {
                throw new PaymentOperationException(502, "Finix returned an unreadable payment instrument.");
            }
            return card!;
        }
        catch (ApiException<GetPaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not read the saved card.", "get card", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "get card", isWrite: false);
        }
    }

    public async Task<IReadOnlyList<PaymentInstrumentCard>> ListCardsAsync(
        string identityId, DateTimeOffset? createdAfter, CancellationToken ct)
    {
        var results = new List<PaymentInstrumentCard>();
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        OwnerIdentityId = identityId,
                        CreatedAtGte = createdAfter,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                if (response.Embedded?.PaymentInstruments is { } items)
                {
                    foreach (var item in items)
                    {
                        if (item.TryGetPaymentInstrumentCard(out var card) && card is not null)
                        {
                            results.Add(card);
                        }
                    }
                }

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }
            catch (ApiException<ListPaymentInstrumentsError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not list saved cards.", "list cards", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "list cards", isWrite: false);
            }
        }
        return results;
    }

    public async Task DisableCardAsync(string paymentInstrumentId, CancellationToken ct)
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
                cancellationToken: ct);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not disable the saved card.", "disable card", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "disable card", isWrite: true);
        }
    }

    // --------------------------------------------------------------------- authorizations

    public async Task<Authorization> CreateAuthorizationAsync(
        string source, long amountCents, string idempotencyId, int orderId, string buyerId, CancellationToken ct)
    {
        var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = Currency,
            Merchant = _settings.MerchantId,
            Source = source,
            IdempotencyId = idempotencyId,
            StatementDescriptor = "ESHOPONWEB",
            Tags = new Dictionary<string, string>
            {
                ["order_id"] = orderId.ToString(),
                ["buyer_id"] = buyerId
            }
        });

        try
        {
            return await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest { Body = body }, cancellationToken: ct);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix declined the authorization.", "authorize", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "authorize", isWrite: true);
        }
    }

    public async Task<Authorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not read the authorization.", "get authorization", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "get authorization", isWrite: false);
        }
    }

    public async Task<Authorization> CaptureAuthorizationAsync(string authorizationId, long amountCents, CancellationToken ct)
    {
        var body = AuthorizationsRequest1.CaptureAuthorization(
            new CaptureAuthorization { CaptureAmount = amountCents });

        try
        {
            return await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest { AuthorizationId = authorizationId, Body = body },
                cancellationToken: ct);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not capture the authorization.", "capture", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "capture", isWrite: true);
        }
    }

    public async Task<Authorization> VoidAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: ct);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not release the authorization.", "void authorization", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "void authorization", isWrite: true);
        }
    }

    public async Task<(IReadOnlyList<Authorization> Items, bool Truncated)> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<Authorization>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        CreatedAtGte = from,
                        CreatedAtLte = to,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                if (response.Embedded?.Authorizations is { } items)
                {
                    results.AddRange(items);
                }

                var next = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next)) return (results, false);
                if (string.Equals(next, cursor, StringComparison.Ordinal))
                {
                    break;
                }
                cursor = next;
            }
            catch (ApiException<ListAuthorizationsError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not list authorizations.", "list authorizations", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "list authorizations", isWrite: false);
            }
        }

        return (results, true);
    }

    public async Task<Authorization?> FindAuthorizationByIdempotencyIdAsync(string idempotencyId, CancellationToken ct)
    {
        string? cursor = null;
        var from = DateTimeOffset.UtcNow.AddDays(-30);
        var to = DateTimeOffset.UtcNow.AddDays(1);

        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        CreatedAtGte = from,
                        CreatedAtLte = to,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                var match = response.Embedded?.Authorizations?
                    .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
                if (match is not null) return match;

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor)) break;
            }
            catch (ApiException<ListAuthorizationsError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not search authorizations.", "find authorization", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "find authorization", isWrite: false);
            }
        }

        return null;
    }

    // --------------------------------------------------------------------- transfers
    public async Task<Transfer> GetTransferAsync(string transferId, CancellationToken ct)
    {
        try
        {
            return await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId }, cancellationToken: ct);
        }
        catch (ApiException<GetTransferError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not read the transfer.", "get transfer", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "get transfer", isWrite: false);
        }
    }

    public async Task<Transfer> CreateReversalAsync(string transferId, long amountCents, CancellationToken ct)
    {
        if (amountCents > int.MaxValue)
        {
            throw new PaymentOperationException(400, "Refund amount exceeds the provider's supported range.");
        }

        var body = TransfersReversalsRequest.RefundTransfer(
            new RefundTransfer { RefundAmount = (int)amountCents });

        try
        {
            return await _client.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest { TransferId = transferId, Body = body },
                cancellationToken: ct);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix rejected the refund.", "refund", ex);
        }
        catch (Exception ex) when (IsTransport(ex))
        {
            throw TransportFailure(ex, "refund", isWrite: true);
        }
    }

    public async Task<(IReadOnlyList<Transfer> Items, bool Truncated)> ListReversalsAsync(
        string transferId, CancellationToken ct)
    {
        var results = new List<Transfer>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = transferId,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                if (response.Embedded?.Transfers is { } items)
                {
                    results.AddRange(items);
                }

                var next = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next)) return (results, false);
                if (string.Equals(next, cursor, StringComparison.Ordinal)) break;
                cursor = next;
            }
            catch (ApiException<ListTransferReversalsError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not list refunds.", "list refunds", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "list refunds", isWrite: false);
            }
        }
        return (results, true);
    }

    public async Task<(IReadOnlyList<Transfer> Items, bool Truncated)> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, Type53? type, CancellationToken ct)
    {
        var results = new List<Transfer>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            try
            {
                var response = await _client.TransfersApi.ListTransfers(
                    new ListTransfersRequest
                    {
                        CreatedAtGte = from,
                        CreatedAtLte = to,
                        Type = type,
                        Limit = PageSize,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct);

                if (response.Embedded?.Transfers is { } items)
                {
                    results.AddRange(items);
                }

                var next = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(next)) return (results, false);
                if (string.Equals(next, cursor, StringComparison.Ordinal)) break;
                cursor = next;
            }
            catch (ApiException<ListTransfersError> ex)
            {
                ex.Error.TryGetGenericError(out var generic);
                throw ApiFailure(ex.StatusCode, generic, ex.Error, "Finix could not list transfers.", "list transfers", ex);
            }
            catch (Exception ex) when (IsTransport(ex))
            {
                throw TransportFailure(ex, "list transfers", isWrite: false);
            }
        }

        return (results, true);
    }

    // --------------------------------------------------------------------- helpers

    private static string NormalizeCardNumber(string number)
        => new string((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static bool IsTransport(Exception ex)
        => ex is SdkConnectionException or SdkTimeoutException or AuthSchemeException
            or ResponseDeserializationException;

    private PaymentOperationException TransportFailure(Exception ex, string operation, bool isWrite)
    {
        _logger.LogWarning(ex, "Finix {Operation} failed before a usable response.", operation);

        return ex switch
        {
            SdkTimeoutException => new PaymentOperationException(
                504, $"Finix did not answer the {operation} request in time; the outcome is unknown.",
                outcomeUnknown: isWrite, innerException: ex),
            AuthSchemeException => new PaymentOperationException(
                502, "Finix credentials are missing or were rejected.", innerException: ex),
            ResponseDeserializationException rde => new PaymentOperationException(
                rde.StatusCode is { } s && (int)s >= 200 && (int)s < 300 ? 502 : (int)rde.StatusCode,
                $"Finix returned an unreadable response to {operation}; the outcome is unknown.",
                outcomeUnknown: isWrite, innerException: ex),
            _ => new PaymentOperationException(
                502, $"Finix could not be reached for {operation}; the outcome is unknown.",
                outcomeUnknown: isWrite, innerException: ex)
        };
    }

    private PaymentOperationException ApiFailure(
        HttpStatusCode status, GenericError? generic, ApiError apiError, string fallback, string operation, Exception inner)
    {
        var first = generic?.Embedded?.Errors?.FirstOrDefault();

        string? rawBody = null;
        if (first is null && apiError.TryGetRawError(out var raw) && raw is not null)
        {
            try { rawBody = raw.ReadAsString(); } catch { /* keep the fallback */ }
        }

        _logger.LogWarning(
            "Finix {Operation} failed with HTTP {Status}; code={Code}; logref={Logref}; body={Body}.",
            operation, (int)status, first?.Code, first?.Logref, rawBody);

        var mapped = (int)status switch
        {
            401 or 403 => 502,
            429 => 503,
            >= 500 => 502,
            _ => (int)status
        };

        var message = !string.IsNullOrWhiteSpace(first?.Message)
            ? first!.Message!
            : !string.IsNullOrWhiteSpace(rawBody)
                ? $"Finix rejected the {operation}: {Truncate(rawBody!)}"
                : fallback;

        return new PaymentOperationException(
            mapped,
            message,
            providerCode: first?.Code,
            providerLogRef: first?.Logref,
            innerException: inner);
    }

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value.Substring(0, 500);
}
