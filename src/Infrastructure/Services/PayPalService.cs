using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.PayPal;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalService(PayPalServerSdkClient client, IOptions<PayPalSettings> options)
    {
        _client = client;
        _currency = options.Value.Currency;
    }

    public async Task<PayPalAuthorizeResult> AuthorizeWithCardAsync(decimal amount, PayPalCardRequest card, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Create order
            var createResp = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: null,
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
                                CurrencyCode = _currency,
                                Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                            }
                        }
                    }
                },
                prefer: "return=representation",
                ct: ct);

            var paypalOrderId = createResp.Id ?? throw new PayPalException("PayPal did not return an order ID");

            if (createResp.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException("PayPal requires payer action (3DS) — sandbox card not supported for direct processing");

            // Step 2: Authorize order with card
            var authResp = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = new CardRequest
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.CardholderName
                        }
                    }
                },
                prefer: "return=representation",
                ct: ct);

            if (authResp.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException("PayPal requires payer action (3DS challenge) — cannot proceed without browser redirect");

            var auth = authResp.PurchaseUnits?[0].Payments?.Authorizations?[0]
                ?? throw new PayPalException("PayPal authorization response missing authorization data");

            return new PayPalAuthorizeResult(
                AuthorizationId: auth.Id ?? throw new PayPalException("PayPal did not return an authorization ID"),
                Status: auth.Status?.Value ?? "UNKNOWN",
                PayPalOrderId: paypalOrderId);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw WrapOrderError(ex, "create order");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw WrapAuthorizeError(ex, "authorize order");
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalAuthorizeResult> AuthorizeWithVaultAsync(decimal amount, string vaultId, CancellationToken ct = default)
    {
        try
        {
            var createResp = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: null,
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
                                CurrencyCode = _currency,
                                Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                            }
                        }
                    }
                },
                prefer: "return=representation",
                ct: ct);

            var paypalOrderId = createResp.Id ?? throw new PayPalException("PayPal did not return an order ID");

            if (createResp.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException("PayPal requires payer action — vault card cannot be used without browser redirect");

            var authResp = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = new CardRequest { VaultId = vaultId }
                    }
                },
                prefer: "return=representation",
                ct: ct);

            if (authResp.Status == OrderStatus.PayerActionRequired)
                throw new PayPalException("PayPal requires payer action (3DS challenge) — cannot proceed without browser redirect");

            var auth = authResp.PurchaseUnits?[0].Payments?.Authorizations?[0]
                ?? throw new PayPalException("PayPal authorization response missing authorization data");

            return new PayPalAuthorizeResult(
                AuthorizationId: auth.Id ?? throw new PayPalException("PayPal did not return an authorization ID"),
                Status: auth.Status?.Value ?? "UNKNOWN",
                PayPalOrderId: paypalOrderId);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw WrapOrderError(ex, "create order");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw WrapAuthorizeError(ex, "authorize order with vault");
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalAuthorizationDetails> GetAuthorizationAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            return new PayPalAuthorizationDetails(
                AuthorizationId: auth.Id ?? authorizationId,
                Status: auth.Status?.Value ?? "UNKNOWN",
                CreateTime: auth.CreateTime,
                ExpirationTime: auth.ExpirationTime);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error typed))
                throw new PayPalException($"Get authorization failed: {typed.Message}", ex);
            if (ex.Error.TryGetNoContent(out RawError noContent))
                throw new PayPalException($"Get authorization failed: HTTP {(int)noContent.StatusCode}", ex, (int)noContent.StatusCode);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Get authorization failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Get authorization failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalAuthorizeResult> ReauthorizeAsync(string authorizationId, decimal amount, CancellationToken ct = default)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = _currency,
                        Value = amount.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                },
                ct: ct);

            return new PayPalAuthorizeResult(
                AuthorizationId: reauth.Id ?? throw new PayPalException("Reauthorization did not return new authorization ID"),
                Status: reauth.Status?.Value ?? "UNKNOWN",
                PayPalOrderId: null);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error typed))
                throw new PayPalException($"Reauthorization failed: {typed.Message}", ex, 422);
            if (ex.Error.TryGetNoContent(out RawError noContent))
                throw new PayPalException($"Reauthorization failed: HTTP {(int)noContent.StatusCode}", ex, (int)noContent.StatusCode);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Reauthorization failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Reauthorization failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalCaptureResult> CaptureAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: ct);

            var breakdown = capture.SellerReceivableBreakdown;
            decimal fee = 0m, net = 0m, captured = 0m;
            if (breakdown != null)
            {
                decimal.TryParse(breakdown.PaypalFee?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out fee);
                decimal.TryParse(breakdown.NetAmount?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out net);
                decimal.TryParse(breakdown.GrossAmount.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out captured);
            }
            else if (capture.Amount != null)
            {
                decimal.TryParse(capture.Amount.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out captured);
            }

            return new PayPalCaptureResult(
                CaptureId: capture.Id ?? throw new PayPalException("PayPal did not return a capture ID"),
                Status: capture.Status?.Value ?? "UNKNOWN",
                CapturedAmount: captured,
                PayPalFee: fee,
                NetAmount: net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error typed))
                throw new PayPalException($"Capture failed: {typed.Message}", ex);
            if (ex.Error.TryGetNoContent(out RawError noContent))
                throw new PayPalException($"Capture failed: HTTP {(int)noContent.StatusCode}", ex, (int)noContent.StatusCode);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Capture failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Capture failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
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
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error typed))
                throw new PayPalException($"Void failed: {typed.Message}", ex);
            if (ex.Error.TryGetNoContent(out RawError noContent))
                throw new PayPalException($"Void failed: HTTP {(int)noContent.StatusCode}", ex, (int)noContent.StatusCode);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Void failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Void failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalRefundResult> RefundAsync(string captureId, decimal? amount, string? idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            RefundRequest? body = null;
            if (amount.HasValue)
            {
                body = new RefundRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = _currency,
                        Value = amount.Value.ToString("0.00", CultureInfo.InvariantCulture)
                    }
                };
            }

            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                ct: ct);

            decimal refundedAmount = 0m;
            decimal.TryParse(refund.Amount?.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out refundedAmount);

            return new PayPalRefundResult(
                RefundId: refund.Id ?? throw new PayPalException("PayPal did not return a refund ID"),
                Status: refund.Status?.Value ?? "UNKNOWN",
                Amount: refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            // 409 with same idempotency key means the refund already exists — not a failure
            if (ex.Error.TryGetError(out Error typed))
                throw new PayPalException($"Refund failed: {typed.Message}", ex);
            if (ex.Error.TryGetNoContent(out RawError noContent))
                throw new PayPalException($"Refund failed: HTTP {(int)noContent.StatusCode}", ex, (int)noContent.StatusCode);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Refund failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Refund failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<PayPalVaultResult> VaultCardAsync(PayPalCardRequest card, string merchantCustomerId, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Create setup token
            var setupResp = await _client.Vault.CreateSetupToken(
                payPalRequestId: null,
                body: new SetupTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.CardholderName
                        }
                    }
                },
                ct: ct);

            if (setupResp.Status == PaymentTokenStatus.PayerActionRequired)
                throw new PayPalException("PayPal requires payer action (3DS) to vault this card — cannot proceed without browser redirect");

            var setupTokenId = setupResp.Id ?? throw new PayPalException("PayPal did not return a setup token ID");

            // Step 2: Create payment token from setup token
            var tokenResp = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { MerchantCustomerId = merchantCustomerId },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setupTokenId,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                ct: ct);

            var cardDescriptor = tokenResp.PaymentSource?.Card;

            return new PayPalVaultResult(
                PaymentTokenId: tokenResp.Id ?? throw new PayPalException("PayPal did not return a payment token ID"),
                Last4: cardDescriptor?.LastDigits,
                Brand: cardDescriptor?.Brand?.Value,
                Expiry: cardDescriptor?.Expiry,
                CardholderName: cardDescriptor?.Name);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1 typed))
                throw new PayPalException($"Vault setup failed: {typed.Message}", ex);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Vault setup failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Vault setup failed", ex);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1 typed))
                throw new PayPalException($"Vault token creation failed: {typed.Message}", ex);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Vault token creation failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Vault token creation failed", ex);
        }
        catch (PayPalException) { throw; }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: paymentTokenId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1 typed))
                throw new PayPalException($"Delete vault token failed: {typed.Message}", ex);
            if (ex.Error.TryGetRawError(out RawError raw))
                throw new PayPalException($"Delete vault token failed: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
            throw new PayPalException("Delete vault token failed", ex);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionRecord>> GetTransactionsAsync(string from, string to, CancellationToken ct = default)
    {
        var allTransactions = new List<PayPalTransactionRecord>();
        int page = 1;
        int totalPages;

        try
        {
            do
            {
                var resp = await _client.TransactionSearch.SearchTransactions(
                    startDate: from,
                    endDate: to,
                    transactionId: null,
                    transactionType: null,
                    transactionStatus: null,
                    transactionAmount: null,
                    transactionCurrency: null,
                    paymentInstrumentType: null,
                    storeId: null,
                    terminalId: null,
                    page: page,
                    ct: ct);

                totalPages = resp.TotalPages ?? 1;

                if (resp.TransactionDetails != null)
                {
                    foreach (var detail in resp.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null) continue;

                        decimal? amt = null;
                        if (info.TransactionAmount?.Value != null)
                            decimal.TryParse(info.TransactionAmount.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedAmt);

                        if (info.TransactionAmount?.Value != null)
                        {
                            decimal.TryParse(info.TransactionAmount.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var a);
                            amt = a;
                        }

                        allTransactions.Add(new PayPalTransactionRecord(
                            TransactionId: info.TransactionId,
                            PayPalReferenceId: info.PaypalReferenceId,
                            Status: info.TransactionStatus,
                            Amount: amt,
                            InitiationDate: info.TransactionInitiationDate,
                            InvoiceId: info.InvoiceId));
                    }
                }

                page++;
            } while (page <= totalPages);

            return allTransactions;
        }
        catch (SdkException<RawError> ex)
        {
            RawError raw = ex.Error;
            throw new PayPalException($"Transaction search failed: HTTP {(int)raw.StatusCode} — {raw.ReadAsString()}", ex, (int)raw.StatusCode);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response", ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unavailable", ex);
        }
    }

    private static PayPalException WrapOrderError(SdkException<CreateOrderError> ex, string operation)
    {
        if (ex.Error.TryGetError(out Error typed))
            return new PayPalException($"Failed to {operation}: {typed.Message}", ex);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"Failed to {operation}: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
        return new PayPalException($"Failed to {operation}", ex);
    }

    private static PayPalException WrapAuthorizeError(SdkException<AuthorizeOrderError> ex, string operation)
    {
        if (ex.Error.TryGetError(out Error typed))
            return new PayPalException($"Failed to {operation}: {typed.Message}", ex);
        if (ex.Error.TryGetRawError(out RawError raw))
            return new PayPalException($"Failed to {operation}: HTTP {(int)raw.StatusCode}", ex, (int)raw.StatusCode);
        return new PayPalException($"Failed to {operation}", ex);
    }
}
