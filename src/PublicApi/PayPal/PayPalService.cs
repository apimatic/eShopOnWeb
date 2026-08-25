using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
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

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalService : IPayPalService
{
    // Per-process prefix scopes idempotency keys and invoice IDs to this run, preventing
    // PayPal sandbox cache hits from previous test sessions that reuse the same DB-assigned IDs.
    private static readonly string _runPrefix = Guid.NewGuid().ToString("N")[..8];

    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;
    private readonly IAppLogger<PayPalService> _logger;

    public string Currency => _currency;

    public PayPalService(PayPalServerSdkClient client, PayPalSettings settings, IAppLogger<PayPalService> logger)
    {
        _client = client;
        _currency = settings.Currency;
        _logger = logger;
    }

    public async Task<AuthorizationResult> AuthorizeWithCardAsync(
        decimal amount, string currency, string idempotencyKey,
        CardPaymentDetails card, string? invoiceRef = null,
        CancellationToken ct = default)
    {
        var paypalOrderId = await CreateOrderAsync(amount, currency, idempotencyKey, invoiceRef, ct);
        return await AuthorizeOrderAsync(paypalOrderId, idempotencyKey, BuildCardPaymentSource(card), ct);
    }

    public async Task<AuthorizationResult> AuthorizeWithVaultAsync(
        decimal amount, string currency, string idempotencyKey,
        string vaultToken, string? invoiceRef = null,
        CancellationToken ct = default)
    {
        var paypalOrderId = await CreateOrderAsync(amount, currency, idempotencyKey, invoiceRef, ct);
        var paymentSource = new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest { VaultId = vaultToken }
        };
        return await AuthorizeOrderAsync(paypalOrderId, idempotencyKey, paymentSource, ct);
    }

    private async Task<string> CreateOrderAsync(
        decimal amount, string currency, string idempotencyKey,
        string? invoiceRef, CancellationToken ct)
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
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    },
                    InvoiceId = invoiceRef is not null
                        ? $"eshop-{_runPrefix}-{invoiceRef}"
                        : null
                }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: $"create-{_runPrefix}-{idempotencyKey}",
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return order.Id ?? throw new PayPalException("PayPal order created but returned no ID.", 502);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when creating the order.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    private async Task<AuthorizationResult> AuthorizeOrderAsync(
        string paypalOrderId, string idempotencyKey,
        OrderAuthorizeRequestPaymentSource? paymentSource, CancellationToken ct)
    {
        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: $"auth-{_runPrefix}-{idempotencyKey}",
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest { PaymentSource = paymentSource },
                prefer: "return=representation",
                ct: ct);

            var auth = response.PurchaseUnits?[0]?.Payments?.Authorizations?[0]
                ?? throw new PayPalException("PayPal authorization response missing authorization details.", 502);

            return new AuthorizationResult(
                PayPalOrderId: response.Id ?? paypalOrderId,
                AuthorizationId: auth.Id ?? throw new PayPalException("Authorization ID missing in PayPal response.", 502),
                AuthorizationStatus: auth.Status?.Value ?? "UNKNOWN",
                ExpirationTime: auth.ExpirationTime);
        }
        catch (PayPalException) { throw; }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when authorizing the order.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<CaptureResult> CaptureAuthorizationAsync(
        string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{_runPrefix}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: ct);

            var capturedAmount = ParseMoney(result.Amount);
            var fee = ParseMoney(result.SellerReceivableBreakdown?.PaypalFee);
            var net = ParseMoney(result.SellerReceivableBreakdown?.NetAmount);

            return new CaptureResult(
                CaptureId: result.Id ?? throw new PayPalException("Capture ID missing in PayPal response.", 502),
                CapturedAmount: capturedAmount,
                PayPalFee: fee,
                NetAmount: net,
                CaptureStatus: result.Status?.Value ?? "UNKNOWN");
        }
        catch (PayPalException) { throw; }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when capturing the authorization.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<bool> IsAuthorizationExpiredAsync(string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);

            if (auth.ExpirationTime is null) return false;
            if (!DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry)) return false;
            return DateTimeOffset.UtcNow >= expiry;
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
                throw new PayPalException($"Failed to check authorization: {error?.Message}", 502);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Failed to check authorization. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Failed to check authorization status.", 502);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when checking the authorization.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<ReauthorizeResult> ReauthorizeAsync(
        string authorizationId, decimal amount, string currency, string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var result = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: $"reauth-{_runPrefix}-{idempotencyKey}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2", CultureInfo.InvariantCulture)
                    }
                },
                ct: ct);

            return new ReauthorizeResult(
                NewAuthorizationId: result.Id ?? throw new PayPalException("Re-authorization ID missing in PayPal response.", 502),
                NewStatus: result.Status?.Value ?? "UNKNOWN",
                NewExpirationTime: result.ExpirationTime);
        }
        catch (PayPalException) { throw; }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
                throw new PayPalException($"Re-authorization failed: {error?.Message}", 422);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("Re-authorization is no longer possible. The authorization may be older than 29 days and must be recreated.", 422, "REAUTH_WINDOW_EXPIRED");
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Re-authorization failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Re-authorization failed.", 422);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when re-authorizing.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task VoidAuthorizationAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: $"void-{_runPrefix}-{idempotencyKey}",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
                throw new PayPalException($"Void failed: {error?.Message}", 422);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("Authorization already voided or captured.", 409);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Void failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Authorization void failed.", 422);
        }
        catch (JsonException)
        {
            // VoidPayment returns 204 No Content (empty body); the SDK throws JsonException
            // when deserializing an empty response — treat this as a successful void.
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<RefundResult> RefundCaptureAsync(
        string captureId, decimal? amount, string currency, string idempotencyKey,
        CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = amount.Value.ToString("F2", CultureInfo.InvariantCulture)
                }
            }
            : null;

        try
        {
            var result = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                ct: ct);

            return new RefundResult(
                RefundId: result.Id ?? throw new PayPalException("Refund ID missing in PayPal response.", 502),
                RefundedAmount: ParseMoney(result.Amount),
                RefundStatus: result.Status?.Value ?? "UNKNOWN");
        }
        catch (PayPalException) { throw; }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var error))
            {
                var detail = error?.Details?.FirstOrDefault()?.Issue ?? error?.Message ?? "Unknown error";
                var statusCode = detail.Contains("DUPLICATE") ? 409 : 422;
                throw new PayPalException($"Refund failed: {detail}", statusCode);
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("Refund request was rejected (no content).", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Refund failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Refund failed.", 422);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response for the refund.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<VaultResult> VaultCardAsync(
        string customerId, string idempotencyKey, CardPaymentDetails card,
        CancellationToken ct = default)
    {
        var expiry = $"{card.ExpiryYear}-{card.ExpiryMonth.PadLeft(2, '0')}";

        var body = new PaymentTokenRequest
        {
            Customer = new Customer { Id = customerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = expiry,
                    SecurityCode = card.Cvv,
                    BillingAddress = new Address
                    {
                        AddressLine1 = card.Street,
                        AdminArea2 = card.City,
                        AdminArea1 = card.State,
                        PostalCode = card.PostalCode,
                        CountryCode = card.CountryCode
                    }
                }
            }
        };

        try
        {
            var result = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: body,
                ct: ct);

            var cardInfo = result.PaymentSource?.Card;
            var brandValue = cardInfo?.Brand?.Value;

            return new VaultResult(
                VaultToken: result.Id ?? throw new PayPalException("Vault token ID missing in PayPal response.", 502),
                Last4Digits: cardInfo?.LastDigits,
                CardBrand: brandValue,
                Expiry: cardInfo?.Expiry);
        }
        catch (PayPalException) { throw; }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
            {
                var detail = error?.Details?.FirstOrDefault()?.Issue ?? error?.Message ?? "Unknown error";
                throw new PayPalException($"Card vault failed: {detail}", 422);
            }
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Card vault failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Card vault failed.", 422);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when vaulting the card.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<IReadOnlyList<VaultedPaymentMethodInfo>> ListVaultedCardsAsync(
        string customerId, CancellationToken ct = default)
    {
        var allTokens = new List<VaultedPaymentMethodInfo>();

        try
        {
            // First call to get total pages
            var first = await _client.Vault.ListCustomerPaymentTokens(
                customerId: customerId,
                pageSize: 50,
                page: 1,
                totalRequired: true,
                ct: ct);

            CollectTokens(first.PaymentTokens, allTokens);

            var totalPages = first.TotalPages ?? 1;
            for (int page = 2; page <= totalPages; page++)
            {
                var next = await _client.Vault.ListCustomerPaymentTokens(
                    customerId: customerId,
                    pageSize: 50,
                    page: page,
                    totalRequired: false,
                    ct: ct);
                CollectTokens(next.PaymentTokens, allTokens);
            }

            return allTokens;
        }
        catch (PayPalException) { throw; }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
                throw new PayPalException($"Failed to list vaulted cards: {error?.Message}", 502);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Failed to list vaulted cards. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Failed to list vaulted cards.", 502);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when listing vaulted cards.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    private static void CollectTokens(
        IReadOnlyList<PaymentTokenResponse>? tokens,
        List<VaultedPaymentMethodInfo> target)
    {
        if (tokens is null) return;
        foreach (var t in tokens)
        {
            var card = t.PaymentSource?.Card;
            target.Add(new VaultedPaymentMethodInfo(
                VaultToken: t.Id ?? "",
                Last4Digits: card?.LastDigits,
                CardBrand: card?.Brand?.Value,
                Expiry: card?.Expiry));
        }
    }

    public async Task DeleteVaultedCardAsync(string vaultToken, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultToken, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var error))
                throw new PayPalException($"Failed to delete vaulted card: {error?.Message}", 422);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Failed to delete vaulted card. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Failed to delete vaulted card.", 422);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response when deleting the vaulted card.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransactionInfo>> SearchTransactionsAsync(
        DateTimeOffset startDate, DateTimeOffset endDate, CancellationToken ct = default)
    {
        var allTransactions = new List<PayPalTransactionInfo>();
        var startStr = startDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var endStr = endDate.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

        try
        {
            // First page to get total pages
            var first = await _client.TransactionSearch.SearchTransactions(
                startDate: startStr,
                endDate: endStr,
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
                page: 1,
                ct: ct);

            CollectTransactions(first.TransactionDetails, allTransactions);

            var totalPages = first.TotalPages ?? 1;
            for (int page = 2; page <= totalPages; page++)
            {
                var next = await _client.TransactionSearch.SearchTransactions(
                    startDate: startStr,
                    endDate: endStr,
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
                CollectTransactions(next.TransactionDetails, allTransactions);
            }

            return allTransactions;
        }
        catch (PayPalException) { throw; }
        catch (SdkException<RawError> ex)
        {
            // TransactionSearch is Case B — SdkException<RawError>
            throw new PayPalException(
                $"Transaction search failed. HTTP {(int)ex.Error.StatusCode}: {ex.Error.ReadAsString()}",
                (int)ex.Error.StatusCode);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unreadable response from transaction search.", 502, inner: ex);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", 503, inner: ex);
        }
    }

    private static void CollectTransactions(
        IReadOnlyList<TransactionDetails>? details,
        List<PayPalTransactionInfo> target)
    {
        if (details is null) return;
        foreach (var td in details)
        {
            var info = td.TransactionInfo;
            if (info is null) continue;
            target.Add(new PayPalTransactionInfo(
                TransactionId: info.TransactionId ?? "",
                Amount: ParseMoneyString(info.TransactionAmount?.Value),
                Fee: ParseMoneyString(info.FeeAmount?.Value),
                Status: info.TransactionStatus,
                InvoiceId: info.InvoiceId,
                CustomField: info.CustomField));
        }
    }

    private static OrderAuthorizeRequestPaymentSource BuildCardPaymentSource(CardPaymentDetails card)
    {
        var expiry = $"{card.ExpiryYear}-{card.ExpiryMonth.PadLeft(2, '0')}";
        return new OrderAuthorizeRequestPaymentSource
        {
            Card = new CardRequest
            {
                Name = card.CardholderName,
                Number = card.Number,
                Expiry = expiry,
                SecurityCode = card.Cvv,
                BillingAddress = new Address
                {
                    AddressLine1 = card.Street,
                    AdminArea2 = card.City,
                    AdminArea1 = card.State,
                    PostalCode = card.PostalCode,
                    CountryCode = card.CountryCode
                }
            }
        };
    }

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value is null) return 0m;
        return ParseMoneyString(money.Value) ?? 0m;
    }

    private static decimal? ParseMoneyString(string? value)
    {
        if (value is null) return null;
        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static PayPalException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var detail = error?.Details?.FirstOrDefault()?.Issue ?? error?.Message ?? "Unknown error";
            return new PayPalException($"Order creation failed: {detail}", 422);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Order creation failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
        return new PayPalException("Order creation failed.", 422);
    }

    private static PayPalException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var detail = error?.Details?.FirstOrDefault()?.Issue ?? error?.Message ?? "Unknown error";
            var statusCode = detail.Contains("INSTRUMENT_DECLINED") ? 402 : 422;
            return new PayPalException($"Authorization failed: {detail}", statusCode);
        }
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Authorization failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
        return new PayPalException("Authorization failed.", 422);
    }

    private static PayPalException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var error))
        {
            var issue = error?.Details?.FirstOrDefault()?.Issue ?? error?.Message ?? "Unknown";
            if (issue.Contains("AUTHORIZATION_EXPIRED"))
                return new PayPalException("The authorization has expired. Fulfil after re-authorizing.", 422, "AUTHORIZATION_EXPIRED");
            if (issue.Contains("AUTHORIZATION_ALREADY_VOIDED"))
                return new PayPalException("The authorization was already voided.", 422, "AUTHORIZATION_ALREADY_VOIDED");
            if (issue.Contains("AUTHORIZATION_ALREADY_CAPTURED"))
                return new PayPalException("The payment was already captured.", 409, "ALREADY_CAPTURED");
            return new PayPalException($"Capture failed: {issue}", 422);
        }
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalException("Capture failed (no content response).", 500);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalException($"Capture failed. HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
        return new PayPalException("Capture failed.", 422);
    }
}
