using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed record FinixAuthorizationResult(
    string Id,
    string? State,
    long? AmountCents,
    string? Currency,
    bool IsVoid,
    DateTimeOffset? ExpiresAt,
    string? TransferId,
    string? FailureCode,
    string? FailureMessage);

public sealed record FinixTransferResult(
    string Id,
    string? State,
    long? AmountCents,
    long? FeeCents,
    string? Type,
    string? OperationKey,
    string? ParentTransferId,
    DateTimeOffset? CreatedAt);

public sealed record FinixCardResult(
    string Id,
    string? Brand,
    string? LastFour,
    int? ExpirationMonth,
    int? ExpirationYear);

public sealed record FinixTransactionRecord(
    string RecordType,
    string Id,
    long? AmountCents,
    string? Currency,
    string? State,
    string? Type,
    string? ParentTransferId,
    DateTimeOffset? CreatedAt);

public sealed class FinixGateway
{
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;

    public FinixGateway(FinixApiReferenceClient client, IOptions<FinixOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string> CreateBuyerIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        try
        {
            var identity = await _client.Identities.CreateIdentity(new CreateIdentityRequest
            {
                Body = IdentitiesRequest.Buyer(new Buyer
                {
                    Entity = new Entity5
                    {
                        Email = ownerId.Contains('@') ? ownerId : null
                    },
                    IdentityRoles = new[] { IdentityRole5.Buyer },
                    Type = Type10.Personal
                })
            }, cancellationToken: cancellationToken);

            return identity.Id
                ?? throw new PaymentOperationException("finix_identity_missing_id", "Finix did not return an identity id.", 502);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_identity_failed", "Creating the Finix buyer identity");
        }
    }

    public async Task<FinixCardResult> CreateCardAsync(string identityId, CardInput card, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                {
                    Identity = identityId,
                    Number = NormalizeCardNumber(card.Number),
                    ExpirationMonth = NormalizeMonth(card.ExpirationMonth),
                    ExpirationYear = NormalizeYear(card.ExpirationYear),
                    SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode.Trim(),
                    Name = string.IsNullOrWhiteSpace(card.Name) ? "eShop Customer" : card.Name.Trim(),
                    Address = ToAddress(card.BillingAddress)
                })
            }, cancellationToken: cancellationToken);

            if (!result.TryGetPaymentInstrumentCard(out var paymentInstrument) || string.IsNullOrWhiteSpace(paymentInstrument.Id))
            {
                throw new PaymentOperationException("finix_card_missing_id", "Finix did not return a payment instrument id.", 502);
            }

            return new FinixCardResult(
                paymentInstrument.Id,
                paymentInstrument.Brand?.Value,
                paymentInstrument.LastFour,
                paymentInstrument.ExpirationMonth,
                paymentInstrument.ExpirationYear);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_card_failed", "Saving the card with Finix", 402);
        }
    }

    public async Task<FinixAuthorizationResult> AuthorizeAsync(long amountCents, string source, string idempotencyId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyId))
        {
            idempotencyId = Guid.NewGuid().ToString("N");
        }

        try
        {
            var authorization = await _client.AuthorizationsApi.CreateAuthorization(new CreateAuthorizationRequest
            {
                Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = ToCurrency(_options.Currency),
                    Merchant = _options.MerchantId,
                    Source = source,
                    IdempotencyId = idempotencyId
                })
            }, cancellationToken: cancellationToken);

            return ToAuthorizationResult(authorization);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_authorization_failed", "Authorizing the order with Finix", 402);
        }
    }

    public async Task<FinixAuthorizationResult> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.GetAuthorization(new GetAuthorizationRequest
            {
                AuthorizationId = authorizationId
            }, cancellationToken: cancellationToken);

            return ToAuthorizationResult(authorization);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_authorization_lookup_failed", "Retrieving the Finix authorization");
        }
    }

    public async Task<FinixAuthorizationResult> CaptureAsync(string authorizationId, long amountCents, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = amountCents
                })
            }, cancellationToken: cancellationToken);

            return ToAuthorizationResult(authorization);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_capture_failed", "Capturing the authorized payment", 409);
        }
    }

    public async Task<FinixAuthorizationResult> VoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
            {
                AuthorizationIdVoidTo = authorizationId,
                Body = new VoidAnAuthorization { VoidMe = true }
            }, cancellationToken: cancellationToken);

            return ToAuthorizationResult(authorization);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_void_failed", "Releasing the authorized payment", 409);
        }
    }

    public async Task<FinixTransferResult> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            var transfer = await _client.TransfersApi.GetTransfer(new GetTransferRequest
            {
                TransferId = transferId
            }, cancellationToken: cancellationToken);

            return ToTransferResult(transfer);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_transfer_lookup_failed", "Retrieving the Finix transfer");
        }
    }

    public async Task<FinixTransferResult> RefundAsync(string captureTransferId, long amountCents, CancellationToken cancellationToken)
    {
        if (amountCents <= 0 || amountCents > int.MaxValue)
        {
            throw new PaymentOperationException("invalid_refund_amount", "The refund amount must be a positive number of cents.", 400);
        }

        try
        {
            var transfer = await _client.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
            {
                TransferId = captureTransferId,
                Body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
                {
                    RefundAmount = (int)amountCents
                })
            }, cancellationToken: cancellationToken);

            return ToTransferResult(transfer);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_refund_failed", "Refunding the captured payment", 409);
        }
    }

    public async Task DisableCardAsync(string paymentInstrumentId, CancellationToken cancellationToken)
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
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_disable_card_failed", "Disabling the saved card with Finix");
        }
    }

    public async Task<(IReadOnlyList<FinixTransactionRecord> Authorizations, IReadOnlyList<FinixTransactionRecord> Transfers)> ListTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var authorizations = new List<FinixTransactionRecord>();
        var transfers = new List<FinixTransactionRecord>();

        try
        {
            var seenAuthorizations = new HashSet<string>(StringComparer.Ordinal);
            var seenAuthorizationCursors = new HashSet<string>(StringComparer.Ordinal);
            var authorizationCursor = (string?)null;
            var authorizationPages = 0;
            do
            {
                var page = await _client.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    Merchant = _options.MerchantId,
                    Limit = 100,
                    AfterCursor = authorizationCursor
                }, cancellationToken: cancellationToken);

                foreach (var authorization in page.Embedded?.Authorizations ?? Array.Empty<Authorization>())
                {
                    if (string.IsNullOrWhiteSpace(authorization.Id) || !seenAuthorizations.Add(authorization.Id))
                    {
                        continue;
                    }

                    if (authorization.CreatedAt is DateTimeOffset authorizationCreatedAt
                        && (authorizationCreatedAt < from || authorizationCreatedAt > to))
                    {
                        continue;
                    }

                    authorizations.Add(new FinixTransactionRecord(
                        "AUTHORIZATION",
                        authorization.Id,
                        authorization.Amount,
                        authorization.Currency?.Value,
                        authorization.State?.Value,
                        null,
                        null,
                        authorization.CreatedAt));
                }

                var nextAuthorizationCursor = page.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(nextAuthorizationCursor)
                    || !seenAuthorizationCursors.Add(nextAuthorizationCursor))
                {
                    authorizationCursor = null;
                }
                else
                {
                    authorizationCursor = nextAuthorizationCursor;
                }

                authorizationPages++;
            }
            while (!string.IsNullOrWhiteSpace(authorizationCursor) && authorizationPages < 1000);

            var seenTransfers = new HashSet<string>(StringComparer.Ordinal);
            var seenTransferCursors = new HashSet<string>(StringComparer.Ordinal);
            var transferCursor = (string?)null;
            var transferPages = 0;
            do
            {
                var page = await _client.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    Merchant = _options.MerchantId,
                    Limit = 100,
                    AfterCursor = transferCursor
                }, cancellationToken: cancellationToken);

                foreach (var transfer in page.Embedded?.Transfers ?? Array.Empty<Transfer>())
                {
                    if (string.IsNullOrWhiteSpace(transfer.Id) || !seenTransfers.Add(transfer.Id))
                    {
                        continue;
                    }

                    if (transfer.CreatedAt is DateTimeOffset transferCreatedAt
                        && (transferCreatedAt < from || transferCreatedAt > to))
                    {
                        continue;
                    }

                    transfers.Add(new FinixTransactionRecord(
                        "TRANSFER",
                        transfer.Id,
                        transfer.Amount,
                        transfer.Currency?.Value,
                        transfer.State?.Value,
                        transfer.Type?.Value,
                        transfer.ParentTransfer,
                        transfer.CreatedAt));
                }

                var nextTransferCursor = page.Page?.NextCursor;
                if (string.IsNullOrWhiteSpace(nextTransferCursor)
                    || !seenTransferCursors.Add(nextTransferCursor))
                {
                    transferCursor = null;
                }
                else
                {
                    transferCursor = nextTransferCursor;
                }

                transferPages++;
            }
            while (!string.IsNullOrWhiteSpace(transferCursor) && transferPages < 1000);
        }
        catch (Exception ex) when (ex is not PaymentOperationException and not OperationCanceledException)
        {
            throw ToPaymentException(ex, "finix_reconciliation_failed", "Listing Finix transactions");
        }

        return (authorizations, transfers);
    }

    private static FinixAuthorizationResult ToAuthorizationResult(Authorization authorization)
    {
        return new FinixAuthorizationResult(
            authorization.Id ?? string.Empty,
            authorization.State?.Value,
            authorization.Amount,
            authorization.Currency?.Value,
            authorization.IsVoid == true,
            authorization.ExpiresAt,
            authorization.Transfer,
            authorization.FailureCode,
            authorization.FailureMessage);
    }

    private static FinixTransferResult ToTransferResult(Transfer transfer)
    {
        return new FinixTransferResult(
            transfer.Id ?? string.Empty,
            transfer.State?.Value,
            transfer.Amount,
            transfer.Fee,
            transfer.Type?.Value,
            transfer.OperationKey?.Value,
            transfer.ParentTransfer,
            transfer.CreatedAt);
    }

    private static string NormalizeCardNumber(string number)
    {
        var normalized = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (normalized.Length < 12 || normalized.Length > 19)
        {
            throw new PaymentOperationException("invalid_card_number", "The card number is not valid.", 400);
        }

        return normalized;
    }

    private static int NormalizeMonth(int month)
    {
        if (month < 1 || month > 12)
        {
            throw new PaymentOperationException("invalid_expiration_month", "The card expiration month must be between 1 and 12.", 400);
        }

        return month;
    }

    private static int NormalizeYear(int year)
    {
        if (year < 100)
        {
            year += 2000;
        }

        if (year < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentOperationException("invalid_expiration_year", "The card expiration year is in the past.", 400);
        }

        return year;
    }

    private static Address? ToAddress(ShippingAddressInput? address)
    {
        if (address is null)
        {
            return null;
        }

        return new Address
        {
            Line1 = string.IsNullOrWhiteSpace(address.Street) ? "1 Main St" : address.Street,
            City = string.IsNullOrWhiteSpace(address.City) ? "Springfield" : address.City,
            Region = string.IsNullOrWhiteSpace(address.State) ? "IL" : address.State,
            PostalCode = string.IsNullOrWhiteSpace(address.ZipCode) ? "62701" : address.ZipCode,
            Country = ToCountry(address.Country)
        };
    }

    private static Country ToCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return Country.Usa;
        }

        var value = country.Trim().ToUpperInvariant();
        if (value is "US" or "USA" or "UNITED STATES")
        {
            return Country.Usa;
        }

        if (value is "CA" or "CAN" or "CANADA")
        {
            return Country.Can;
        }

        try
        {
            return JsonSerializer.Deserialize<Country>(JsonSerializer.Serialize(value)) ?? Country.Usa;
        }
        catch (JsonException)
        {
            return Country.Usa;
        }
    }

    private static Currency ToCurrency(string currency)
    {
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase))
        {
            return Currency.Usd;
        }

        if (string.Equals(currency, "CAD", StringComparison.OrdinalIgnoreCase))
        {
            return Currency.Cad;
        }

        return JsonSerializer.Deserialize<Currency>(JsonSerializer.Serialize(currency)) ?? Currency.Usd;
    }

    private static PaymentOperationException ToPaymentException(Exception exception, string code, string action, int statusCode = 502)
    {
        var description = Describe(exception);
        return new PaymentOperationException(
            code,
            $"{action} failed: {description}",
            statusCode,
            new Dictionary<string, object?> { ["finix"] = description });
    }

    private static string Describe(Exception exception)
    {
        if (exception is ApiException apiException)
        {
            var error = apiException.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance)?.GetValue(apiException);
            if (error is not null)
            {
                var rawErrorMethod = error.GetType().GetMethod("TryGetRawError", BindingFlags.Public | BindingFlags.Instance);
                if (rawErrorMethod is not null)
                {
                    var arguments = new object?[] { null };
                    if (rawErrorMethod.Invoke(error, arguments) is true && arguments[0] is RawError rawError)
                    {
                        return Trim(rawError.ReadAsString());
                    }
                }

                var genericErrorMethod = error.GetType().GetMethod("TryGetGenericError", BindingFlags.Public | BindingFlags.Instance);
                if (genericErrorMethod is not null)
                {
                    var arguments = new object?[] { null };
                    if (genericErrorMethod.Invoke(error, arguments) is true && arguments[0] is GenericError genericError)
                    {
                        var messages = genericError.Embedded.Errors?
                            .Select(item => $"{item.Code}: {item.Message}")
                            .Where(item => !string.IsNullOrWhiteSpace(item))
                            .ToArray();
                        if (messages is { Length: > 0 })
                        {
                            return string.Join("; ", messages);
                        }
                    }
                }
            }
        }

        return exception.Message;
    }

    private static string Trim(string value)
    {
        const int limit = 2000;
        return value.Length <= limit ? value : value[..limit];
    }
}
