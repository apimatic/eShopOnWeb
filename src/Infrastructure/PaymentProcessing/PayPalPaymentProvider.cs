using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using PayPalServerSdk;
using PayPalServerSdk.Api;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.PaymentProcessing;

/// <summary>
/// Implements <see cref="IPaymentProvider"/> against the PayPal Server SDK. Every write operation
/// (authorize, capture, void, refund, vault-create) is called with a PayPal-Request-Id idempotency
/// key so a retried request cannot move money or vault a card twice.
/// </summary>
public class PayPalPaymentProvider : IPaymentProvider
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentProvider(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizationResult> AuthorizeAsync(AuthorizePaymentRequest request, CancellationToken ct)
    {
        if (request.Card is null == request.VaultId is null)
        {
            throw new ArgumentException("Exactly one of Card or VaultId must be supplied.", nameof(request));
        }

        var createBody = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = request.Currency,
                        Value = FormatAmount(request.Amount)
                    }
                }
            }
        };

        Order createdOrder;
        try
        {
            createdOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{request.IdempotencyKey}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: createBody,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw Translate("create order", ex.Error, e => e.TryGetError(out var err) ? DescribeError(err) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while creating the order.", ex);
        }

        var paymentSource = BuildPaymentSource(request);
        var authorizeBody = new OrderAuthorizeRequest { PaymentSource = paymentSource };

        OrderAuthorizeResponse authorizeResponse;
        try
        {
            authorizeResponse = await _client.Orders.AuthorizeOrder(
                id: createdOrder.Id!,
                payPalMockResponse: null,
                payPalRequestId: $"authorize-{request.IdempotencyKey}",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeBody,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw Translate("authorize order", ex.Error, e => e.TryGetError(out var err) ? DescribeError(err) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while authorizing the order.", ex);
        }

        if (authorizeResponse.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentApprovalRequiredException(
                $"PayPal requires the shopper to complete an interactive approval step for order {createdOrder.Id}. " +
                "This integration only supports direct, non-interactive card payments; a manual/alternate approval flow is required.");
        }

        var authorization = authorizeResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization?.Id is null)
        {
            throw new PaymentProviderException($"PayPal did not return an authorization for order {createdOrder.Id} (status {authorizeResponse.Status?.Value}).");
        }

        return new AuthorizationResult(
            createdOrder.Id!,
            authorization.Id,
            authorization.Status?.Value ?? "UNKNOWN",
            ParseTimestamp(authorization.ExpirationTime));
    }

    public async Task<AuthorizationFreshnessResult> GetAuthorizationFreshnessAsync(string authorizationId, CancellationToken ct)
    {
        PaymentAuthorization authorization;
        try
        {
            authorization = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw Translate("check authorization", ex.Error, e =>
                e.TryGetError(out var err) ? DescribeError(err) :
                e.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while checking the authorization.", ex);
        }

        var expiresAt = ParseTimestamp(authorization.ExpirationTime);
        var isFresh = authorization.Status == AuthorizationStatus.Created
            && (expiresAt is null || expiresAt.Value > DateTimeOffset.UtcNow.AddMinutes(5));

        return new AuthorizationFreshnessResult(isFresh, authorization.Status?.Value ?? "UNKNOWN", expiresAt);
    }

    public async Task<ReauthorizationResult> ReauthorizeAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        PaymentAuthorization reauthorized;
        try
        {
            reauthorized = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            var description =
                ex.Error.TryGetError(out var err) ? DescribeError(err) :
                ex.Error.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) :
                ex.Error.TryGetRawError(out var raw) ? DescribeRaw(raw) : "unknown error";

            throw new PaymentAuthorizationNotRenewableException(
                $"PayPal could not renew authorization {authorizationId}: {description}. " +
                "This authorization can no longer be captured; cancel the order and have the shopper pay again.");
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while renewing the authorization.", ex);
        }

        return new ReauthorizationResult(
            reauthorized.Id!,
            reauthorized.Status?.Value ?? "UNKNOWN",
            ParseTimestamp(reauthorized.ExpirationTime));
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = new CaptureRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) },
            FinalCapture = true
        };

        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw Translate("capture payment", ex.Error, e =>
                e.TryGetError(out var err) ? DescribeError(err) :
                e.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while capturing the payment.", ex);
        }

        var breakdown = captured.SellerReceivableBreakdown;
        return new CaptureResult(
            captured.Id!,
            captured.Status?.Value ?? "UNKNOWN",
            ParseMoney(breakdown?.GrossAmount),
            ParseMoney(breakdown?.PaypalFee),
            ParseMoney(breakdown?.NetAmount));
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw Translate("void authorization", ex.Error, e =>
                e.TryGetError(out var err) ? DescribeError(err) :
                e.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) : null);
        }
        catch (JsonException)
        {
            // PayPal returns 204 No Content on a successful void, but this SDK build's generated
            // VoidPayment always tries to deserialize a PaymentAuthorization body regardless of an
            // empty response, so a genuinely successful void throws here too. Confirm what actually
            // happened by re-reading the authorization instead of assuming either outcome.
            PaymentAuthorization authorization;
            try
            {
                authorization = await _client.Payments.GetAuthorizedPayment(
                    authorizationId: authorizationId,
                    payPalMockResponse: null,
                    payPalAuthAssertion: null,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<GetAuthorizedPaymentError> getEx)
            {
                throw Translate("void authorization", getEx.Error, e =>
                    e.TryGetError(out var err) ? DescribeError(err) :
                    e.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) : null);
            }

            if (authorization.Status != AuthorizationStatus.Voided)
            {
                throw new PaymentProviderException($"PayPal returned a response that could not be processed while releasing the hold on authorization {authorizationId} (status is now {authorization.Status?.Value}).");
            }
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, decimal amount, string currency, string idempotencyKey, CancellationToken ct)
    {
        var body = new RefundRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = FormatAmount(amount) }
        };

        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw Translate("refund payment", ex.Error, e =>
                e.TryGetError(out var err) ? DescribeError(err) :
                e.TryGetNoContent(out var noContent) ? DescribeRaw(noContent) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while refunding the payment.", ex);
        }

        return new RefundResult(refund.Id!, refund.Status?.Value ?? "UNKNOWN", ParseMoney(refund.Amount));
    }

    public async Task<SavedCardResult> SaveCardAsync(CardDetails card, string idempotencyKey, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = new Address
                    {
                        AddressLine1 = card.AddressLine1,
                        AdminArea2 = card.City,
                        PostalCode = card.PostalCode,
                        CountryCode = card.CountryCode
                    }
                }
            }
        };

        PaymentTokenResponse token;
        try
        {
            token = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw Translate("save card", ex.Error, e => e.TryGetError1(out var err) ? DescribeError1(err) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while saving the card.", ex);
        }

        var savedCard = token.PaymentSource?.Card;
        return new SavedCardResult(
            token.Id!,
            savedCard?.Brand?.Value ?? "UNKNOWN",
            savedCard?.LastDigits ?? string.Empty,
            savedCard?.Expiry ?? string.Empty);
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, requestOptions: null, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw Translate("delete saved card", ex.Error, e => e.TryGetError1(out var err) ? DescribeError1(err) : null);
        }
        catch (JsonException ex)
        {
            throw new PaymentProviderException("PayPal returned a response that could not be processed while deleting the saved card.", ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        var startDate = from.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
        var endDate = to.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

        var page = 1;
        int totalPages;
        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: startDate,
                    endDate: endDate,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    requestOptions: null,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PaymentProviderException($"PayPal transaction search failed: {DescribeRaw(ex.Error)}", ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentProviderException("PayPal returned a response that could not be processed while searching transactions.", ex);
            }

            foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                results.Add(new PayPalTransaction(
                    info?.TransactionId,
                    ParseMoney(info?.TransactionAmount),
                    info?.TransactionAmount?.CurrencyCode,
                    info?.TransactionStatus,
                    ParseTimestamp(info?.TransactionInitiationDate)));
            }

            totalPages = response.TotalPages ?? page;
            page++;
        } while (page <= totalPages);

        return results;
    }

    private static OrderAuthorizeRequestPaymentSource BuildPaymentSource(AuthorizePaymentRequest request)
    {
        if (request.Card is { } card)
        {
            return new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Name = card.Name,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = new Address
                    {
                        AddressLine1 = card.AddressLine1,
                        AdminArea2 = card.City,
                        PostalCode = card.PostalCode,
                        CountryCode = card.CountryCode
                    }
                }
            };
        }

        return new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest
            {
                VaultId = request.VaultId,
                StoredCredential = new CardStoredCredential
                {
                    PaymentInitiator = PaymentInitiator.Merchant,
                    PaymentType = StoredPaymentSourcePaymentType.Unscheduled,
                    Usage = StoredPaymentSourceUsageType.Subsequent
                }
            }
        };
    }

    private static string FormatAmount(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

    private static decimal ParseMoney(Money? money) =>
        money?.Value is { } value && decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0m;

    private static DateTimeOffset? ParseTimestamp(string? value) =>
        !string.IsNullOrEmpty(value) && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static string DescribeError(Error error)
    {
        var issues = error.Details is null ? string.Empty : string.Join(", ", error.Details.Select(d => d.Issue));
        return string.IsNullOrEmpty(issues) ? $"{error.Name}: {error.Message}" : $"{error.Name}: {error.Message} ({issues})";
    }

    private static string DescribeError1(Error1 error)
    {
        var issues = error.Details is null ? string.Empty : string.Join(", ", error.Details.Select(d => d.Issue));
        return string.IsNullOrEmpty(issues) ? $"{error.Name}: {error.Message}" : $"{error.Name}: {error.Message} ({issues})";
    }

    private static string DescribeRaw(RawError raw) => $"HTTP {(int)raw.StatusCode}: {raw.ReadAsString()}";

    private static PaymentProviderException Translate<TError>(string operation, TError error, Func<TError, string?> describeTyped)
        where TError : ApiError
    {
        var description = describeTyped(error);
        if (description is null && error.TryGetRawError(out var raw))
        {
            description = DescribeRaw(raw);
        }

        return new PaymentProviderException($"PayPal {operation} failed: {description ?? "unknown error"}");
    }
}
