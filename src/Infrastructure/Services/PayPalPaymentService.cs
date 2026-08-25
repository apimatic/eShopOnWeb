using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalPaymentService(PayPalServerSdkClient client, PayPalSettings settings)
    {
        _client = client;
        _currency = settings.Currency;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(int orderId, decimal amount, string currency, CardDetails card, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(orderId, amount, currency, ct);
        return await AuthorizeOrderAsync(orderId, payPalOrderId, card: card, vaultId: null, ct: ct);
    }

    public async Task<AuthorizeResult> AuthorizeWithVaultAsync(int orderId, decimal amount, string currency, string vaultId, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(orderId, amount, currency, ct);
        return await AuthorizeOrderAsync(orderId, payPalOrderId, card: null, vaultId: vaultId, ct: ct);
    }

    private async Task<string> CreatePayPalOrderAsync(int orderId, decimal amount, string currency, CancellationToken ct)
    {
        try
        {
            var body = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest>
                {
                    new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = amount.ToString("F2")
                        },
                        CustomId = orderId.ToString()
                    }
                }
            };

            var result = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return result.Id ?? throw new PayPalException("PayPal did not return an order ID.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    private async Task<AuthorizeResult> AuthorizeOrderAsync(int orderId, string payPalOrderId, CardDetails? card, string? vaultId, CancellationToken ct)
    {
        CardRequest cardRequest;
        if (vaultId != null)
        {
            cardRequest = new CardRequest { VaultId = vaultId };
        }
        else if (card != null)
        {
            cardRequest = new CardRequest
            {
                Number = card.Number,
                Expiry = card.Expiry,
                SecurityCode = card.SecurityCode,
                Name = card.Name,
                BillingAddress = card.CountryCode != null
                    ? new Address { CountryCode = card.CountryCode }
                    : null
            };
        }
        else
        {
            throw new ArgumentException("Either card or vaultId must be provided.");
        }

        try
        {
            var authBody = new OrderAuthorizeRequest
            {
                PaymentSource = new OrderAuthorizeRequestPaymentSource
                {
                    Card = cardRequest
                }
            };

            var authResponse = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: $"auth-{orderId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authBody,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            if (authResponse.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException("This card requires browser-based 3D Secure verification, which is not supported in this flow.");

            var authId = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
                ?? throw new PayPalException("PayPal did not return an authorization ID.");

            var expiryStr = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.ExpirationTime;
            DateTimeOffset? expiresAt = expiryStr != null
                ? DateTimeOffset.Parse(expiryStr)
                : null;

            return new AuthorizeResult(payPalOrderId, authId, expiresAt);
        }
        catch (PayPalException) { throw; }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal authorization response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var captureId = result.Id ?? throw new PayPalException("PayPal did not return a capture ID.");
            var breakdown = result.SellerReceivableBreakdown;
            var gross = ParseMoney(breakdown?.GrossAmount);
            var fee = breakdown?.PaypalFee != null ? ParseMoney(breakdown.PaypalFee) : (decimal?)null;
            var net = breakdown?.NetAmount != null ? ParseMoney(breakdown.NetAmount) : (decimal?)null;

            return new CaptureResult(captureId, gross, fee, net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal capture response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<string> ReauthorizeAsync(string authorizationId, decimal amount, string currency, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{authorizationId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = currency, Value = amount.ToString("F2") }
                },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            return result.Id ?? throw new PayPalException("PayPal did not return a new authorization ID.");
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorizeError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal reauthorize response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task VoidAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateVoidError(ex);
        }
        catch (JsonException)
        {
            // VoidPayment returns 204 No Content on success; the SDK throws JsonException
            // when it tries to deserialize the empty body — treat this as success.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(string captureId, string idempotencyKey, decimal? amount, string currency, CancellationToken ct = default)
    {
        try
        {
            RefundRequest? body = amount.HasValue
                ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amount.Value.ToString("F2") } }
                : null;

            var result = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var refundId = result.Id ?? throw new PayPalException("PayPal did not return a refund ID.");
            var refundedAmount = ParseMoney(result.Amount);

            return new RefundResult(refundId, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal refund response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<VaultResult> VaultCardAsync(string merchantCustomerId, CardDetails card, CancellationToken ct = default)
    {
        try
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
                        Name = card.Name,
                        BillingAddress = card.CountryCode != null
                            ? new Address { CountryCode = card.CountryCode }
                            : null
                    }
                }
            };

            var result = await _client.Vault.CreatePaymentToken(
                payPalRequestId: $"vault-{merchantCustomerId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                body: body,
                requestOptions: null,
                ct: ct);

            var vaultId = result.Id ?? throw new PayPalException("PayPal did not return a vault ID.");
            var cardEntity = result.PaymentSource?.Card;

            return new VaultResult(
                vaultId,
                cardEntity?.LastDigits,
                cardEntity?.Brand?.Value,
                cardEntity?.Expiry,
                cardEntity?.Name);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal vault response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<IReadOnlyList<VaultedCard>> ListVaultedCardsAsync(string merchantCustomerId, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Vault.ListCustomerPaymentTokens(
                customerId: merchantCustomerId,
                pageSize: 100,
                page: 1,
                totalRequired: false,
                requestOptions: null,
                ct: ct);

            return (result.PaymentTokens ?? Enumerable.Empty<PaymentTokenResponse>())
                .Select(t =>
                {
                    var card = t.PaymentSource?.Card;
                    return new VaultedCard(
                        t.Id ?? string.Empty,
                        card?.LastDigits,
                        card?.Brand?.Value,
                        card?.Expiry,
                        card?.Name);
                })
                .ToList();
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            throw TranslateListVaultError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal vault list response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateDeleteVaultError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The PayPal delete vault response could not be processed.", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is currently unavailable.", ex);
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var all = new List<TransactionRecord>();
        int page = 1;
        int totalPages = 1;

        do
        {
            try
            {
                var result = await _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    endDate: to.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
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

                if (result.TransactionDetails != null)
                {
                    foreach (var td in result.TransactionDetails)
                    {
                        var info = td.TransactionInfo;
                        all.Add(new TransactionRecord(
                            info?.TransactionId,
                            info?.TransactionStatus,
                            info?.TransactionAmount != null ? ParseMoney(info.TransactionAmount) : null,
                            info?.TransactionAmount?.CurrencyCode,
                            info?.FeeAmount != null ? ParseMoney(info.FeeAmount) : null,
                            info?.CustomField,
                            info?.TransactionInitiationDate));
                    }
                }

                if (result.TotalPages.HasValue && result.TotalPages.Value > 0)
                    totalPages = result.TotalPages.Value;

                page++;
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalException($"PayPal transaction search failed: HTTP {(int)ex.Error.StatusCode}", ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalException("The PayPal transaction search response could not be processed.", ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("PayPal is currently unavailable.", ex);
            }
        } while (page <= totalPages);

        return all;
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value == null) return 0m;
        return decimal.TryParse(money.Value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static PayPalException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal create order failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal create order failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal create order failed.", ex);
    }

    private static PayPalException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal authorization failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal authorization failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal authorization failed.", ex);
    }

    private static PayPalException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal capture failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal capture failed with an internal server error.", ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal capture failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal capture failed.", ex);
    }

    private static PayPalException TranslateReauthorizeError(SdkException<ReauthorizePaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal reauthorization failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal reauthorization failed with an internal server error.", ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal reauthorization failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal reauthorization failed.", ex);
    }

    private static PayPalException TranslateVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal void failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal void failed with an internal server error.", ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal void failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal void failed.", ex);
    }

    private static PayPalException TranslateRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
            return new PayPalException($"PayPal refund failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("PayPal refund failed with an internal server error.", ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal refund failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal refund failed.", ex);
    }

    private static PayPalException TranslateVaultError(SdkException<CreatePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
            return new PayPalException($"PayPal vault failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal vault failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal vault failed.", ex);
    }

    private static PayPalException TranslateListVaultError(SdkException<ListCustomerPaymentTokensError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
            return new PayPalException($"PayPal list vault failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal list vault failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal list vault failed.", ex);
    }

    private static PayPalException TranslateDeleteVaultError(SdkException<DeletePaymentTokenError> ex)
    {
        if (ex.Error.TryGetError1(out var error))
            return new PayPalException($"PayPal delete vault failed: {error.Message}", ex) { IsClientError = true };
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"PayPal delete vault failed: HTTP {(int)raw.StatusCode}", ex);
        return new PayPalException("PayPal delete vault failed.", ex);
    }
}

public class PayPalException : Exception
{
    public bool IsClientError { get; init; }

    public PayPalException(string message) : base(message) { }
    public PayPalException(string message, Exception inner) : base(message, inner) { }
}
