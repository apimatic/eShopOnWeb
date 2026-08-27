using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// IPaymentGateway implementation over the PayPal Server SDK. All contract facts
/// (signatures, wire names, error accessors) come from paypal-plan.md.
/// Full card details flow through to PayPal only; they are never persisted or logged here.
/// </summary>
public class PayPalPaymentGateway : IPaymentGateway
{
    private static readonly TimeSpan CallBudget = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan MaxTransactionSearchWindow = TimeSpan.FromDays(31);

    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGateway(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<GatewayAuthorization> AuthorizeCardPaymentAsync(
        decimal amount, string currency, string referenceId, CardPaymentDetails card,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = BuildAuthorizeOrderRequest(amount, currency, referenceId,
            new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            });

        try
        {
            var order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            GuardAgainstPayerAction(order);
            return ToGatewayAuthorization(order, amount, currency);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToGatewayException(ex.Error, "create the PayPal order", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, "create the PayPal order");
        }
    }

    public async Task<GatewayAuthorization> AuthorizeVaultedCardPaymentAsync(
        decimal amount, string currency, string referenceId, string vaultId,
        string idempotencyKey, CancellationToken ct = default)
    {
        var body = BuildAuthorizeOrderRequest(amount, currency, referenceId,
            new PaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = vaultId
                }
            });

        try
        {
            var order = await Bounded(c => _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            GuardAgainstPayerAction(order);
            return ToGatewayAuthorization(order, amount, currency);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw ToGatewayException(ex.Error, "pay with the saved card", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, "pay with the saved card");
        }
    }

    public async Task<GatewayAuthorizationInfo> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: c), ct);

            return ToGatewayAuthorizationInfo(authorization);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error, $"read authorization {authorizationId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"read authorization {authorizationId}");
        }
    }

    public async Task<GatewayAuthorizationInfo> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var authorization = await Bounded(c => _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount) }
                },
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            return ToGatewayAuthorizationInfo(authorization);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // Any typed rejection here means the hold could not be renewed; surface PayPal's
            // own reason so an operator can act on it (typically: ask the shopper to pay again).
            if (ex.Error.TryGetError(out var error))
            {
                throw new PaymentStateException(
                    $"The PayPal authorization {authorizationId} can no longer be renewed: {Describe(error)} " +
                    "Cancel the order and ask the shopper to pay again.");
            }
            throw ToGatewayException(ex.Error, $"renew authorization {authorizationId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"renew authorization {authorizationId}");
        }
    }

    public async Task<GatewayCapture> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var capture = await Bounded(c => _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            var breakdown = capture.SellerReceivableBreakdown;
            return new GatewayCapture(
                CaptureId: capture.Id ?? string.Empty,
                Status: capture.Status?.Value ?? string.Empty,
                Amount: ParseMoney(capture.Amount?.Value) ?? 0m,
                Currency: capture.Amount?.CurrencyCode ?? string.Empty,
                PayPalFee: ParseMoney(breakdown?.PaypalFee?.Value),
                NetAmount: ParseMoney(breakdown?.NetAmount?.Value));
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error, $"capture authorization {authorizationId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"capture authorization {authorizationId}");
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: idempotencyKey,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw ToGatewayException(ex.Error, $"release authorization {authorizationId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"release authorization {authorizationId}");
        }
    }

    public async Task<GatewayRefund> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
    {
        // An empty body refunds the full captured amount; setting Amount makes it partial.
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = FormatMoney(amount.Value) } }
            : new RefundRequest();

        try
        {
            var refund = await Bounded(c => _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: c), ct);

            return new GatewayRefund(
                RefundId: refund.Id ?? string.Empty,
                Status: refund.Status?.Value ?? string.Empty,
                Amount: ParseMoney(refund.Amount?.Value),
                Currency: refund.Amount?.CurrencyCode);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw ToGatewayException(ex.Error, $"refund capture {captureId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"refund capture {captureId}");
        }
    }

    public async Task<GatewaySavedCard> SaveCardAsync(
        string merchantCustomerId, CardPaymentDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = merchantCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.CardholderName,
                    BillingAddress = ToSdkAddress(card.BillingAddress)
                }
            }
        };

        try
        {
            var token = await Bounded(c => _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                requestOptions: null,
                ct: c), ct);

            return new GatewaySavedCard(
                VaultId: token.Id ?? string.Empty,
                PayPalCustomerId: token.Customer?.Id,
                Brand: token.PaymentSource?.Card?.Brand?.Value,
                LastDigits: token.PaymentSource?.Card?.LastDigits,
                Expiry: token.PaymentSource?.Card?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw ToGatewayException(ex.Error, "save the card", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, "save the card");
        }
    }

    public async Task DeleteSavedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await Bounded(c => _client.Vault.DeletePaymentToken(
                id: vaultId,
                requestOptions: null,
                ct: c), ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw ToGatewayException(ex.Error, $"delete saved card {vaultId}", ex);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
        {
            throw ToBoundaryException(ex, $"delete saved card {vaultId}");
        }
    }

    public async Task<IReadOnlyList<GatewayTransaction>> SearchTransactionsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var results = new List<GatewayTransaction>();

        // The transaction search API supports at most a 31-day range per call.
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart + MaxTransactionSearchWindow;
            if (windowEnd > to) windowEnd = to;

            var page = 1;
            while (true)
            {
                SearchResponse response;
                try
                {
                    response = await Bounded(c => _client.TransactionSearch.SearchTransactions(
                        startDate: FormatSearchDate(windowStart),
                        endDate: FormatSearchDate(windowEnd),
                        transactionId: null,
                        transactionType: null,
                        transactionStatus: null,
                        transactionAmount: null,
                        transactionCurrency: null,
                        paymentInstrumentType: null,
                        storeId: null,
                        terminalId: null,
                        fields: "transaction_info",
                        pageSize: 100,
                        page: page,
                        requestOptions: null,
                        ct: c), ct);
                }
                catch (SdkException<RawError> ex)
                {
                    throw new PaymentGatewayException(
                        $"PayPal transaction search failed: HTTP {(int)ex.Error.StatusCode} {ex.Error.ReadAsString()}",
                        (int)ex.Error.StatusCode, ex);
                }
                catch (Exception ex) when (ex is JsonException or HttpRequestException or TaskCanceledException)
                {
                    throw ToBoundaryException(ex, "search PayPal transactions");
                }

                if (response.TransactionDetails is not null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info is null) continue;

                        results.Add(new GatewayTransaction(
                            TransactionId: info.TransactionId ?? string.Empty,
                            ReferenceId: info.PaypalReferenceId,
                            ReferenceIdType: info.PaypalReferenceIdType?.Value,
                            EventCode: info.TransactionEventCode,
                            Status: info.TransactionStatus,
                            Amount: ParseMoney(info.TransactionAmount?.Value),
                            Currency: info.TransactionAmount?.CurrencyCode,
                            Fee: ParseMoney(info.FeeAmount?.Value),
                            InitiatedAt: ParseDate(info.TransactionInitiationDate),
                            UpdatedAt: ParseDate(info.TransactionUpdatedDate),
                            InvoiceId: info.InvoiceId));
                    }
                }

                var totalPages = response.TotalPages ?? page;
                if (page >= totalPages) break;
                page++;
            }

            windowStart = windowEnd;
        }

        return results;
    }

    private static OrderRequest BuildAuthorizeOrderRequest(
        decimal amount, string currency, string referenceId, PaymentSource paymentSource)
    {
        return new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    ReferenceId = referenceId,
                    CustomId = referenceId,
                    InvoiceId = referenceId,
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = currency,
                        Value = FormatMoney(amount)
                    }
                }
            },
            PaymentSource = paymentSource
        };
    }

    private GatewayAuthorization ToGatewayAuthorization(Order order, decimal amount, string currency)
    {
        var authorization = order.PurchaseUnits?
            .SelectMany(u => u.Payments?.Authorizations ?? Enumerable.Empty<AuthorizationWithAdditionalData>())
            .FirstOrDefault();

        if (order.Id is null || authorization?.Id is null)
        {
            throw new PaymentGatewayException(
                "PayPal approved the payment request but returned no authorization; the order state is unknown and must be checked in PayPal.");
        }

        return new GatewayAuthorization(
            PayPalOrderId: order.Id,
            AuthorizationId: authorization.Id,
            Status: authorization.Status?.Value ?? string.Empty,
            Amount: ParseMoney(authorization.Amount?.Value) ?? amount,
            Currency: authorization.Amount?.CurrencyCode ?? currency,
            ExpiresAt: ParseDate(authorization.ExpirationTime));
    }

    private static GatewayAuthorizationInfo ToGatewayAuthorizationInfo(PaymentAuthorization authorization)
    {
        return new GatewayAuthorizationInfo(
            AuthorizationId: authorization.Id ?? string.Empty,
            Status: authorization.Status?.Value ?? string.Empty,
            Amount: ParseMoney(authorization.Amount?.Value),
            Currency: authorization.Amount?.CurrencyCode,
            ExpiresAt: ParseDate(authorization.ExpirationTime));
    }

    /// <summary>
    /// PayPal can answer a card payment with a challenge that requires the shopper to approve
    /// in a browser (3DS / contingency). This integration does not build an approval
    /// round-trip; the condition is reported instead.
    /// </summary>
    private static void GuardAgainstPayerAction(Order order)
    {
        if (order.Status == OrderStatus.PayerActionRequired)
        {
            throw new PayerActionRequiredException(
                "PayPal requires the shopper to approve this payment in a browser (3D Secure challenge). " +
                "This integration does not support an approval round-trip; the payment was not completed.");
        }

        if (order.Links is not null && order.Links.Any(l =>
                l.Rel is not null &&
                (l.Rel.Contains("payer-action", StringComparison.OrdinalIgnoreCase) ||
                 l.Rel.Contains("approve", StringComparison.OrdinalIgnoreCase))))
        {
            throw new PayerActionRequiredException(
                "PayPal returned a payer-approval step for this card payment. " +
                "This integration does not support an approval round-trip; the payment was not completed.");
        }

        var authResult = order.PaymentSource?.Card?.AuthenticationResult;
        var threeDSecure = authResult?.ThreeDSecure;
        if (threeDSecure?.EnrollmentStatus == EnrollmentStatus.Y &&
            threeDSecure.AuthenticationStatus != ParesStatus.Y &&
            threeDSecure.AuthenticationStatus != ParesStatus.A)
        {
            throw new PayerActionRequiredException(
                "The card is enrolled in 3D Secure and the authentication did not complete. " +
                "This integration does not support an approval round-trip; the payment was not completed.");
        }

        if (authResult?.LiabilityShift == LiabilityShiftIndicator.No && threeDSecure is not null)
        {
            throw new PayerActionRequiredException(
                "PayPal reported no liability shift for this card authentication. " +
                "This integration does not support an approval round-trip; the payment was not completed.");
        }
    }

    private static Address? ToSdkAddress(GatewayAddress? address)
    {
        if (address is null) return null;

        return new Address
        {
            CountryCode = address.CountryCode,
            AddressLine1 = address.AddressLine1,
            AddressLine2 = address.AddressLine2,
            AdminArea2 = address.City,
            AdminArea1 = address.State,
            PostalCode = address.PostalCode
        };
    }

    private static async Task<T> Bounded<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        return await call(cts.Token);
    }

    private static async Task Bounded(Func<CancellationToken, Task> call, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CallBudget);
        await call(cts.Token);
    }

    private static string FormatMoney(decimal amount) =>
        amount.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal? ParseMoney(string? value) =>
        decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed : null;

    private static string FormatSearchDate(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Describe(Error error)
    {
        var issues = error.Details is null
            ? string.Empty
            : " Issues: " + string.Join("; ", error.Details.Select(d => $"{d.Issue} {d.Description}".Trim()));
        return $"{error.Name}: {error.Message} (debug id: {error.DebugId}).{issues}";
    }

    private static string Describe(Error1 error)
    {
        var issues = error.Details is null
            ? string.Empty
            : " Issues: " + string.Join("; ", error.Details.Select(d => $"{d.Issue} {d.Description}".Trim()));
        return $"{error.Name}: {error.Message} (debug id: {error.DebugId}).{issues}";
    }

    // One ladder per operation error type: every TryGet* accessor the type declares gets a
    // branch, most specific first, TryGetRawError always last (it only fires for statuses
    // with no more-specific accessor).
    private static PaymentGatewayException ToGatewayException(CreateOrderError error, string action, Exception inner)
    {
        if (error.TryGetError(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetRawError(out var raw))
            return RawFailure(raw, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(GetAuthorizedPaymentError error, string action, Exception inner)
    {
        if (error.TryGetError(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetNoContent(out var raw))
            return RawFailure(raw, action, inner);
        if (error.TryGetRawError(out var fallback))
            return RawFailure(fallback, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(ReauthorizePaymentError error, string action, Exception inner)
    {
        if (error.TryGetNoContent(out var raw))
            return RawFailure(raw, action, inner);
        if (error.TryGetRawError(out var fallback))
            return RawFailure(fallback, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(CaptureAuthorizedPaymentError error, string action, Exception inner)
    {
        if (error.TryGetError(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetNoContent(out var raw))
            return RawFailure(raw, action, inner);
        if (error.TryGetRawError(out var fallback))
            return RawFailure(fallback, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(VoidPaymentError error, string action, Exception inner)
    {
        if (error.TryGetError(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetNoContent(out var raw))
            return RawFailure(raw, action, inner);
        if (error.TryGetRawError(out var fallback))
            return RawFailure(fallback, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(RefundCapturedPaymentError error, string action, Exception inner)
    {
        if (error.TryGetError(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetNoContent(out var raw))
            return RawFailure(raw, action, inner);
        if (error.TryGetRawError(out var fallback))
            return RawFailure(fallback, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(CreatePaymentTokenError error, string action, Exception inner)
    {
        if (error.TryGetError1(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetRawError(out var raw))
            return RawFailure(raw, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException ToGatewayException(DeletePaymentTokenError error, string action, Exception inner)
    {
        if (error.TryGetError1(out var e))
            return new PaymentGatewayException($"PayPal could not {action}: {Describe(e)}", null, inner);
        if (error.TryGetRawError(out var raw))
            return RawFailure(raw, action, inner);
        return UnknownFailure(action, inner);
    }

    private static PaymentGatewayException RawFailure(RawError raw, string action, Exception inner) =>
        new PaymentGatewayException(
            $"PayPal could not {action}: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}",
            (int)raw.StatusCode, inner);

    private static PaymentGatewayException UnknownFailure(string action, Exception inner) =>
        new PaymentGatewayException($"PayPal could not {action}.", null, inner);

    private static PaymentGatewayException ToBoundaryException(Exception ex, string action)
    {
        return ex switch
        {
            JsonException => new PaymentGatewayException(
                $"PayPal returned a response that could not be processed while trying to {action}.", null, ex),
            HttpRequestException => new PaymentGatewayException(
                $"PayPal was unreachable while trying to {action}; the outcome is unknown and must be verified before retrying.", null, ex),
            TaskCanceledException => new PaymentGatewayException(
                $"The call to PayPal timed out while trying to {action}; the outcome is unknown and must be verified before retrying.", null, ex),
            _ => new PaymentGatewayException($"PayPal could not {action}.", null, ex)
        };
    }
}
