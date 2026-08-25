using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// The sole adapter between ApplicationCore's <see cref="IPaymentGatewayService"/> and the
/// PayPal .NET SDK (PayPalServerSdk). Every PayPal call in this integration goes through here.
/// </summary>
public class PayPalPaymentGatewayService : IPaymentGatewayService
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentGatewayService(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(PaymentAmount amount, CardDetails card, string requestId, CancellationToken ct)
    {
        var cardRequest = new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = BuildAddress(card.BillingAddress)
        };
        return CreateOrderAndAuthorizeAsync(amount, cardRequest, requestId, ct);
    }

    public Task<PaymentAuthorizationResult> AuthorizeWithVaultedCardAsync(PaymentAmount amount, string vaultId, string requestId, CancellationToken ct)
    {
        var cardRequest = new CardRequest { VaultId = vaultId };
        return CreateOrderAndAuthorizeAsync(amount, cardRequest, requestId, ct);
    }

    private async Task<PaymentAuthorizationResult> CreateOrderAndAuthorizeAsync(PaymentAmount amount, CardRequest cardRequest, string requestId, CancellationToken ct)
    {
        var orderRequest = new OrderRequest
        {
            Intent = CheckoutPaymentIntent.Authorize,
            PurchaseUnits = new List<PurchaseUnitRequest>
            {
                new PurchaseUnitRequest
                {
                    Amount = new AmountWithBreakdown
                    {
                        CurrencyCode = amount.CurrencyCode,
                        Value = FormatAmount(amount.Value)
                    }
                }
            },
            PaymentSource = new PaymentSource { Card = cardRequest }
        };

        Order response;
        try
        {
            response = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw DescribeCreateOrderFailure(ex, "authorize the payment");
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while authorizing the payment.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while authorizing the payment.", isProviderRejection: false, ex);
        }

        if (response.Status == OrderStatus.PayerActionRequired)
        {
            throw new PaymentActionRequiredException();
        }

        var authorization = response.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
        if (authorization is null || string.IsNullOrEmpty(authorization.Id))
        {
            throw new PaymentGatewayException("PayPal did not return a payment authorization for this order.", isProviderRejection: false);
        }

        // An idempotent replay of PayPal-Request-Id can return an authorization that has already
        // moved past CREATED/PENDING (e.g. if the same request id was ever reused). Accepting that
        // as a fresh hold would silently hand back money that is not actually held any more.
        if (authorization.Status != AuthorizationStatus.Created && authorization.Status != AuthorizationStatus.Pending)
        {
            throw new PaymentGatewayException(
                $"PayPal returned an authorization that is not usable as a fresh hold (status: {authorization.Status?.Value ?? "UNKNOWN"}). This usually means the idempotency key for this request was already used against a different authorization.",
                isProviderRejection: false);
        }

        return new PaymentAuthorizationResult(
            PayPalOrderId: response.Id ?? string.Empty,
            AuthorizationId: authorization.Id,
            Status: authorization.Status?.Value ?? "UNKNOWN",
            ExpiresAt: ParseExpiration(authorization.ExpirationTime),
            RequiresShopperAction: false);
    }

    public async Task<PaymentAuthorizationStatusResult> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        PaymentAuthorization response;
        try
        {
            response = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw DescribeFailure(
                "check the payment authorization status",
                ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while checking the authorization.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while checking the authorization.", isProviderRejection: false, ex);
        }

        return new PaymentAuthorizationStatusResult(
            AuthorizationId: response.Id ?? authorizationId,
            Status: response.Status?.Value ?? "UNKNOWN",
            ExpiresAt: ParseExpiration(response.ExpirationTime));
    }

    public async Task<PaymentCaptureResult> CaptureAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        CapturedPayment response;
        try
        {
            response = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw DescribeFailure(
                "capture the payment",
                ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while capturing the payment.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while capturing the payment.", isProviderRejection: false, ex);
        }

        var breakdown = response.SellerReceivableBreakdown;
        return new PaymentCaptureResult(
            CaptureId: response.Id ?? string.Empty,
            Status: response.Status?.Value ?? "UNKNOWN",
            CapturedAmount: ParseAmount(response.Amount),
            FeeAmount: breakdown?.PaypalFee is null ? null : ParseAmount(breakdown.PaypalFee),
            NetAmount: breakdown?.NetAmount is null ? null : ParseAmount(breakdown.NetAmount));
    }

    public async Task<PaymentAuthorizationStatusResult> ReauthorizeAsync(string authorizationId, PaymentAmount amount, string requestId, CancellationToken ct)
    {
        PaymentAuthorization response;
        try
        {
            response = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: requestId,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = amount.CurrencyCode, Value = FormatAmount(amount.Value) }
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            // The reauthorization window (days 4-29) has no typed "too old to renew" status in this
            // SDK - any rejection here is surfaced as "no longer renewable", operator-actionable.
            if (ex.Error.TryGetError(out var err))
            {
                throw new PaymentAuthorizationNotRenewableException(DescribeError(err));
            }
            if (ex.Error.TryGetNoContent(out var noContent))
            {
                throw new PaymentAuthorizationNotRenewableException($"HTTP {(int)noContent.StatusCode}");
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentAuthorizationNotRenewableException(raw.ReadAsString());
            }
            throw new PaymentAuthorizationNotRenewableException(ex.Message);
        }
        catch (JsonException ex)
        {
            throw new PaymentAuthorizationNotRenewableException($"PayPal returned a response that could not be processed: {ex.Message}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while renewing the authorization.", isProviderRejection: false, ex);
        }

        return new PaymentAuthorizationStatusResult(
            AuthorizationId: response.Id ?? authorizationId,
            Status: response.Status?.Value ?? "UNKNOWN",
            ExpiresAt: ParseExpiration(response.ExpirationTime));
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string requestId, CancellationToken ct)
    {
        try
        {
            _ = await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: requestId,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw DescribeFailure(
                "cancel the payment hold",
                ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while cancelling the payment hold.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while cancelling the payment hold.", isProviderRejection: false, ex);
        }
    }

    public async Task<PaymentRefundResult> RefundCaptureAsync(string captureId, PaymentAmount? amount, string idempotencyKey, CancellationToken ct)
    {
        var body = amount is null
            ? null
            : new RefundRequest { Amount = new Money { CurrencyCode = amount.CurrencyCode, Value = FormatAmount(amount.Value) } };

        Refund response;
        try
        {
            response = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw DescribeFailure(
                "refund the payment",
                ex.Error.TryGetError, ex.Error.TryGetNoContent, ex.Error.TryGetRawError, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while refunding the payment.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while refunding the payment.", isProviderRejection: false, ex);
        }

        return new PaymentRefundResult(
            RefundId: response.Id ?? string.Empty,
            Status: response.Status?.Value ?? "UNKNOWN",
            Amount: ParseAmount(response.Amount));
    }

    public async Task<VaultedCardResult> SaveCardAsync(string customerId, CardDetails card, string requestId, CancellationToken ct)
    {
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { MerchantCustomerId = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = BuildAddress(card.BillingAddress)
                }
            }
        };

        PaymentTokenResponse response;
        try
        {
            response = await _client.Vault.CreatePaymentToken(payPalRequestId: requestId, body: body, ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
            {
                throw new PaymentGatewayException($"PayPal rejected saving the card: {DescribeError(err)}", isProviderRejection: true, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentGatewayException($"PayPal save-card failed (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", isProviderRejection: IsClientStatus(raw.StatusCode), ex);
            }
            throw new PaymentGatewayException("PayPal save-card failed.", isProviderRejection: false, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while saving the card.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while saving the card.", isProviderRejection: false, ex);
        }

        var cardEntity = response.PaymentSource?.Card;
        return new VaultedCardResult(
            VaultId: response.Id ?? string.Empty,
            CardBrand: cardEntity?.Brand?.Value,
            Last4: cardEntity?.LastDigits,
            Expiry: cardEntity?.Expiry,
            CardholderName: cardEntity?.Name);
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // An unknown/already-deleted token has no typed accessor for 404 - it falls to
            // TryGetRawError. Treat that as success: deleting an already-gone card is idempotent.
            if (ex.Error.TryGetRawError(out var notFound) && notFound.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return;
            }
            if (ex.Error.TryGetError1(out var err))
            {
                throw new PaymentGatewayException($"PayPal rejected deleting the card: {DescribeError(err)}", isProviderRejection: true, ex);
            }
            if (ex.Error.TryGetRawError(out var raw))
            {
                throw new PaymentGatewayException($"PayPal delete-card failed (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", isProviderRejection: IsClientStatus(raw.StatusCode), ex);
            }
            throw new PaymentGatewayException("PayPal delete-card failed.", isProviderRejection: false, ex);
        }
        catch (JsonException ex)
        {
            throw new PaymentGatewayException("PayPal returned a response that could not be processed while deleting the card.", isProviderRejection: false, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentGatewayException("PayPal was unreachable while deleting the card.", isProviderRejection: false, ex);
        }
    }

    public async Task<TransactionSearchResult> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var results = new List<PayPalTransaction>();
        var page = 1;
        int? totalPages;

        do
        {
            SearchResponse response;
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture),
                    endDate: to.ToString("yyyy-MM-ddTHH:mm:ss.fffK", CultureInfo.InvariantCulture),
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
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PaymentGatewayException(
                    $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}",
                    isProviderRejection: IsClientStatus(ex.Error.StatusCode), ex);
            }
            catch (JsonException ex)
            {
                throw new PaymentGatewayException("PayPal returned a transaction search response that could not be processed.", isProviderRejection: false, ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PaymentGatewayException("PayPal was unreachable while searching transactions.", isProviderRejection: false, ex);
            }

            foreach (var detail in response.TransactionDetails ?? Array.Empty<TransactionDetails>())
            {
                var info = detail.TransactionInfo;
                if (info is null)
                {
                    continue;
                }

                results.Add(new PayPalTransaction(
                    TransactionId: info.TransactionId ?? string.Empty,
                    Status: info.TransactionStatus,
                    Amount: info.TransactionAmount is null ? null : ParseAmount(info.TransactionAmount),
                    CurrencyCode: info.TransactionAmount?.CurrencyCode,
                    InitiatedAt: string.IsNullOrEmpty(info.TransactionInitiationDate)
                        ? null
                        : DateTimeOffset.Parse(info.TransactionInitiationDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    ReferenceId: info.PaypalReferenceId,
                    ReferenceIdType: info.PaypalReferenceIdType?.Value));
            }

            totalPages = response.TotalPages;
            page++;
        } while (totalPages.HasValue && page <= totalPages.Value);

        return new TransactionSearchResult(results);
    }

    private static PaymentGatewayException DescribeCreateOrderFailure(SdkException<CreateOrderError> ex, string action)
    {
        if (ex.Error.TryGetError(out var err))
        {
            return new PaymentGatewayException($"PayPal rejected the request to {action}: {DescribeError(err)}", isProviderRejection: true, ex);
        }
        if (ex.Error.TryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal failed to {action} (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", isProviderRejection: IsClientStatus(raw.StatusCode), ex);
        }
        return new PaymentGatewayException($"PayPal failed to {action}.", isProviderRejection: false, ex);
    }

    private static PaymentGatewayException DescribeFailure<TError>(
        string action,
        TryGetErrorFunc tryGetError,
        TryGetNoContentFunc tryGetNoContent,
        TryGetRawErrorFunc tryGetRawError,
        SdkException<TError> ex)
    {
        if (tryGetError(out var err))
        {
            return new PaymentGatewayException($"PayPal rejected the request to {action}: {DescribeError(err)}", isProviderRejection: true, ex);
        }
        if (tryGetNoContent(out var noContent))
        {
            return new PaymentGatewayException($"PayPal failed to {action} (HTTP {(int)noContent.StatusCode}).", isProviderRejection: false, ex);
        }
        if (tryGetRawError(out var raw))
        {
            return new PaymentGatewayException($"PayPal failed to {action} (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}", isProviderRejection: IsClientStatus(raw.StatusCode), ex);
        }
        return new PaymentGatewayException($"PayPal failed to {action}.", isProviderRejection: false, ex);
    }

    private delegate bool TryGetErrorFunc(out Error value);
    private delegate bool TryGetNoContentFunc(out RawError value);
    private delegate bool TryGetRawErrorFunc(out RawError value);

    private static string DescribeError(Error error)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrEmpty(i)).ToArray();
        return issues is { Length: > 0 } ? $"{error.Message} ({string.Join("; ", issues)})" : error.Message;
    }

    private static string DescribeError(Error1 error)
    {
        var issues = error.Details?.Select(d => d.Issue).Where(i => !string.IsNullOrEmpty(i)).ToArray();
        return issues is { Length: > 0 } ? $"{error.Message} ({string.Join("; ", issues)})" : error.Message;
    }

    private static bool IsClientStatus(System.Net.HttpStatusCode code) => (int)code is >= 400 and < 500;

    private static string FormatAmount(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    private static decimal ParseAmount(Money? money) =>
        money is null || string.IsNullOrEmpty(money.Value) ? 0m : decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseExpiration(string? expirationTime) =>
        string.IsNullOrEmpty(expirationTime) ? null : DateTimeOffset.Parse(expirationTime, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static Address? BuildAddress(BillingAddress? billingAddress)
    {
        if (billingAddress is null)
        {
            return null;
        }

        return new Address
        {
            AddressLine1 = billingAddress.AddressLine1,
            AddressLine2 = billingAddress.AddressLine2,
            AdminArea1 = billingAddress.AdminArea1,
            AdminArea2 = billingAddress.AdminArea2,
            PostalCode = billingAddress.PostalCode,
            CountryCode = billingAddress.CountryCode
        };
    }
}
