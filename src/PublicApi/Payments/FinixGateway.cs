using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
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

using FinixAuthorization = FinixApiReference.Models.Authorization;
using FinixIdentity = FinixApiReference.Models.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class FinixGateway
{
    private static readonly TimeSpan DefaultBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ReconciliationBudget = TimeSpan.FromSeconds(120);

    private readonly FinixApiReferenceClient _client;
    private readonly FinixSettings _settings;
    private readonly ILogger<FinixGateway> _logger;
    private readonly Currency _currency;

    public FinixGateway(FinixApiReferenceClient client, FinixSettings settings, ILogger<FinixGateway> logger)
    {
        _client = client;
        _settings = settings;
        _logger = logger;

        if (!Currency.TryGetKnownValue(settings.Currency, out var currency))
        {
            throw new InvalidOperationException(
                $"Finix:Currency '{settings.Currency}' is not a currency this SDK recognises.");
        }

        _currency = currency;
    }

    public Currency Currency => _currency;

    public async Task<string> CreateBuyerIdentityAsync(string shopperId, string? email, CancellationToken ct)
    {
        var tags = new Dictionary<string, string> { ["eshop_shopper"] = shopperId };
        var body = IdentitiesRequest.Buyer(new Buyer
        {
            Entity = new Entity5 { Email = string.IsNullOrWhiteSpace(email) ? shopperId : email },
            Tags = tags
        });

        try
        {
            var identity = await CallAsync<FinixIdentity, CreateIdentityError>(
                "create buyer identity",
                token => _client.Identities.CreateIdentity(
                    new CreateIdentityRequest { Body = body }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix rejected the buyer identity.", e => GetGeneric(e)),
                ct);

            return identity.Id ?? throw new FinixProviderException("Finix returned an identity without an id.");
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var found = await TryFindIdentityAsync(tags, ct);
            if (found is not null)
            {
                return found;
            }

            throw new FinixProviderException(
                "Finix did not confirm the buyer identity; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixInstrumentInfo> CreateCardInstrumentAsync(string identityId, CardData card,
        string requestTag, CancellationToken ct)
    {
        var tags = new Dictionary<string, string> { ["eshop_request"] = requestTag };
        var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
        {
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            SecurityCode = card.SecurityCode,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Tags = tags,
            Address = BuildAddress(card)
        });

        try
        {
            var response = await CallAsync<CreatePaymentInstrument, CreatePaymentInstrumentError>(
                "create payment instrument",
                token => _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                    new CreatePaymentInstrumentRequest { Body = body }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix rejected the card.", e => GetGeneric(e)),
                ct);

            if (!response.TryGetPaymentInstrumentCard(out var created) || string.IsNullOrWhiteSpace(created.Id))
            {
                throw new FinixProviderException("Finix returned a payment instrument that could not be read.");
            }

            return MapInstrument(created);
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var found = await TryFindInstrumentAsync("eshop_request", requestTag, ct);
            if (found is not null)
            {
                return found;
            }

            throw new FinixProviderException(
                "Finix did not confirm the saved card; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixInstrumentInfo?> GetInstrumentAsync(string instrumentId, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync<PaymentInstrumentsResponse, GetPaymentInstrumentError>(
                "get payment instrument",
                token => _client.PaymentInstrumentsApi.GetPaymentInstrument(
                    new GetPaymentInstrumentRequest { PaymentInstrumentId = instrumentId }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not return the card.", e => GetGeneric(e)),
                ct);

            return response.TryGetPaymentInstrumentCard(out var card) ? MapInstrument(card) : null;
        }
        catch (FinixProviderException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task DisableInstrumentAsync(string instrumentId, CancellationToken ct)
    {
        var body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
            new PaymentInstrumentDisableAPaymentInstrument { Enabled = false });

        try
        {
            await CallAsync<PaymentInstrumentsResponse1, UpdatePaymentInstrumentError>(
                "disable payment instrument",
                token => _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                    new UpdatePaymentInstrumentRequest
                    {
                        PaymentInstrumentId = instrumentId,
                        Body = body
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not disable the card.", e => GetGeneric(e)),
                ct);
        }
        catch (FinixProviderException ex) when (ex.StatusCode == 404)
        {
            return;
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var instrument = await GetInstrumentAsync(instrumentId, ct);
            if (instrument is null || instrument.Enabled == false)
            {
                return;
            }

            throw new FinixProviderException(
                "Finix did not confirm that the card was disabled; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixAuthorizationInfo> AuthorizeAsync(long amountCents, string sourceInstrumentId,
        string idempotencyId, string statementDescriptor, CancellationToken ct)
    {
        var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = _currency,
            Merchant = _settings.MerchantId,
            Source = sourceInstrumentId,
            IdempotencyId = idempotencyId,
            StatementDescriptor = statementDescriptor
        });

        try
        {
            var authorization = await CallAsync<FinixAuthorization, CreateAuthorizationError>(
                "authorize",
                token => _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest { Body = body }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix declined the authorization.", e => GetGeneric(e)),
                ct);

            return MapAuthorization(authorization);
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var found = await TryFindAuthorizationByIdempotencyAsync(idempotencyId, ct);
            if (found is not null)
            {
                return found;
            }

            throw new FinixProviderException(
                "Finix did not confirm the authorization; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        var authorization = await CallAsync<FinixAuthorization, GetAuthorizationError>(
            "get authorization",
            token => _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: token),
            (error, status) => Describe(error, status, "Finix could not return the authorization.", e => GetGeneric(e)),
            ct);

        return MapAuthorization(authorization);
    }

    public async Task<FinixAuthorizationInfo> CaptureAsync(string authorizationId, long amountCents,
        CancellationToken ct)
    {
        var body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
        {
            CaptureAmount = amountCents
        });

        try
        {
            var authorization = await CallAsync<FinixAuthorization, CaptureAuthorizationError>(
                "capture authorization",
                token => _client.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = authorizationId,
                        Body = body
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not capture the authorization.", e => GetGeneric(e)),
                ct);

            return MapAuthorization(authorization);
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var current = await GetAuthorizationAsync(authorizationId, ct);
            if (!string.IsNullOrWhiteSpace(current.TransferId))
            {
                return current;
            }

            throw new FinixProviderException(
                "Finix did not confirm the capture; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixAuthorizationInfo> VoidAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            var authorization = await CallAsync<FinixAuthorization, VoidAuthorizationError>(
                "void authorization",
                token => _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = authorizationId,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not release the hold.", e => GetGeneric(e)),
                ct);

            return MapAuthorization(authorization);
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var current = await GetAuthorizationAsync(authorizationId, ct);
            if (current.IsVoid || current.VoidState == VoidState.Succeeded.Value)
            {
                return current;
            }

            throw new FinixProviderException(
                "Finix did not confirm the release; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixTransferInfo> RefundAsync(string transferId, long amountCents, CancellationToken ct)
    {
        if (amountCents > int.MaxValue)
        {
            throw new PaymentDomainException(400, "Refund amount is too large for Finix.");
        }

        var body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
        {
            RefundAmount = (int)amountCents
        });

        try
        {
            var transfer = await CallAsync<Transfer, CreateTransferReversalError>(
                "refund",
                token => _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = transferId,
                        Body = body
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix rejected the refund.", e => GetGeneric(e)),
                ct);

            return MapTransfer(transfer);
        }
        catch (FinixProviderException ex) when (ex.OutcomeUnknown)
        {
            var reversals = await ListReversalsAsync(transferId, ct);
            var match = reversals.FirstOrDefault(r => r.Amount == amountCents);
            if (match is not null)
            {
                return match;
            }

            throw new FinixProviderException(
                "Finix did not confirm the refund; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    public async Task<FinixTransferInfo> GetTransferAsync(string transferId, CancellationToken ct)
    {
        var transfer = await CallAsync<Transfer, GetTransferError>(
            "get transfer",
            token => _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId }, cancellationToken: token),
            (error, status) => Describe(error, status, "Finix could not return the transfer.", e => GetGeneric(e)),
            ct);

        return MapTransfer(transfer);
    }

    public async Task<FinixListResult<FinixTransferInfo>> ListTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        int maxPages, CancellationToken ct)
    {
        var items = new List<FinixTransferInfo>();
        string? cursor = null;
        var truncated = false;

        for (var page = 0; page < maxPages; page++)
        {
            var request = new ListTransfersRequest
            {
                CreatedAtGte = from,
                CreatedAtLte = to,
                Merchant = _settings.MerchantId,
                Limit = 100,
                AfterCursor = cursor
            };

            var response = await CallAsync<ListTransfers, ListTransfersError>(
                "list transfers",
                token => _client.TransfersApi.ListTransfers(request, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not list transfers.", e => GetGeneric(e)),
                ct, ReconciliationBudget);

            var pageItems = response.Embedded?.Transfers;
            if (pageItems is not null)
            {
                items.AddRange(pageItems.Select(MapTransfer));
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next))
            {
                cursor = null;
                break;
            }

            if (next == cursor)
            {
                truncated = true;
                cursor = next;
                break;
            }

            cursor = next;
            if (page == maxPages - 1)
            {
                truncated = true;
            }
        }

        return new FinixListResult<FinixTransferInfo>(items, truncated, cursor);
    }

    public async Task<FinixListResult<FinixAuthorizationInfo>> ListAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to, int maxPages, CancellationToken ct)
    {
        var items = new List<FinixAuthorizationInfo>();
        string? cursor = null;
        var truncated = false;

        for (var page = 0; page < maxPages; page++)
        {
            var request = new ListAuthorizationsRequest
            {
                CreatedAtGte = from,
                CreatedAtLte = to,
                Merchant = _settings.MerchantId,
                Limit = 100,
                AfterCursor = cursor
            };

            var response = await CallAsync<ListAuthorizations, ListAuthorizationsError>(
                "list authorizations",
                token => _client.AuthorizationsApi.ListAuthorizations(request, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not list authorizations.", e => GetGeneric(e)),
                ct, ReconciliationBudget);

            var pageItems = response.Embedded?.Authorizations;
            if (pageItems is not null)
            {
                items.AddRange(pageItems.Select(MapAuthorization));
            }

            var next = response.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(next))
            {
                cursor = null;
                break;
            }

            if (next == cursor)
            {
                truncated = true;
                cursor = next;
                break;
            }

            cursor = next;
            if (page == maxPages - 1)
            {
                truncated = true;
            }
        }

        return new FinixListResult<FinixAuthorizationInfo>(items, truncated, cursor);
    }

    private async Task<FinixAuthorizationInfo?> TryFindAuthorizationByIdempotencyAsync(string idempotencyId,
        CancellationToken ct)
    {
        try
        {
            var response = await CallAsync<ListAuthorizations, ListAuthorizationsError>(
                "find authorization",
                token => _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        Merchant = _settings.MerchantId,
                        Limit = 10
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not search authorizations.", e => GetGeneric(e)),
                ct);

            var match = response.Embedded?.Authorizations?.FirstOrDefault();
            return match is null ? null : MapAuthorization(match);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<FinixTransferInfo>> ListReversalsAsync(string parentTransferId,
        CancellationToken ct)
    {
        try
        {
            var response = await CallAsync<TransfersReversalsResponse, ListTransferReversalsError>(
                "list reversals",
                token => _client.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest { TransferId = parentTransferId, Limit = 100 },
                    cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not list refunds.", e => GetGeneric(e)),
                ct);

            return response.Embedded?.Transfers?.Select(MapTransfer).ToList() ?? [];
        }
        catch (FinixProviderException)
        {
            return [];
        }
    }

    private async Task<string?> TryFindIdentityAsync(Dictionary<string, string> tags, CancellationToken ct)
    {
        try
        {
            var response = await CallAsync<ListIdentities, ListIdentitiesError>(
                "find buyer identity",
                token => _client.Identities.ListIdentities(
                    new ListIdentitiesRequest
                    {
                        TagsKey = tags.Keys.First(),
                        TagsValue = tags.Values.First(),
                        Limit = 10
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not search identities.", e => GetGeneric(e)),
                ct);

            return response.Embedded?.Identities?.FirstOrDefault()?.Id;
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<FinixInstrumentInfo?> TryFindInstrumentAsync(string tagKey, string tagValue,
        CancellationToken ct)
    {
        try
        {
            var response = await CallAsync<ListPaymentInstruments, ListPaymentInstrumentsError>(
                "find payment instrument",
                token => _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        TagsKey = tagKey,
                        TagsValue = tagValue,
                        Limit = 10
                    }, cancellationToken: token),
                (error, status) => Describe(error, status, "Finix could not search payment instruments.", e => GetGeneric(e)),
                ct);

            var first = response.Embedded?.PaymentInstruments?.FirstOrDefault();
            return first is not null && first.TryGetPaymentInstrumentCard(out var card) ? MapInstrument(card) : null;
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<T> CallAsync<T, TError>(string operation, Func<CancellationToken, Task<T>> call,
        Func<TError, HttpStatusCode, FinixProviderException> mapError, CancellationToken ct,
        TimeSpan? budget = null)
        where TError : ApiError
    {
        try
        {
            return await Bounded(call, ct, budget ?? DefaultBudget);
        }
        catch (ApiException<TError> ex)
        {
            _logger.LogWarning("Finix {Operation} failed with HTTP {Status}.", operation, (int)ex.StatusCode);
            throw mapError(ex.Error, ex.StatusCode);
        }
        catch (ResponseDeserializationException ex)
        {
            _logger.LogError(ex, "Finix {Operation} returned an unreadable response.", operation);
            throw new FinixProviderException(
                $"Finix returned a response for {operation} that could not be processed.",
                (int)ex.StatusCode, innerException: ex);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are missing or were rejected.");
            throw new FinixProviderException("Finix credentials are missing or were rejected.", innerException: ex);
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogWarning(ex, "Finix {Operation} did not complete at the transport level.", operation);
            throw new FinixProviderException(
                $"Finix did not respond to {operation}; the outcome is unknown.", outcomeUnknown: true,
                innerException: ex);
        }
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct,
        TimeSpan budget)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);
        return await call(cts.Token);
    }

    private static FinixProviderException Describe<TError>(TError error, HttpStatusCode status, string fallback,
        Func<TError, GenericError?> getGeneric)
        where TError : ApiError
    {
        var generic = getGeneric(error);
        var first = generic?.Embedded?.Errors?.FirstOrDefault();
        if (first is not null)
        {
            return new FinixProviderException(first.Message ?? fallback, (int)status, first.Code, first.Logref);
        }

        if (error.TryGetRawError(out var raw) && raw is not null)
        {
            string? body = null;
            try
            {
                body = raw.ReadAsString();
            }
            catch
            {
                // The raw body is best-effort only.
            }

            return new FinixProviderException(body ?? fallback, (int)status);
        }

        return new FinixProviderException(fallback, (int)status);
    }

    private static GenericError? GetGeneric(CreateIdentityError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(CreatePaymentInstrumentError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(UpdatePaymentInstrumentError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(GetPaymentInstrumentError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(ListPaymentInstrumentsError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(ListIdentitiesError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(CreateAuthorizationError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(GetAuthorizationError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(CaptureAuthorizationError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(VoidAuthorizationError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(ListAuthorizationsError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(CreateTransferReversalError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(GetTransferError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(ListTransfersError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static GenericError? GetGeneric(ListTransferReversalsError error)
        => error.TryGetGenericError(out var value) ? value : null;

    private static FinixInstrumentInfo MapInstrument(PaymentInstrumentCard card)
    {
        return new FinixInstrumentInfo(
            card.Id ?? string.Empty,
            card.Brand?.Value,
            card.LastFour,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Enabled);
    }

    private static FinixAuthorizationInfo MapAuthorization(FinixAuthorization authorization)
    {
        return new FinixAuthorizationInfo(
            authorization.Id ?? string.Empty,
            authorization.CreatedAt,
            authorization.State?.Value,
            authorization.IsVoid ?? false,
            authorization.VoidState?.Value,
            authorization.Transfer,
            authorization.ExpiresAt,
            authorization.Amount,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.IdempotencyId);
    }

    private static FinixTransferInfo MapTransfer(Transfer transfer)
    {
        return new FinixTransferInfo(
            transfer.Id ?? string.Empty,
            transfer.State?.Value,
            transfer.Amount,
            transfer.Fee,
            transfer.ParentTransfer,
            transfer.Type?.Value,
            transfer.CreatedAt,
            transfer.IdempotencyId,
            transfer.Source);
    }

    private static Address? BuildAddress(CardData card)
    {
        if (string.IsNullOrWhiteSpace(card.Street) && string.IsNullOrWhiteSpace(card.ZipCode))
        {
            return null;
        }

        return new Address
        {
            Line1 = card.Street,
            City = card.City,
            Region = card.State,
            PostalCode = card.ZipCode,
            Country = Country.Usa
        };
    }
}
