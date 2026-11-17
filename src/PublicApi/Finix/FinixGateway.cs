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
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Finix;

public sealed class FinixGateway : IFinixGateway
{
    private const int PageSize = 100;
    private const int ReadPageCap = 200;
    private const int ReconciliationPageCap = 1000;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly Currency _currency;

    public FinixGateway(FinixApiReferenceClient client, IOptions<FinixOptions> options)
    {
        _client = client;
        _options = options.Value;
        _currency = FinixServiceCollectionExtensions.ResolveCurrency(_options.Currency);
    }

    public async Task<FinixIdentity> CreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var tags = new Dictionary<string, string> { ["app_buyer_id"] = buyerId };
        var buyer = new Buyer
        {
            Entity = new Entity5
            {
                Email = buyerId.Contains('@') ? buyerId : null,
                FirstName = "eShop",
                LastName = "Shopper"
            },
            IdentityRoles = new[] { IdentityRole5.Buyer },
            Type = Type10.Personal,
            Tags = tags
        };

        try
        {
            var response = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = IdentitiesRequest.Buyer(buyer) },
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(response.Id))
            {
                throw new FinixProviderException(
                    null,
                    "FINIX_IDENTITY_MISSING_ID",
                    "Finix created an identity but returned no identifier.",
                    outcomeUnknown: true);
            }

            return new FinixIdentity(response.Id!);
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixIdentity?> FindIdentityByTagAsync(
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.Identities.ListIdentities(
                new ListIdentitiesRequest { TagsKey = key, TagsValue = value, Limit = 1 },
                cancellationToken: cancellationToken);

            var identity = response.Embedded?.Identities?.FirstOrDefault();
            return string.IsNullOrWhiteSpace(identity?.Id) ? null : new FinixIdentity(identity!.Id!);
        }
        catch (ApiException<ListIdentitiesError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixInstrument> CreateCardInstrumentAsync(
        string identityId,
        CardInput card,
        string? requestTag,
        CancellationToken cancellationToken)
    {
        var instrument = new PaymentInstrumentCard1
        {
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            Number = card.Number,
            SecurityCode = card.SecurityCode,
            Tags = string.IsNullOrWhiteSpace(requestTag)
                ? null
                : new Dictionary<string, string> { ["app_request_key"] = requestTag }
        };

        try
        {
            var response = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(instrument)
                },
                cancellationToken: cancellationToken);

            if (response.TryGetPaymentInstrumentCard(out var created) && !string.IsNullOrWhiteSpace(created?.Id))
            {
                return MapInstrument(created!);
            }

            throw new FinixProviderException(
                null,
                "FINIX_INSTRUMENT_MISSING_ID",
                "Finix created a payment instrument but returned no identifier.",
                outcomeUnknown: true);
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixInstrument?> GetInstrumentAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: cancellationToken);

            return response.TryGetPaymentInstrumentCard(out var instrument) && instrument is not null
                ? MapInstrument(instrument)
                : null;
        }
        catch (ApiException<GetPaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixInstrument?> FindInstrumentByTagAsync(
        string key,
        string value,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                new ListPaymentInstrumentsRequest { TagsKey = key, TagsValue = value, Limit = 1 },
                cancellationToken: cancellationToken);

            var instrument = response.Embedded?.PaymentInstruments?.FirstOrDefault();
            if (instrument is not null && instrument.TryGetPaymentInstrumentCard(out var card) && card is not null)
            {
                return MapInstrument(card);
            }

            return null;
        }
        catch (ApiException<ListPaymentInstrumentsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task DisableInstrumentAsync(string paymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentInstrumentId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                },
                cancellationToken: cancellationToken);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixAuthorization> AuthorizeAsync(
        long amountCents,
        string source,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var authorization = new AuthorizationAllFields
        {
            Amount = amountCents,
            Currency = _currency,
            Merchant = _options.MerchantId,
            Source = source,
            IdempotencyId = idempotencyId
        };

        try
        {
            var response = await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = AuthorizationsRequest.AuthorizationAllFields(authorization)
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(response);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixAuthorization?> FindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest { IdempotencyId = idempotencyId, Limit = 1 },
                cancellationToken: cancellationToken);

            var authorization = response.Embedded?.Authorizations?.FirstOrDefault();
            return authorization is null ? null : MapAuthorization(authorization);
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixAuthorization> GetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);

            return MapAuthorization(response);
        }
        catch (ApiException<GetAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixAuthorization> CaptureAsync(
        string authorizationId,
        long amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = authorizationId,
                    Body = AuthorizationsRequest1.CaptureAuthorization(
                        new CaptureAuthorization { CaptureAmount = amountCents })
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(response);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixAuthorization> VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: cancellationToken);

            return MapAuthorization(response);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixTransfer> RefundAsync(
        string transferId,
        int amountCents,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.TransfersApi.CreateTransferReversal(
                new CreateTransferReversalRequest
                {
                    TransferId = transferId,
                    Body = TransfersReversalsRequest.RefundTransfer(
                        new RefundTransfer { RefundAmount = amountCents })
                },
                cancellationToken: cancellationToken);

            return MapTransfer(response);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixTransfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: cancellationToken);

            return MapTransfer(response);
        }
        catch (ApiException<GetTransferError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixPage<FinixTransfer>> ListTransferReversalsAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var items = new List<FinixTransfer>();
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages < ReadPageCap)
            {
                var response = await _client.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = transferId,
                        AfterCursor = cursor,
                        Limit = PageSize
                    },
                    cancellationToken: cancellationToken);

                items.AddRange((response.Embedded?.Transfers ?? Array.Empty<Transfer>()).Select(MapTransfer));
                pages++;

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return new FinixPage<FinixTransfer>(items, true, pages);
                }
            }

            return new FinixPage<FinixTransfer>(items, false, pages);
        }
        catch (ApiException<ListTransferReversalsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixPage<FinixTransfer>> ListTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WalkTransfersAsync(from, to, useDateFilter: true, cancellationToken);
        }
        catch (FinixProviderException ex) when (IsDateFormatRejection(ex))
        {
            return await WalkTransfersAsync(from, to, useDateFilter: false, cancellationToken);
        }
    }

    private async Task<FinixPage<FinixTransfer>> WalkTransfersAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        bool useDateFilter,
        CancellationToken cancellationToken)
    {
        var items = new List<FinixTransfer>();
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages < ReconciliationPageCap)
            {
                var request = new ListTransfersRequest
                {
                    AfterCursor = cursor,
                    Limit = PageSize
                };
                if (useDateFilter)
                {
                    request = request with { CreatedAtGte = from, CreatedAtLte = to };
                }

                var response = await _client.TransfersApi.ListTransfers(
                    request,
                    cancellationToken: cancellationToken);

                var pageItems = (response.Embedded?.Transfers ?? Array.Empty<Transfer>()).Select(MapTransfer);
                if (!useDateFilter)
                {
                    pageItems = pageItems.Where(t =>
                        t.CreatedAt.HasValue && t.CreatedAt.Value >= from && t.CreatedAt.Value <= to);
                }

                items.AddRange(pageItems);
                pages++;

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return new FinixPage<FinixTransfer>(items, true, pages);
                }
            }

            return new FinixPage<FinixTransfer>(items, false, pages);
        }
        catch (ApiException<ListTransfersError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixPage<FinixAuthorization>> ListAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        try
        {
            return await WalkAuthorizationsAsync(from, to, useDateFilter: true, cancellationToken);
        }
        catch (FinixProviderException ex) when (IsDateFormatRejection(ex))
        {
            return await WalkAuthorizationsAsync(from, to, useDateFilter: false, cancellationToken);
        }
    }

    private async Task<FinixPage<FinixAuthorization>> WalkAuthorizationsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        bool useDateFilter,
        CancellationToken cancellationToken)
    {
        var items = new List<FinixAuthorization>();
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages < ReconciliationPageCap)
            {
                var request = new ListAuthorizationsRequest
                {
                    AfterCursor = cursor,
                    Limit = PageSize
                };
                if (useDateFilter)
                {
                    request = request with { CreatedAtGte = from, CreatedAtLte = to };
                }

                var response = await _client.AuthorizationsApi.ListAuthorizations(
                    request,
                    cancellationToken: cancellationToken);

                var pageItems = (response.Embedded?.Authorizations ?? Array.Empty<Authorization>())
                    .Select(MapAuthorization);
                if (!useDateFilter)
                {
                    pageItems = pageItems.Where(a =>
                        a.CreatedAt.HasValue && a.CreatedAt.Value >= from && a.CreatedAt.Value <= to);
                }

                items.AddRange(pageItems);
                pages++;

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return new FinixPage<FinixAuthorization>(items, true, pages);
                }
            }

            return new FinixPage<FinixAuthorization>(items, false, pages);
        }
        catch (ApiException<ListAuthorizationsError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    public async Task<FinixFeeSummary> GetFeesAsync(string transferId, CancellationToken cancellationToken)
    {
        var total = 0d;
        var count = 0;
        string? cursor = null;
        var pages = 0;

        try
        {
            while (pages < ReadPageCap)
            {
                var response = await _client.FeesApi.ListFees(
                    new ListFeesRequest
                    {
                        LinkedTo = transferId,
                        AfterCursor = cursor,
                        Limit = PageSize
                    },
                    cancellationToken: cancellationToken);

                foreach (var fee in response.Embedded?.Fees ?? Array.Empty<Fee>())
                {
                    total += fee.Amount ?? 0d;
                    count++;
                }

                pages++;

                cursor = response.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(cursor))
                {
                    return new FinixFeeSummary(
                        (long)Math.Round(total, MidpointRounding.AwayFromZero),
                        count,
                        true,
                        pages);
                }
            }

            return new FinixFeeSummary(
                (long)Math.Round(total, MidpointRounding.AwayFromZero),
                count,
                false,
                pages);
        }
        catch (ApiException<ListFeesError> ex)
        {
            ex.Error.TryGetGenericError(out var generic);
            ex.Error.TryGetRawError(out var raw);
            throw FromApi(ex, generic, raw);
        }
        catch (ResponseDeserializationException ex)
        {
            throw FromDeserialization(ex);
        }
        catch (SdkTimeoutException ex)
        {
            throw FromTransport(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw FromTransport(ex);
        }
        catch (AuthSchemeException ex)
        {
            throw FromAuth(ex);
        }
    }

    private static FinixInstrument MapInstrument(PaymentInstrumentCard instrument) =>
        new(
            instrument.Id ?? string.Empty,
            instrument.LastFour,
            instrument.Brand?.Value,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Enabled,
            instrument.Fingerprint,
            instrument.Name);

    private static FinixAuthorization MapAuthorization(Authorization authorization) =>
        new(
            authorization.Id ?? string.Empty,
            authorization.Amount,
            authorization.State?.Value,
            authorization.Transfer,
            authorization.IsVoid,
            authorization.VoidState?.Value,
            authorization.FailureCode,
            authorization.FailureMessage,
            authorization.CreatedAt);

    private static FinixTransfer MapTransfer(Transfer transfer) =>
        new(
            transfer.Id ?? string.Empty,
            transfer.Amount,
            transfer.State?.Value,
            transfer.Type?.Value,
            transfer.ParentTransfer,
            transfer.Fee,
            transfer.CreatedAt,
            transfer.FailureCode,
            transfer.FailureMessage);

    private static bool IsDateFormatRejection(FinixProviderException exception) =>
        exception.Code == "INVALID_FIELD"
        || (exception.StatusCode == 400
            && exception.Message.Contains("format", StringComparison.OrdinalIgnoreCase));

    private static FinixProviderException FromApi(
        ApiException exception,
        GenericError? generic,
        RawError? raw)
    {
        var first = generic?.Embedded?.Errors?.FirstOrDefault();
        var message = first?.Message;

        return new FinixProviderException(
            (int)exception.StatusCode,
            first?.Code,
            string.IsNullOrWhiteSpace(message)
                ? $"Finix request failed with HTTP {(int)exception.StatusCode}."
                : message!,
            outcomeUnknown: false,
            exception);
    }

    private static FinixProviderException FromDeserialization(ResponseDeserializationException exception)
    {
        var status = (int)exception.StatusCode;
        var success = status is >= 200 and < 300;
        return new FinixProviderException(
            status,
            "FINIX_RESPONSE_UNREADABLE",
            success
                ? "Finix returned a successful response that could not be read; the operation outcome is unknown."
                : "Finix rejected the request but the error body could not be read.",
            outcomeUnknown: success,
            exception);
    }

    private static FinixProviderException FromTransport(SdkException exception) =>
        new(
            null,
            "FINIX_CONNECTION_FAILED",
            "Finix could not be reached or did not answer in time; the operation outcome is unknown.",
            outcomeUnknown: true,
            exception);

    private static FinixProviderException FromAuth(AuthSchemeException exception) =>
        new(
            null,
            "FINIX_AUTH_CONFIGURATION",
            "Finix credentials are missing or could not be applied.",
            outcomeUnknown: false,
            exception);
}

