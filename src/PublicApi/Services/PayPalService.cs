using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;
using PayPalOrderStatus = PayPalServerSdk.Models.Enums.OrderStatus;

namespace Microsoft.eShopWeb.PublicApi.Services;

public record AuthorizePaymentResult(string PayPalOrderId, string AuthorizationId);

public record CapturePaymentResult(
    string CaptureId,
    decimal CapturedAmount,
    decimal PayPalFee,
    decimal NetAmount,
    string? NewAuthorizationId);

public record RefundPaymentResult(string RefundId, string Status);

public record VaultCardResult(
    string VaultTokenId,
    string PayPalCustomerId,
    string LastDigits,
    string Brand,
    string? Expiry);

public class PayPalPaymentException : Exception
{
    public int? StatusCode { get; }
    public PayPalPaymentException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }
}

public class PayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    public PayPalService(PayPalServerSdkClient client, IConfiguration configuration)
    {
        _client = client;
        _currency = configuration["PayPal:Currency"] ?? "USD";
    }

    // ──────────────────────────────── AUTHORIZE ────────────────────────────────

    public async Task<AuthorizePaymentResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardDetails? card,
        string? vaultTokenId,
        CancellationToken ct)
    {
        PaymentSource paymentSource;
        if (vaultTokenId != null)
        {
            paymentSource = new PaymentSource
            {
                Token = new Token
                {
                    Id = vaultTokenId,
                    Type = TokenType.BillingAgreement
                }
            };
        }
        else if (card != null)
        {
            paymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = card.BillingCountryCode != null
                        ? new Address { CountryCode = card.BillingCountryCode }
                        : null
                }
            };
        }
        else
        {
            throw new ArgumentException("Either card or vaultTokenId must be provided");
        }

        var idempotencyKey = $"auth-{orderId}";

        Order ppOrder;
        try
        {
            ppOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
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
                                Value = amount.ToString("F2")
                            },
                            CustomId = orderId.ToString()
                        }
                    },
                    PaymentSource = paymentSource
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw TranslateCreateOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during order creation.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        if (ppOrder.Status == PayPalOrderStatus.PayerActionRequired)
        {
            var approveLink = FindLink(ppOrder.Links, "approve");
            throw new PayPalPaymentException(
                $"PayPal requires browser approval for this payment. Approve at: {approveLink ?? "(no link returned)"}",
                statusCode: 422);
        }

        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: ppOrder.Id!,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw TranslateAuthorizeOrderError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during authorization.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        var authorization = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]
            ?? throw new PayPalPaymentException("PayPal did not return an authorization ID.");

        return new AuthorizePaymentResult(ppOrder.Id!, authorization.Id!);
    }

    // ──────────────────────────────── CAPTURE ────────────────────────────────

    public async Task<CapturePaymentResult> CaptureAsync(
        int orderId,
        string authorizationId,
        decimal amount,
        CancellationToken ct)
    {
        var (authToCapture, newAuthId) = await EnsureCaptureableAuthorizationAsync(
            orderId, authorizationId, amount, ct);

        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authToCapture,
                payPalMockResponse: null,
                payPalRequestId: $"capture-{orderId}",
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw TranslateCaptureError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during capture.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        var breakdown = capture.SellerReceivableBreakdown;
        var capturedAmount = ParseMoney(breakdown?.GrossAmount);
        var fee = ParseMoney(breakdown?.PaypalFee);
        var net = ParseMoney(breakdown?.NetAmount);

        return new CapturePaymentResult(
            CaptureId: capture.Id!,
            CapturedAmount: capturedAmount,
            PayPalFee: fee,
            NetAmount: net,
            NewAuthorizationId: newAuthId);
    }

    private async Task<(string AuthorizationId, string? NewAuthorizationId)> EnsureCaptureableAuthorizationAsync(
        int orderId, string authorizationId, decimal amount, CancellationToken ct)
    {
        PaymentAuthorization auth;
        try
        {
            auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw TranslateGetAuthError(ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response checking authorization.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        var status = auth.Status;

        if (status == AuthorizationStatus.Captured || status == AuthorizationStatus.PartiallyCaptured)
            return (authorizationId, null);

        if (status == AuthorizationStatus.Voided)
            throw new PayPalPaymentException("Authorization has been voided and cannot be captured.", statusCode: 422);

        if (status == AuthorizationStatus.Denied)
            throw new PayPalPaymentException("Authorization was denied by PayPal.", statusCode: 422);

        if (status == AuthorizationStatus.Pending)
            throw new PayPalPaymentException("Authorization is pending. Please try again shortly.", statusCode: 422);

        // Status is Created — check if expired
        if (auth.ExpirationTime != null &&
            DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry) &&
            expiry < DateTimeOffset.UtcNow)
        {
            return await ReauthorizeAsync(orderId, authorizationId, amount, ct);
        }

        return (authorizationId, null);
    }

    private async Task<(string AuthorizationId, string? NewAuthorizationId)> ReauthorizeAsync(
        int orderId, string originalAuthId, decimal amount, CancellationToken ct)
    {
        try
        {
            var reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: originalAuthId,
                payPalRequestId: $"reauth-{orderId}",
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money { CurrencyCode = _currency, Value = amount.ToString("F2") }
                },
                prefer: "return=representation",
                ct: ct);

            return (reauth.Id!, reauth.Id);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PayPalPaymentException(
                    $"Authorization expired and cannot be renewed (PayPal: {err?.Message ?? "unknown"}). " +
                    "Please re-collect payment from the shopper.", statusCode: 422, inner: ex);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalPaymentException(
                    "Authorization expired and cannot be renewed. Please re-collect payment from the shopper.",
                    statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException(
                    $"Authorization renewal failed (HTTP {(int)raw.StatusCode}). Please re-collect payment from the shopper.",
                    statusCode: 422, inner: ex);

            throw new PayPalPaymentException("Authorization renewal failed unexpectedly.", statusCode: 422, inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during reauthorization.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable during reauthorization. Please try again.", inner: ex);
        }
    }

    // ──────────────────────────────── VOID ────────────────────────────────

    public async Task VoidAsync(string authorizationId, CancellationToken ct)
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
            if (ex.Error.TryGetError(out var err))
                throw new PayPalPaymentException($"Cannot void authorization: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalPaymentException("Authorization void failed.", statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException($"Void failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
            throw new PayPalPaymentException("Void failed unexpectedly.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during void.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }
    }

    // ──────────────────────────────── REFUND ────────────────────────────────

    public async Task<RefundPaymentResult> RefundAsync(
        string captureId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken ct)
    {
        Refund refund;
        try
        {
            refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: new RefundRequest
                {
                    Amount = amount.HasValue
                        ? new Money { CurrencyCode = _currency, Value = amount.Value.ToString("F2") }
                        : null
                },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out var err))
                throw new PayPalPaymentException($"Refund failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalPaymentException("Refund failed.", statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException($"Refund failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
            throw new PayPalPaymentException("Refund failed unexpectedly.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during refund.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        return new RefundPaymentResult(refund.Id!, refund.Status?.Value ?? "PENDING");
    }

    // ──────────────────────────────── TRANSACTIONS ────────────────────────────────

    public async Task<IReadOnlyList<TransactionDetails>> GetTransactionsAsync(
        string from, string to, CancellationToken ct)
    {
        var all = new List<TransactionDetails>();
        int page = 1;
        SearchResponse response;

        do
        {
            try
            {
                response = await _client.TransactionSearch.SearchTransactions(
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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalPaymentException(
                    $"Transaction search failed (HTTP {(int)ex.Error.StatusCode}): {ex.Error.ReadAsString()}",
                    statusCode: (int)ex.Error.StatusCode, inner: ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalPaymentException("PayPal returned an unreadable response during transaction search.", inner: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
            }

            if (response.TransactionDetails != null)
                all.AddRange(response.TransactionDetails);

            page++;
        } while (response.TotalPages.HasValue && page <= response.TotalPages.Value);

        return all;
    }

    // ──────────────────────────────── VAULT — SAVE CARD ────────────────────────────────

    public async Task<VaultCardResult> SaveCardAsync(
        string? existingPayPalCustomerId,
        CardDetails card,
        CancellationToken ct)
    {
        // Step 9A — CreateSetupToken
        SetupTokenResponse setupToken;
        try
        {
            setupToken = await _client.Vault.CreateSetupToken(
                payPalRequestId: null,
                body: new SetupTokenRequest
                {
                    PaymentSource = new SetupTokenRequestPaymentSource
                    {
                        Card = new SetupTokenRequestCard
                        {
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            Name = card.Name,
                            BillingAddress = card.BillingCountryCode != null
                                ? new Address { CountryCode = card.BillingCountryCode }
                                : null
                        }
                    },
                    Customer = existingPayPalCustomerId != null
                        ? new Customer { Id = existingPayPalCustomerId }
                        : null
                },
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalPaymentException($"Card vault setup failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException($"Card vault setup failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
            throw new PayPalPaymentException("Card vault setup failed unexpectedly.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during card vault setup.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        if (setupToken.Status == PaymentTokenStatus.PayerActionRequired)
        {
            var approveLink = FindLink(setupToken.Links, "approve");
            throw new PayPalPaymentException(
                $"PayPal requires browser approval to vault this card. Approve at: {approveLink ?? "(no link returned)"}",
                statusCode: 422);
        }

        // Step 9B — CreatePaymentToken
        PaymentTokenResponse paymentToken;
        try
        {
            paymentToken = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setupToken.Id!,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalPaymentException($"Card vaulting failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException($"Card vaulting failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
            throw new PayPalPaymentException("Card vaulting failed unexpectedly.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during card vaulting.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }

        var cardInfo = paymentToken.PaymentSource?.Card;
        return new VaultCardResult(
            VaultTokenId: paymentToken.Id!,
            PayPalCustomerId: paymentToken.Customer?.Id ?? setupToken.Customer?.Id ?? string.Empty,
            LastDigits: cardInfo?.LastDigits ?? string.Empty,
            Brand: cardInfo?.Brand?.Value ?? string.Empty,
            Expiry: cardInfo?.Expiry);
    }

    // ──────────────────────────────── VAULT — DELETE CARD ────────────────────────────────

    public async Task DeleteCardAsync(string vaultTokenId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultTokenId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err))
                throw new PayPalPaymentException($"Card deletion failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalPaymentException($"Card deletion failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
            throw new PayPalPaymentException("Card deletion failed unexpectedly.", inner: ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalPaymentException("PayPal returned an unreadable response during card deletion.", inner: ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalPaymentException("PayPal is unreachable. Please try again.", inner: ex);
        }
    }

    // ──────────────────────────────── HELPERS ────────────────────────────────

    private static decimal ParseMoney(Money? money)
    {
        if (money?.Value == null) return 0m;
        return decimal.TryParse(money.Value, out var v) ? v : 0m;
    }

    private static string? FindLink(IReadOnlyList<LinkDescription>? links, string rel)
        => links?.FirstOrDefault(l => l.Rel == rel)?.Href;

    private static PayPalPaymentException TranslateCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            return new PayPalPaymentException($"Order creation failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalPaymentException($"Order creation failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
        return new PayPalPaymentException("Order creation failed unexpectedly.", inner: ex);
    }

    private static PayPalPaymentException TranslateAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            return new PayPalPaymentException($"Authorization failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalPaymentException($"Authorization failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
        return new PayPalPaymentException("Authorization failed unexpectedly.", inner: ex);
    }

    private static PayPalPaymentException TranslateGetAuthError(SdkException<GetAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            return new PayPalPaymentException($"Could not retrieve authorization: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalPaymentException("Authorization not found.", statusCode: 404, inner: ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalPaymentException($"Authorization retrieval failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
        return new PayPalPaymentException("Authorization retrieval failed unexpectedly.", inner: ex);
    }

    private static PayPalPaymentException TranslateCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            return new PayPalPaymentException($"Capture failed: {err?.Message ?? "unknown"}", statusCode: 422, inner: ex);
        if (ex.Error.TryGetNoContent(out _))
            return new PayPalPaymentException("Capture failed (no content).", statusCode: 422, inner: ex);
        if (ex.Error.TryGetRawError(out var raw))
            return new PayPalPaymentException($"Capture failed (HTTP {(int)raw.StatusCode}).", statusCode: (int)raw.StatusCode, inner: ex);
        return new PayPalPaymentException("Capture failed unexpectedly.", inner: ex);
    }
}

public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string? Name,
    string? BillingCountryCode);
