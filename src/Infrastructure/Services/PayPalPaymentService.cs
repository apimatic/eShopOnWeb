using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using PayPalServerSdk;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalPaymentService : IPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly ILogger<PayPalPaymentService> _logger;
    private readonly string _currency;

    public PayPalPaymentService(
        PayPalServerSdkClient client,
        ILogger<PayPalPaymentService> logger,
        IConfiguration config)
    {
        _client = client;
        _logger = logger;
        _currency = config["PayPal:Currency"] ?? "USD";
    }

    public async Task<PaymentAuthResult> AuthorizePaymentAsync(
        decimal amount,
        string currency,
        CardDetails? card,
        string? vaultToken,
        string eShopOrderId,
        CancellationToken ct = default)
    {
        PaymentSource? paymentSource = null;

        if (card != null)
        {
            paymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.Cvv,
                    Name = card.Name
                }
            };
        }
        else if (vaultToken != null)
        {
            paymentSource = new PaymentSource
            {
                Token = new Token
                {
                    Id = vaultToken,
                    Type = TokenType.BillingAgreement
                }
            };
        }

        // Step 1a: Create PayPal order with AUTHORIZE intent
        PayPalServerSdk.Models.Order paypalOrder;
        try
        {
            paypalOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{eShopOrderId}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = new List<PurchaseUnitRequest>
                    {
                        new PurchaseUnitRequest
                        {
                            Amount = new AmountWithBreakdown
                            {
                                CurrencyCode = currency,
                                Value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                            },
                            CustomId = eShopOrderId
                        }
                    },
                    PaymentSource = paymentSource
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during order creation.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        if (paypalOrder.Id == null)
            throw new PaymentException("PayPal did not return an order ID.", 502);

        // Step 1b: Authorize the order
        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: paypalOrder.Id,
                payPalMockResponse: null,
                payPalRequestId: $"auth-{eShopOrderId}",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex.Error, vaultToken != null);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during authorization.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        var auth = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0];
        if (auth?.Id == null)
            throw new PaymentException("PayPal authorization did not return an authorization ID.", 502);

        DateTimeOffset? expiry = null;
        DateTimeOffset? createdAt = null;

        if (auth.ExpirationTime != null &&
            DateTimeOffset.TryParse(auth.ExpirationTime, out var parsedExpiry))
            expiry = parsedExpiry;

        if (auth.CreateTime != null &&
            DateTimeOffset.TryParse(auth.CreateTime, out var parsedCreated))
            createdAt = parsedCreated;

        _logger.LogInformation("Payment authorized: paypalOrderId={PayPalOrderId} authId={AuthId}",
            paypalOrder.Id, auth.Id);

        return new PaymentAuthResult(paypalOrder.Id, auth.Id, expiry, createdAt);
    }

    public async Task<CaptureResult> CapturePaymentAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string eShopOrderId,
        CancellationToken ct = default)
    {
        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{eShopOrderId}",
                payPalAuthAssertion: null,
                body: new CaptureRequest
                {
                    FinalCapture = true
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during capture.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        if (capture.Id == null)
            throw new PaymentException("PayPal capture did not return a capture ID.", 502);

        var breakdown = capture.SellerReceivableBreakdown;
        var capturedAmount = ParseMoney(capture.Amount?.Value);
        var fee = ParseMoney(breakdown?.PaypalFee?.Value);
        var net = ParseMoney(breakdown?.NetAmount?.Value);

        _logger.LogInformation("Payment captured: captureId={CaptureId} amount={Amount}", capture.Id, capturedAmount);

        return new CaptureResult(capture.Id, capturedAmount, fee, net);
    }

    public async Task<string> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                    }
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw TranslateReauthorizeError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during re-authorization.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        if (reauth.Id == null)
            throw new PaymentException("PayPal re-authorization did not return a new authorization ID.", 502);

        _logger.LogInformation("Payment re-authorized: newAuthId={AuthId}", reauth.Id);
        return reauth.Id;
    }

    public async Task VoidAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            throw TranslateVoidError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during void.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        _logger.LogInformation("Authorization voided: authId={AuthId}", authorizationId);
    }

    public async Task<string> RefundPaymentAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        RefundRequest? body = null;
        if (amount.HasValue)
        {
            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = amount.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                }
            };
        }

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
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw TranslateRefundError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during refund.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        if (refund.Id == null)
            throw new PaymentException("PayPal refund did not return a refund ID.", 502);

        _logger.LogInformation("Payment refunded: refundId={RefundId} amount={Amount}", refund.Id, amount);
        return refund.Id;
    }

    public async Task<SavedCardInfo> SaveCardAsync(
        string customerId,
        CardDetails card,
        CancellationToken ct = default)
    {
        PaymentTokenResponse tokenResponse;
        try
        {
            tokenResponse = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { Id = customerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.Cvv,
                            Name = card.Name
                        }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            throw TranslateVaultError(ex.Error, "save card");
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response during card save.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        if (tokenResponse.Id == null)
            throw new PaymentException("PayPal vault did not return a token ID.", 502);

        var cardInfo = tokenResponse.PaymentSource?.Card;
        var expiry = cardInfo?.Expiry;
        string? expiryMonth = null;
        string? expiryYear = null;
        if (expiry != null && expiry.Length >= 7)
        {
            // Format is YYYY-MM
            expiryYear = expiry[..4];
            expiryMonth = expiry[5..7];
        }

        _logger.LogInformation("Card vaulted: tokenId={TokenId}", tokenResponse.Id);

        return new SavedCardInfo(
            VaultToken: tokenResponse.Id,
            Last4: cardInfo?.LastDigits,
            CardBrand: cardInfo?.Brand?.Value,
            ExpiryMonth: expiryMonth,
            ExpiryYear: expiryYear);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListSavedCardsAsync(
        string customerId,
        CancellationToken ct = default)
    {
        var results = new List<SavedCardInfo>();
        int currentPage = 1;
        int totalPages;

        do
        {
            CustomerVaultPaymentTokensResponse response;
            try
            {
                response = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: customerId,
                    pageSize: 20,
                    page: currentPage,
                    totalRequired: true,
                    ct: ct);
            }
            catch (SdkException<ListCustomerPaymentTokensError> ex)
            {
                throw TranslateListVaultError(ex.Error);
            }
            catch (JsonException ex)
            {
                throw new PaymentException("PayPal returned an unprocessable response listing cards.", ex, 502);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                throw new PaymentException("PayPal service is unavailable.", ex, 502);
            }

            totalPages = response.TotalPages ?? 1;

            if (response.PaymentTokens != null)
            {
                foreach (var token in response.PaymentTokens)
                {
                    if (token.Id == null) continue;
                    var cardInfo = token.PaymentSource?.Card;
                    var expiry = cardInfo?.Expiry;
                    string? expiryMonth = null;
                    string? expiryYear = null;
                    if (expiry != null && expiry.Length >= 7)
                    {
                        expiryYear = expiry[..4];
                        expiryMonth = expiry[5..7];
                    }
                    results.Add(new SavedCardInfo(
                        VaultToken: token.Id,
                        Last4: cardInfo?.LastDigits,
                        CardBrand: cardInfo?.Brand?.Value,
                        ExpiryMonth: expiryMonth,
                        ExpiryYear: expiryYear));
                }
            }

            currentPage++;
        }
        while (currentPage <= totalPages);

        return results;
    }

    public async Task DeleteSavedCardAsync(string vaultToken, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: vaultToken,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            throw TranslateDeleteVaultError(ex.Error);
        }
        catch (JsonException ex)
        {
            throw new PaymentException("PayPal returned an unprocessable response deleting card.", ex, 502);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal service is unavailable.", ex, 502);
        }

        _logger.LogInformation("Vault token deleted: {VaultToken}", vaultToken);
    }

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(
        string startDate,
        string endDate,
        CancellationToken ct = default)
    {
        var results = new List<TransactionRecord>();
        int currentPage = 1;
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
                    page: currentPage,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                // Case B — no typed error model
                var body = ex.Error.ReadAsString();
                _logger.LogWarning("PayPal transaction search error HTTP {Status}: {Body}",
                    (int)ex.Error.StatusCode, body);
                throw new PaymentException(
                    $"PayPal transaction search failed (HTTP {(int)ex.Error.StatusCode}).", 502);
            }
            catch (JsonException ex)
            {
                throw new PaymentException("PayPal returned an unprocessable response during transaction search.", ex, 502);
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                throw new PaymentException("PayPal service is unavailable.", ex, 502);
            }

            totalPages = response.TotalPages ?? 1;

            if (response.TransactionDetails != null)
            {
                foreach (var detail in response.TransactionDetails)
                {
                    var info = detail.TransactionInfo;
                    results.Add(new TransactionRecord(
                        TransactionId: info?.TransactionId,
                        Amount: info?.TransactionAmount?.Value,
                        Fee: info?.FeeAmount?.Value,
                        Status: info?.TransactionStatus,
                        CreateTime: info?.TransactionInitiationDate,
                        PayPalReference: info?.PaypalReferenceId));
                }
            }

            currentPage++;
        }
        while (currentPage <= totalPages);

        return results;
    }

    // Error translators — Orders/Payments (Case A, uses TryGetError(out Error))

    private static PaymentException TranslateCreateOrderError(CreateOrderError error)
    {
        if (error.TryGetError(out var e))
            return new PaymentException($"PayPal rejected the order: {e.Message}", 422);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal order creation error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal order creation failed.", 422);
    }

    private static PaymentException TranslateAuthorizeOrderError(AuthorizeOrderError error, bool usingVaultToken)
    {
        if (error.TryGetError(out var e))
        {
            var msg = usingVaultToken && (e.Name?.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) == true
                                       || e.Message?.Contains("token", StringComparison.OrdinalIgnoreCase) == true)
                ? $"Vault token incompatible with PayPal authorization: {e.Message}"
                : $"PayPal rejected the authorization: {e.Message}";
            return new PaymentException(msg, 422);
        }
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal authorization error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal authorization failed.", 422);
    }

    private static PaymentException TranslateCaptureError(CaptureAuthorizedPaymentError error)
    {
        if (error.TryGetError(out var e))
        {
            // Defensively detect expired authorization (A3 — UNVERIFIED exact error name)
            var isExpired = e.Name?.Contains("AUTHORIZATION_ALREADY_CAPTURED", StringComparison.OrdinalIgnoreCase) == true
                         || e.Name?.Contains("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) == true
                         || e.Details?.Any(d => d.Issue?.Contains("expired", StringComparison.OrdinalIgnoreCase) == true) == true;
            if (isExpired)
                return new PaymentException("The payment authorization has expired and cannot be captured.", 422);
            return new PaymentException($"PayPal rejected the capture: {e.Message}", 422);
        }
        if (error.TryGetNoContent(out var noContent))
            return new PaymentException($"PayPal capture server error (HTTP {(int)noContent.StatusCode}).", 502);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal capture error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal capture failed.", 422);
    }

    private static PaymentException TranslateReauthorizeError(ReauthorizePaymentError error)
    {
        if (error.TryGetError(out var e))
            return new PaymentException($"PayPal rejected the re-authorization: {e.Message}", 422);
        if (error.TryGetNoContent(out var noContent))
            return new PaymentException($"PayPal re-authorization server error (HTTP {(int)noContent.StatusCode}).", 502);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal re-authorization error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal re-authorization failed.", 422);
    }

    private static PaymentException TranslateVoidError(VoidPaymentError error)
    {
        if (error.TryGetError(out var e))
            return new PaymentException($"PayPal rejected the void: {e.Message}", 422);
        if (error.TryGetNoContent(out var noContent))
            return new PaymentException($"PayPal void server error (HTTP {(int)noContent.StatusCode}).", 502);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal void error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal void failed.", 422);
    }

    private static PaymentException TranslateRefundError(RefundCapturedPaymentError error)
    {
        if (error.TryGetError(out var e))
        {
            // 409 Conflict = duplicate refund under different idempotency key
            return new PaymentException($"PayPal rejected the refund: {e.Message}", 422);
        }
        if (error.TryGetNoContent(out var noContent))
            return new PaymentException($"PayPal refund server error (HTTP {(int)noContent.StatusCode}).", 502);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal refund error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal refund failed.", 422);
    }

    // Error translators — Vault (Case A, uses TryGetError1(out Error1))

    private static PaymentException TranslateVaultError(CreatePaymentTokenError error, string op)
    {
        if (error.TryGetError1(out var e))
            return new PaymentException($"PayPal vault error ({op}): {e.Message}", 422);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal vault error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException($"PayPal vault {op} failed.", 422);
    }

    private static PaymentException TranslateListVaultError(ListCustomerPaymentTokensError error)
    {
        if (error.TryGetError1(out var e))
            return new PaymentException($"PayPal vault list error: {e.Message}", 422);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal vault list error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal vault list failed.", 422);
    }

    private static PaymentException TranslateDeleteVaultError(DeletePaymentTokenError error)
    {
        if (error.TryGetError1(out var e))
            return new PaymentException($"PayPal vault delete error: {e.Message}", 422);
        if (error.TryGetRawError(out var raw))
            return new PaymentException($"PayPal vault delete error (HTTP {(int)raw.StatusCode}).", 422);
        return new PaymentException("PayPal vault delete failed.", 422);
    }

    private static decimal ParseMoney(string? value)
    {
        if (value == null) return 0m;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var result) ? result : 0m;
    }
}
