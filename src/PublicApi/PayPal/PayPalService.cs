using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;

namespace Microsoft.eShopWeb.PublicApi.PayPal;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings)
    {
        _client = client;
        _currency = settings.Value.Currency;
    }

    public string Currency => _currency;

    // ─── Order Authorization ─────────────────────────────────────────────────

    public async Task<AuthorizeResult> AuthorizeOrderAsync(
        decimal amount, CardPaymentDetails card, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(amount, ct);

        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Card = BuildCardRequest(card)
            }
        };

        return await AuthorizePayPalOrderAsync(payPalOrderId, authorizeRequest, ct);
    }

    public async Task<AuthorizeResult> AuthorizeOrderWithTokenAsync(
        decimal amount, string vaultTokenId, CancellationToken ct = default)
    {
        var payPalOrderId = await CreatePayPalOrderAsync(amount, ct);

        var authorizeRequest = new OrderAuthorizeRequest
        {
            PaymentSource = new OrderAuthorizeRequestPaymentSource
            {
                Token = new Token
                {
                    Id = vaultTokenId,
                    Type = TokenType.FromValue("PAYMENT_METHOD_TOKEN")
                }
            }
        };

        return await AuthorizePayPalOrderAsync(payPalOrderId, authorizeRequest, ct);
    }

    // ─── Capture ─────────────────────────────────────────────────────────────

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId, CancellationToken ct = default)
    {
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return ExtractCaptureResult(captured, null);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            // Check if this is a stale/expired authorization
            if (IsStaleAuthError(ex))
            {
                return await HandleStaleAuthAndCaptureAsync(authorizationId, ct);
            }

            throw WrapCaptureError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    // ─── Void ────────────────────────────────────────────────────────────────

    public async Task VoidAuthorizationAsync(
        string authorizationId, CancellationToken ct = default)
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
            string message = "Failed to void authorization.";
            if (ex.Error.TryGetError(out Error typed))
                message = typed.Message;
            else if (ex.Error.TryGetNoContent(out RawError noContent))
                message = $"Provider error (HTTP {(int)noContent.StatusCode}).";
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException)
        {
            // VoidPayment returns Task<PaymentAuthorization> but PayPal returns 204 No Content on
            // success — the SDK throws JsonException trying to parse the empty body. 204 = success.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    // ─── Refund ──────────────────────────────────────────────────────────────

    public async Task<RefundResult> RefundAsync(
        string captureId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        RefundRequest? body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = _currency, Value = FormatAmount(amount.Value) } }
            : null;

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return new RefundResult(
                RefundId: refund.Id ?? throw new PayPalException("PayPal refund returned no ID."),
                Amount: ParseAmount(refund.Amount?.Value));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            string message = "Failed to refund payment.";
            if (ex.Error.TryGetError(out Error typed))
                message = typed.Message;
            else if (ex.Error.TryGetNoContent(out RawError noContent))
                message = $"Provider error (HTTP {(int)noContent.StatusCode}).";
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    // ─── Card Vaulting ───────────────────────────────────────────────────────

    public async Task<VaultResult> VaultCardAsync(
        string customerId, string? existingPayPalCustomerId, CardPaymentDetails card, CancellationToken ct = default)
    {
        var request = new PaymentTokenRequest
        {
            Customer = new Customer { Id = existingPayPalCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = card.CardholderName,
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    BillingAddress = new Address
                    {
                        CountryCode = card.CountryCode,
                        AddressLine1 = card.AddressLine1,
                        AdminArea2 = card.City,
                        AdminArea1 = card.State,
                        PostalCode = card.PostalCode
                    }
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: request,
                requestOptions: null,
                ct: ct);

            var lastFour = response.PaymentSource?.Card?.LastDigits;
            var brand = response.PaymentSource?.Card?.Brand?.Value;
            var expiry = response.PaymentSource?.Card?.Expiry;

            return new VaultResult(
                TokenId: response.Id ?? throw new PayPalException("PayPal vault returned no token ID."),
                CustomerId: response.Customer?.Id,
                LastFour: lastFour,
                Brand: brand,
                Expiry: expiry);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            string message = "Failed to save card.";
            if (ex.Error.TryGetError1(out Error1 typed))
                message = typed.Message;
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    public async Task<IReadOnlyList<VaultedCardInfo>> ListVaultedCardsAsync(
        string payPalCustomerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: payPalCustomerId,
                pageSize: 50,
                page: 1,
                totalRequired: true,
                requestOptions: null,
                ct: ct);

            var results = new List<VaultedCardInfo>();
            if (response.PaymentTokens == null) return results;

            foreach (var token in response.PaymentTokens)
            {
                results.Add(new VaultedCardInfo(
                    TokenId: token.Id ?? string.Empty,
                    LastFour: token.PaymentSource?.Card?.LastDigits,
                    Brand: token.PaymentSource?.Card?.Brand?.Value,
                    Expiry: token.PaymentSource?.Card?.Expiry));
            }

            return results;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            string message = "Failed to list saved cards.";
            if (ex.Error.TryGetError1(out Error1 typed))
                message = typed.Message;
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    public async Task DeleteVaultedCardAsync(string tokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: tokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            string message = "Failed to delete saved card.";
            if (ex.Error.TryGetError1(out Error1 typed))
                message = typed.Message;
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    // ─── Transaction Reconciliation ──────────────────────────────────────────

    public async Task<IReadOnlyList<TransactionRecord>> SearchTransactionsAsync(
        string startDate, string endDate, CancellationToken ct = default)
    {
        var all = new List<TransactionRecord>();

        try
        {
            var firstPage = await _client.TransactionSearch.SearchTransactions(
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
                fields: "all",
                balanceAffectingRecordsOnly: "N",
                pageSize: 500,
                page: 1,
                requestOptions: null,
                ct: ct);

            AppendTransactions(all, firstPage);

            var totalPages = firstPage.TotalPages ?? 1;
            for (var p = 2; p <= totalPages; p++)
            {
                var nextPage = await _client.TransactionSearch.SearchTransactions(
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
                    fields: "all",
                    balanceAffectingRecordsOnly: "N",
                    pageSize: 500,
                    page: p,
                    requestOptions: null,
                    ct: ct);

                AppendTransactions(all, nextPage);
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalException(
                $"Transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}",
                ex.Error.StatusCode,
                ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }

        return all;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task<string> CreatePayPalOrderAsync(decimal amount, CancellationToken ct)
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
                        CurrencyCode = _currency,
                        Value = FormatAmount(amount)
                    }
                }
            }
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: orderRequest,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return order.Id ?? throw new PayPalException("PayPal CreateOrder returned no order ID.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            string message = "Failed to create PayPal order.";
            if (ex.Error.TryGetError(out Error typed))
                message = typed.Message;
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    private async Task<AuthorizeResult> AuthorizePayPalOrderAsync(
        string payPalOrderId, OrderAuthorizeRequest authorizeRequest, CancellationToken ct)
    {
        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: payPalOrderId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authorizeRequest,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var authId = response.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
                ?? throw new PayPalException("PayPal AuthorizeOrder returned no authorization ID.");

            return new AuthorizeResult(PayPalOrderId: payPalOrderId, AuthorizationId: authId);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            string message = "Failed to authorize PayPal order.";
            if (ex.Error.TryGetError(out Error typed))
                message = typed.Message;
            else if (ex.Error.TryGetRawError(out RawError raw))
                message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(message, null, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("The payment provider returned a response that could not be processed.", null, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("Payment provider is unreachable.", null, ex);
        }
    }

    private async Task<CaptureResult> HandleStaleAuthAndCaptureAsync(
        string authorizationId, CancellationToken ct)
    {
        // Inspect the authorization state
        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            string msg = "Failed to retrieve authorization status.";
            if (ex.Error.TryGetError(out Error typed)) msg = typed.Message;
            else if (ex.Error.TryGetNoContent(out RawError noContent))
                msg = $"Provider error (HTTP {(int)noContent.StatusCode}).";
            else if (ex.Error.TryGetRawError(out RawError raw))
                msg = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";
            throw new PayPalException(msg, null, ex);
        }

        bool isExpired = IsAuthExpired(auth);
        bool isVoidedOrDenied = auth.Status == AuthorizationStatus.Voided
                             || auth.Status == AuthorizationStatus.Denied;

        if (!isExpired && !isVoidedOrDenied)
        {
            // Not a stale auth — some other capture error
            throw new PayPalException("Capture failed and the authorization is not stale. Check provider logs.");
        }

        if (isVoidedOrDenied)
        {
            throw new PayPalException(
                $"The authorization has been {auth.Status?.Value?.ToLowerInvariant()} and cannot be renewed. " +
                "Please contact the customer to collect a new payment.");
        }

        // Attempt reauthorization (works from day 4 to day 29 after original auth)
        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest(),
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            string msg = "Authorization has expired and cannot be renewed.";
            if (ex.Error.TryGetError(out Error typed)) msg = typed.Message;
            else if (ex.Error.TryGetNoContent(out RawError noContent))
                msg = $"Reauthorization failed (HTTP {(int)noContent.StatusCode}). The authorization may have expired past the 29-day reauthorization window.";
            else if (ex.Error.TryGetRawError(out RawError raw))
                msg = $"Reauthorization failed (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";

            throw new PayPalException(msg, null, ex);
        }

        var newAuthId = reauth.Id ?? throw new PayPalException("Reauthorization returned no authorization ID.");

        // Now capture the new authorization
        CapturedPayment captured;
        try
        {
            captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: newAuthId,
                payPalMockResponse: null,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw WrapCaptureError(ex);
        }

        return ExtractCaptureResult(captured, newAuthId);
    }

    private static CaptureResult ExtractCaptureResult(CapturedPayment captured, string? newAuthId)
    {
        var captureId = captured.Id ?? throw new PayPalException("PayPal capture returned no capture ID.");
        var capturedAmount = ParseAmount(captured.Amount?.Value);
        var fee = ParseAmountNullable(captured.SellerReceivableBreakdown?.PaypalFee?.Value);
        var net = ParseAmountNullable(captured.SellerReceivableBreakdown?.NetAmount?.Value);

        return new CaptureResult(
            CaptureId: captureId,
            CapturedAmount: capturedAmount,
            Fee: fee,
            Net: net,
            NewAuthorizationId: newAuthId);
    }

    private static bool IsStaleAuthError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out Error typed))
        {
            // PayPal returns AUTHORIZATION_ALREADY_CAPTURED, INVALID_RESOURCE_ID or similar for stale auth
            return typed.Name?.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) == true
                || typed.Name?.Contains("INVALID_RESOURCE", StringComparison.OrdinalIgnoreCase) == true
                || typed.Name?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) == true;
        }
        if (ex.Error.TryGetRawError(out RawError raw))
        {
            return (int)raw.StatusCode == 422;
        }
        return false;
    }

    private static bool IsAuthExpired(PaymentAuthorization auth)
    {
        if (auth.Status == AuthorizationStatus.Voided || auth.Status == AuthorizationStatus.Denied)
            return false; // handled separately

        if (auth.ExpirationTime is null) return false;

        if (DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry))
            return DateTimeOffset.UtcNow > expiry;

        return false;
    }

    private static PayPalException WrapCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        string message = "Failed to capture payment.";
        if (ex.Error.TryGetError(out Error typed))
            message = typed.Message;
        else if (ex.Error.TryGetNoContent(out RawError noContent))
            message = $"Provider error (HTTP {(int)noContent.StatusCode}).";
        else if (ex.Error.TryGetRawError(out RawError raw))
            message = $"Provider error (HTTP {(int)raw.StatusCode}): {raw.ReadAsString()}";
        return new PayPalException(message, null, ex);
    }

    private static CardRequest BuildCardRequest(CardPaymentDetails card) =>
        new CardRequest
        {
            Name = card.CardholderName,
            Number = card.Number,
            Expiry = card.Expiry,
            SecurityCode = card.SecurityCode,
            BillingAddress = new Address
            {
                CountryCode = card.CountryCode,
                AddressLine1 = card.AddressLine1,
                AdminArea2 = card.City,
                AdminArea1 = card.State,
                PostalCode = card.PostalCode
            }
        };

    private static void AppendTransactions(List<TransactionRecord> all, SearchResponse page)
    {
        if (page.TransactionDetails == null) return;
        foreach (var tx in page.TransactionDetails)
        {
            var info = tx.TransactionInfo;
            all.Add(new TransactionRecord(
                TransactionId: info?.TransactionId,
                Amount: info?.TransactionAmount?.Value,
                Currency: info?.TransactionAmount?.CurrencyCode,
                Status: info?.TransactionStatus,
                PayPalReferenceId: info?.PaypalReferenceId,
                ReferenceType: info?.PaypalReferenceIdType?.Value,
                Timestamp: null));
        }
    }

    private static string FormatAmount(decimal amount) => amount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

    private static decimal ParseAmount(string? value)
    {
        if (string.IsNullOrEmpty(value)) return 0m;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    private static decimal? ParseAmountNullable(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }
}
