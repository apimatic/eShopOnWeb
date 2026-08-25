using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PaypalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _currency = settings.Value.Currency;
    }

    public async Task<PaymentAuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string idempotencyKey, CardPaymentDetails card, CancellationToken ct = default)
    {
        try
        {
            var createResult = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = [new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    }],
                    PaymentSource = new PaymentSource
                    {
                        Card = new CardRequest
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name
                        }
                    }
                },
                prefer: "return=minimal",
                ct: ct);

            if (createResult.Status == PaypalOrderStatus.PayerActionRequired)
                throw new PaymentOperationException(
                    "This payment requires browser-based buyer approval (SCA/3DS triggered). Direct card payment is not possible for this transaction.", 422);

            var payPalOrderId = createResult.Id
                ?? throw new PaymentOperationException("PayPal did not return an order ID.", 502);

            var authResult = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            if (authResult.Status == PaypalOrderStatus.PayerActionRequired)
                throw new PaymentOperationException(
                    "This payment requires browser-based buyer approval (SCA/3DS triggered). Direct card payment is not possible for this transaction.", 422);

            var authorization = authResult.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization?.Id == null)
                throw new PaymentOperationException("PayPal did not return an authorization ID.", 502);

            return new PaymentAuthorizationResult(payPalOrderId, authorization.Id, authorization.ExpirationTime);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex, "CreateOrder(card)");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex, "AuthorizeOrder(card)");
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException("PayPal returned an unprocessable response.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task<PaymentAuthorizationResult> AuthorizeWithVaultTokenAsync(
        decimal amount, string currency, string idempotencyKey, string vaultTokenId, CancellationToken ct = default)
    {
        try
        {
            // Pass vault token in CreateOrder; use return=representation so we can check if the
            // order auto-authorizes (becomes COMPLETED) and extract the auth ID without calling
            // AuthorizeOrder separately.
            var createResult = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderRequest
                {
                    Intent = CheckoutPaymentIntent.Authorize,
                    PurchaseUnits = [new PurchaseUnitRequest
                    {
                        Amount = new AmountWithBreakdown
                        {
                            CurrencyCode = currency,
                            Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                        }
                    }],
                    PaymentSource = new PaymentSource
                    {
                        Token = new Token
                        {
                            Id = vaultTokenId,
                            Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN")
                        }
                    }
                },
                prefer: "return=representation",
                ct: ct);

            if (createResult.Status == PaypalOrderStatus.PayerActionRequired)
                throw new PaymentOperationException(
                    "This payment requires browser-based buyer approval and cannot be completed directly.", 422);

            var payPalOrderId = createResult.Id
                ?? throw new PaymentOperationException("PayPal did not return an order ID from CreateOrder.", 502);

            // If the order already has authorizations (auto-authorized during CreateOrder), use them directly.
            var createAuthorization = createResult.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (createAuthorization?.Id != null)
                return new PaymentAuthorizationResult(payPalOrderId, createAuthorization.Id, createAuthorization.ExpirationTime);

            // Otherwise call AuthorizeOrder to complete the authorization.
            var authResult = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            if (authResult.Status == PaypalOrderStatus.PayerActionRequired)
                throw new PaymentOperationException(
                    "This payment requires browser-based buyer approval and cannot be completed directly.", 422);

            var authorization = authResult.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (authorization?.Id == null)
                throw new PaymentOperationException("PayPal did not return an authorization ID.", 502);

            return new PaymentAuthorizationResult(payPalOrderId, authorization.Id, authorization.ExpirationTime);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex, "CreateOrder(vault-token)");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex, "AuthorizeOrder(vault-token)");
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException("PayPal returned an unprocessable response.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task<CapturePaymentResult> CapturePaymentAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            return await DoCaptureAsync(authorizationId, idempotencyKey, ct);
        }
        catch (PaymentOperationException ex) when (ex.IsAuthorizationExpired)
        {
            PaymentAuthorization reauth;
            try
            {
                reauth = await _client.Payments.ReauthorizePayment(
                    authorizationId: authorizationId,
                    payPalRequestId: $"{idempotencyKey}-reauth",
                    payPalAuthAssertion: null,
                    body: null,
                    prefer: "return=representation",
                    ct: ct);
            }
            catch (SdkException<ReauthorizePaymentError> rex)
            {
                var detail = "";
                if (rex.Error.TryGetError(out var re)) detail = $" Detail: {re.Message}";
                throw new PaymentOperationException(
                    $"Authorization is expired and could not be reauthorized; the order cannot be fulfilled. " +
                    $"Please void the order and ask the customer to re-authorize.{detail}", 422, rex);
            }

            var newAuthId = reauth.Id
                ?? throw new PaymentOperationException(
                    "Reauthorization did not return a new authorization ID.", 502);

            return await DoCaptureAsync(newAuthId, idempotencyKey, ct);
        }
    }

    private async Task<CapturePaymentResult> DoCaptureAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct)
    {
        try
        {
            var result = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            var captureId = result.Id
                ?? throw new PaymentOperationException("PayPal did not return a capture ID.", 502);

            var gross = ParseMoney(result.SellerReceivableBreakdown?.GrossAmount?.Value);
            var fee = ParseMoney(result.SellerReceivableBreakdown?.PaypalFee?.Value);
            var net = ParseMoney(result.SellerReceivableBreakdown?.NetAmount?.Value);
            var currency = result.Amount?.CurrencyCode ?? _currency;

            return new CapturePaymentResult(captureId, gross, currency, fee, net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                var issue = error.Details?.FirstOrDefault()?.Issue ?? "";
                var isExpired = issue.Contains("AUTHORIZATION_EXPIRED") || issue.Contains("AUTH_EXPIRED");
                throw new PaymentOperationException(error.Message ?? "Capture failed.", 422)
                {
                    IsAuthorizationExpired = isExpired
                };
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PaymentOperationException("PayPal returned an unexpected empty response during capture.", 500);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PaymentOperationException("Capture failed.", (int)raw.StatusCode);
            throw new PaymentOperationException("Capture failed.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException("PayPal returned an unprocessable response during capture.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task VoidPaymentAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: null,
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                var issue = error.Details?.FirstOrDefault()?.Issue ?? "";
                // Treat already-voided as idempotent success
                if (issue.Contains("VOIDED") || issue.Contains("COMPLETED") || issue.Contains("ALREADY")
                    || (int)(ex.Error.TryGetRawError(out _) ? 409 : 0) == 409)
                    return;
                throw new PaymentOperationException(error.Message ?? "Void failed.", 422);
            }
            if (ex.Error.TryGetNoContent(out _))
                // 204 as an error → treat as success (void completed but no body returned)
                return;
            if (ex.Error.TryGetRawError(out var raw))
            {
                // 409 Conflict = already voided → idempotent
                if (raw.StatusCode == System.Net.HttpStatusCode.Conflict)
                    return;
                throw new PaymentOperationException("Void failed.", (int)raw.StatusCode);
            }
            throw new PaymentOperationException("Void failed.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException)
        {
            // VoidPayment returns 204 No Content on success. The SDK throws JsonException
            // when deserializing the empty body into the expected response type. This is success.
            return;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task<RefundPaymentResult> RefundPaymentAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey, CancellationToken ct = default)
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
                        CurrencyCode = currency,
                        Value = amount.Value.ToString("F2", CultureInfo.InvariantCulture)
                    }
                };
            }

            var result = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            var refundId = result.Id
                ?? throw new PaymentOperationException("PayPal did not return a refund ID.", 502);

            var refundedAmount = ParseMoney(result.Amount?.Value);
            var totalRefunded = ParseMoney(result.SellerPayableBreakdown?.TotalRefundedAmount?.Value);
            var refundCurrency = result.Amount?.CurrencyCode ?? currency;

            return new RefundPaymentResult(refundId, refundedAmount, refundCurrency, totalRefunded);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
                throw new PaymentOperationException(error.Message ?? "Refund failed.", 422);
            if (ex.Error.TryGetNoContent(out _))
                throw new PaymentOperationException("PayPal returned an unexpected empty response during refund.", 500);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PaymentOperationException("Refund failed.", (int)raw.StatusCode);
            throw new PaymentOperationException("Refund failed.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException("PayPal returned an unprocessable response during refund.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        string startDate, string endDate, CancellationToken ct = default)
    {
        var allTransactions = new List<TransactionRecord>();
        int page = 1;
        int totalPages;

        try
        {
            do
            {
                var response = await _client.TransactionSearch.SearchTransactions(
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
                    ct: ct);

                totalPages = response.TotalPages ?? 1;

                if (response.TransactionDetails != null)
                {
                    foreach (var detail in response.TransactionDetails)
                    {
                        var info = detail.TransactionInfo;
                        if (info == null) continue;
                        allTransactions.Add(new TransactionRecord(
                            TransactionId: info.TransactionId,
                            Amount: ParseMoneyNullable(info.TransactionAmount?.Value),
                            Currency: info.TransactionAmount?.CurrencyCode,
                            Fee: ParseMoneyNullable(info.FeeAmount?.Value),
                            Status: info.TransactionStatus,
                            InitiationDate: info.TransactionInitiationDate));
                    }
                }

                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw new PaymentOperationException(
                $"Transaction search failed: {(int)ex.Error.StatusCode}",
                (int)ex.Error.StatusCode, ex);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException(
                "PayPal returned an unprocessable response during transaction search.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }

        return allTransactions;
    }

    public async Task<VaultTokenResult> CreateVaultTokenAsync(
        string customerId, CardVaultDetails card, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            // customerId is the merchant-side identifier; pass it only if PayPal has already assigned
            // a vault customer ID (i.e., it starts with "CUSTOMER-"). For new customers, omit and let
            // PayPal assign one, then return it to the caller for storage.
            Customer? customer = customerId.StartsWith("CUSTOMER-", StringComparison.Ordinal)
                ? new Customer { Id = customerId }
                : null;

            var result = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    Customer = customer,
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name
                        }
                    }
                },
                ct: ct);

            var tokenId = result.Id
                ?? throw new PaymentOperationException("PayPal vault did not return a token ID.", 502);

            var cardInfo = result.PaymentSource?.Card;
            return new VaultTokenResult(
                TokenId: tokenId,
                CustomerId: result.Customer?.Id,
                Last4: cardInfo?.LastDigits,
                Brand: cardInfo?.Brand?.Value,
                Expiry: cardInfo?.Expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
                throw new PaymentOperationException(error.Message ?? "Failed to save card.", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PaymentOperationException("Failed to save card.", (int)raw.StatusCode);
            throw new PaymentOperationException("Failed to save card.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException(
                "PayPal returned an unprocessable response during vault creation.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    public async Task<IReadOnlyList<VaultTokenInfo>> ListVaultTokensAsync(
        string customerId, CancellationToken ct = default)
    {
        var tokens = new List<VaultTokenInfo>();
        try
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId,
                pageSize: 100,
                page: 1,
                totalRequired: false,
                ct: ct);

            if (response.PaymentTokens != null)
            {
                foreach (var token in response.PaymentTokens)
                {
                    if (token.Id == null) continue;
                    var cardInfo = token.PaymentSource?.Card;
                    tokens.Add(new VaultTokenInfo(
                        TokenId: token.Id,
                        Last4: cardInfo?.LastDigits,
                        Brand: cardInfo?.Brand?.Value,
                        Expiry: cardInfo?.Expiry));
                }
            }
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
                throw new PaymentOperationException(error.Message ?? "Failed to list saved cards.", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PaymentOperationException("Failed to list saved cards.", (int)raw.StatusCode);
            throw new PaymentOperationException("Failed to list saved cards.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException(
                "PayPal returned an unprocessable response while listing tokens.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }

        return tokens;
    }

    public async Task DeleteVaultTokenAsync(string tokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: tokenId,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
                throw new PaymentOperationException(error.Message ?? "Failed to delete saved card.", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PaymentOperationException("Failed to delete saved card.", (int)raw.StatusCode);
            throw new PaymentOperationException("Failed to delete saved card.", 502);
        }
        catch (PaymentOperationException)
        {
            throw;
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new PaymentOperationException(
                "PayPal returned an unprocessable response during token deletion.", 502, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentOperationException("PayPal service is unavailable. Please try again.", 503, ex);
        }
    }

    private static decimal ParseMoney(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0m;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static decimal? ParseMoneyNullable(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static PaymentOperationException TranslateCreateOrderError(SdkException<CreateOrderError> ex, string stage = "CreateOrder")
    {
        if (ex.Error.TryGetError(out var error))
        {
            var issue = error.Details?.FirstOrDefault()?.Issue;
            var msg = issue != null
                ? $"[{stage}] {error.Message} [Issue: {issue}]"
                : $"[{stage}] {error.Message ?? "Failed to create PayPal order."}";
            return new PaymentOperationException(msg, 422);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new PaymentOperationException($"[{stage}] Failed to create PayPal order.", (int)raw.StatusCode);
        return new PaymentOperationException($"[{stage}] Failed to create PayPal order.", 502);
    }

    private static PaymentOperationException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex, string stage = "AuthorizeOrder")
    {
        if (ex.Error.TryGetError(out var error))
        {
            var issue = error.Details?.FirstOrDefault()?.Issue;
            var msg = issue != null
                ? $"[{stage}] {error.Message} [Issue: {issue}]"
                : $"[{stage}] {error.Message ?? "Failed to authorize PayPal payment."}";
            return new PaymentOperationException(msg, 422);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new PaymentOperationException($"[{stage}] Failed to authorize PayPal payment.", (int)raw.StatusCode);
        return new PaymentOperationException($"[{stage}] Failed to authorize PayPal payment.", 502);
    }
}
