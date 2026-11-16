using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Authentication.Basic;
using FinixApiReference.Core.Configuration;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixAddress = FinixApiReference.Models.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class FinixPaymentGateway : IFinixPaymentGateway
{
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentGateway> _logger;

    public FinixPaymentGateway(FinixApiReferenceClient client, IOptions<FinixOptions> options,
        ILogger<FinixPaymentGateway> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> CreateBuyerIdentityAsync(string email, string firstName, string lastName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var identity = await _client.Identities.CreateIdentity(new CreateIdentityRequest
            {
                Body = IdentitiesRequest.Buyer(new Buyer
                {
                    Entity = new Entity5
                    {
                        Email = email,
                        FirstName = firstName,
                        LastName = lastName
                    },
                    IdentityRoles = new[] { IdentityRole5.Buyer },
                    Type = Type10.Personal
                })
            }, cancellationToken: cancellationToken);

            return identity.Id ?? throw PaymentException.Upstream("Finix did not return a buyer identity id.");
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Creating the Finix buyer identity");
        }
    }

    public async Task<SavedCardResult> CreatePaymentInstrumentAsync(string identityId, CardDetails card,
        CancellationToken cancellationToken = default)
    {
        var number = new string(card.Number.Where(char.IsDigit).ToArray());

        try
        {
            var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                {
                    Identity = identityId,
                    Name = card.Name,
                    Number = number,
                    ExpirationMonth = card.ExpirationMonth,
                    ExpirationYear = card.ExpirationYear,
                    SecurityCode = card.SecurityCode,
                    Address = new FinixAddress
                    {
                        Line1 = card.BillingAddress.Line1,
                        Line2 = card.BillingAddress.Line2,
                        City = card.BillingAddress.City,
                        Region = card.BillingAddress.Region,
                        PostalCode = card.BillingAddress.PostalCode,
                        Country = ResolveCountry(card.BillingAddress.Country)
                    }
                })
            }, cancellationToken: cancellationToken);

            if (!result.TryGetPaymentInstrumentCard(out var cardResult) || string.IsNullOrWhiteSpace(cardResult.Id))
                throw PaymentException.Upstream("Finix did not return a payment instrument id.");

            return new SavedCardResult(cardResult.Id, cardResult.Brand?.Value, cardResult.LastFour,
                cardResult.ExpirationMonth, cardResult.ExpirationYear);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Saving the card with Finix");
        }
    }

    public async Task DisablePaymentInstrumentAsync(string paymentInstrumentId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(new UpdatePaymentInstrumentRequest
            {
                PaymentInstrumentId = paymentInstrumentId,
                Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                    new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Disabling the Finix payment instrument");
        }
    }

    public async Task<FinixAuthorizationResult> AuthorizeAsync(string paymentInstrumentId, long amountMinor,
        string currency, string idempotencyId, string orderReference,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.CreateAuthorization(new CreateAuthorizationRequest
            {
                Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                {
                    Amount = amountMinor,
                    Currency = ResolveCurrency(currency),
                    Merchant = _options.MerchantId,
                    Source = paymentInstrumentId,
                    IdempotencyId = idempotencyId,
                    Tags = new Dictionary<string, string> { ["order_reference"] = orderReference }
                })
            }, cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Authorizing the payment with Finix");
        }
    }

    public async Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.GetAuthorization(new GetAuthorizationRequest
            {
                AuthorizationId = authorizationId
            }, cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Fetching the Finix authorization");
        }
    }

    public async Task<FinixCaptureResult> CaptureAsync(string authorizationId, long amountMinor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountMinor
                })
            }, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(authorization.Transfer))
                throw PaymentException.Upstream("Finix captured the authorization but did not return a transfer id.");

            try
            {
                return await GetCaptureAsync(authorization.Transfer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Captured transfer {TransferId} could not be refreshed immediately.", authorization.Transfer);
                return new FinixCaptureResult(authorization.Id ?? authorizationId, authorization.Transfer,
                    authorization.State?.Value, amountMinor, 0, amountMinor, authorization.Currency?.Value);
            }
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Capturing the authorized payment with Finix");
        }
    }

    public async Task<FinixCaptureResult> GetCaptureAsync(string transferId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var transfer = await _client.TransfersApi.GetTransfer(new GetTransferRequest
            {
                TransferId = transferId
            }, cancellationToken: cancellationToken);

            var amount = transfer.Amount ?? 0;
            var fee = transfer.Fee ?? 0;
            return new FinixCaptureResult(transferId, transfer.Id ?? transferId, transfer.State?.Value,
                amount, fee, amount - fee, transfer.Currency?.Value);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Fetching the Finix capture");
        }
    }

    public async Task<FinixAuthorizationResult> VoidAsync(string authorizationId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
            {
                AuthorizationIdVoidTo = authorizationId,
                Body = new VoidAnAuthorization { VoidMe = true }
            }, cancellationToken: cancellationToken);

            return MapAuthorization(authorization);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Voiding the Finix authorization");
        }
    }

    public async Task<FinixRefundResult> RefundAsync(string captureTransferId, long amountMinor,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        if (amountMinor > int.MaxValue)
            throw PaymentException.BadRequest("The refund amount is too large for Finix.");

        try
        {
            var refund = new RefundTransfer { RefundAmount = (int)amountMinor };
            refund.AdditionalProperties.Set("idempotency_id", idempotencyKey);

            var transfer = await _client.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
            {
                TransferId = captureTransferId,
                Body = TransfersReversalsRequest.RefundTransfer(refund)
            }, cancellationToken: cancellationToken);

            return new FinixRefundResult(transfer.Id ?? string.Empty, transfer.State?.Value, transfer.Type?.Value,
                transfer.Amount ?? amountMinor, transfer.Fee ?? 0, transfer.Currency?.Value);
        }
        catch (ApiException ex)
        {
            throw ToPaymentException(ex, "Refunding the payment with Finix");
        }
    }

    public async Task<IReadOnlyList<FinixTransactionRecord>> ListTransactionsAsync(DateTimeOffset from,
        DateTimeOffset to, string currency, CancellationToken cancellationToken = default)
    {
        var records = new List<FinixTransactionRecord>();
        var currencyValue = ResolveCurrency(currency);

        await foreach (var transfer in EnumerateTransfers(from, to, currencyValue, cancellationToken))
        {
            records.Add(new FinixTransactionRecord(transfer.Id ?? string.Empty, "TRANSFER",
                transfer.Amount ?? 0, transfer.Currency?.Value, transfer.State?.Value, transfer.Type?.Value,
                transfer.CreatedAt, transfer.Merchant));
        }

        await foreach (var authorization in EnumerateAuthorizations(from, to, currencyValue, cancellationToken))
        {
            records.Add(new FinixTransactionRecord(authorization.Id ?? string.Empty, "AUTHORIZATION",
                authorization.Amount ?? 0, authorization.Currency?.Value, authorization.State?.Value, "AUTHORIZATION",
                authorization.CreatedAt, authorization.Merchant));
        }

        return records;
    }

    private async IAsyncEnumerable<Transfer> EnumerateTransfers(DateTimeOffset from, DateTimeOffset to,
        Currency currency, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 1000; page++)
        {
            ListTransfers result;
            try
            {
                result = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    Merchant = _options.MerchantId,
                    Currency = currency.Value,
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    AfterCursor = cursor,
                    Limit = 100
                }, cancellationToken: cancellationToken);
            }
            catch (ApiException ex)
            {
                throw ToPaymentException(ex, "Listing Finix transfers");
            }

            if (result.Embedded?.Transfers is { } transfers)
            {
                foreach (var transfer in transfers)
                    yield return transfer;
            }

            cursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
                yield break;
        }
    }

    private async IAsyncEnumerable<Authorization> EnumerateAuthorizations(DateTimeOffset from, DateTimeOffset to,
        Currency currency, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var page = 0; page < 1000; page++)
        {
            ListAuthorizations result;
            try
            {
                result = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    Merchant = _options.MerchantId,
                    Currency = currency.Value,
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    AfterCursor = cursor,
                    Limit = 100
                }, cancellationToken: cancellationToken);
            }
            catch (ApiException ex)
            {
                throw ToPaymentException(ex, "Listing Finix authorizations");
            }

            if (result.Embedded?.Authorizations is { } authorizations)
            {
                foreach (var authorization in authorizations)
                    yield return authorization;
            }

            cursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor) || !seenCursors.Add(cursor))
                yield break;
        }
    }

    private static FinixAuthorizationResult MapAuthorization(Authorization authorization)
    {
        if (string.IsNullOrWhiteSpace(authorization.Id))
            throw PaymentException.Upstream("Finix did not return an authorization id.");

        return new FinixAuthorizationResult(authorization.Id, authorization.State?.Value,
            authorization.IsVoid ?? false, authorization.Amount ?? 0, authorization.ExpiresAt,
            authorization.FailureCode, authorization.FailureMessage, authorization.Transfer);
    }

    private static Currency ResolveCurrency(string currency)
    {
        if (Currency.TryGetKnownValue(currency?.Trim().ToUpperInvariant(), out var value))
            return value;

        throw PaymentException.BadRequest($"Finix:Currency '{currency}' is not supported by the Finix SDK.");
    }

    private static Country ResolveCountry(string country)
    {
        var code = country?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code is "US" or "USA" or "UNITED STATES" or "UNITED STATES OF AMERICA")
            return Country.Usa;

        if (Country.TryGetKnownValue(code, out var value))
            return value;

        throw PaymentException.BadRequest($"Billing country '{country}' is not supported.");
    }

    private static PaymentException ToPaymentException(ApiException exception, string operation)
    {
        var statusCode = (int)exception.StatusCode;
        if (statusCode is < 400 or >= 500)
            statusCode = 502;

        return new PaymentException(statusCode, $"{operation} failed: {Describe(exception)}");
    }

    private static string Describe(ApiException exception)
    {
        var error = exception.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)
            ?.GetValue(exception);

        if (error is not null)
        {
            var tryGeneric = error.GetType().GetMethod("TryGetGenericError",
                BindingFlags.Public | BindingFlags.Instance);
            if (tryGeneric is not null)
            {
                var arguments = new object?[] { null };
                if (tryGeneric.Invoke(error, arguments) is true && arguments[0] is GenericError genericError)
                {
                    var messages = genericError.Embedded?.Errors?
                        .Select(e => e.Message)
                        .Where(m => !string.IsNullOrWhiteSpace(m))
                        .ToArray();
                    if (messages is { Length: > 0 })
                        return string.Join("; ", messages);
                }
            }

            if (error is ApiError apiError && apiError.TryGetRawError(out var raw))
            {
                var text = raw.ReadAsString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Length > 1000 ? text[..1000] : text;
            }
        }

        return exception.Message;
    }
}
