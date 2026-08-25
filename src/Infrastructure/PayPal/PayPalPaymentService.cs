using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
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

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;

    // Per-startup unique prefix keeps idempotency keys fresh across server restarts.
    // In production (SQL Server), orderId auto-increments and never repeats, so the
    // prefix is empty. In development with UseOnlyInMemoryDatabase, orderId resets
    // to 1 on every restart, which would collide with PayPal's 30-day idempotency
    // cache. The prefix avoids that by making each restart logically distinct.
    private static readonly string _runPrefix = Guid.NewGuid().ToString("N")[..8];

    private static string IKey(string pattern) => $"{_runPrefix}-{pattern}";

    public PayPalPaymentService(PayPalServerSdkClient client, string currency)
    {
        _client = client;
        _currency = currency;
    }

    public async Task<AuthorizeResult> AuthorizeAsync(
        int orderId,
        decimal amount,
        CardDetails? card,
        string? savedCardTokenId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        PaymentSource paymentSource;
        if (savedCardTokenId != null)
        {
            // Use CardRequest.VaultId — TokenType.BillingAgreement is rejected by PayPal for vault tokens.
            paymentSource = new PaymentSource
            {
                Card = new CardRequest
                {
                    VaultId = savedCardTokenId
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
            throw new ArgumentException("Either card or savedCardTokenId must be provided.");
        }

        var body = new OrderRequest
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
                    CustomId = orderId.ToString(),
                    InvoiceId = IKey($"inv-{orderId}")
                }
            },
            PaymentSource = paymentSource
        };

        try
        {
            var order = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: IKey(idempotencyKey),
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var payPalOrderId = order.Id
                ?? throw new PayPalOperationException("PayPal order ID missing from response.", HttpStatusCode.BadGateway);

            var authId = order.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
                ?? throw new PayPalOperationException("Authorization ID missing from PayPal response. If paying with a saved card token type is unsupported, try a direct card payment.", HttpStatusCode.UnprocessableEntity);

            return new AuthorizeResult(payPalOrderId, authId);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            if (ex.Error.TryGetError(out Error? e) && e != null)
            {
                var issues = e.Details != null
                    ? string.Join("; ", e.Details.Select(d => $"{d.Issue}: {d.Description}"))
                    : string.Empty;
                throw new PayPalOperationException(
                    $"PayPal error [{e.Name}]: {e.Message}{(issues.Length > 0 ? " | " + issues : "")}",
                    HttpStatusCode.UnprocessableEntity, ex);
            }
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal error: HTTP {(int)raw.StatusCode} {raw.ReadAsString()}", raw.StatusCode, ex);
            throw new PayPalOperationException("PayPal authorization failed.", HttpStatusCode.BadGateway, ex);
        }
        catch (PayPalOperationException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    public async Task<CaptureResult> CaptureWithBreakdownAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        try
        {
            var captured = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: IKey(idempotencyKey),
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var captureId = captured.Id
                ?? throw new PayPalOperationException("Capture ID missing from PayPal response.", HttpStatusCode.BadGateway);

            decimal capturedAmount = 0m;
            if (captured.SellerReceivableBreakdown?.GrossAmount?.Value is string gross &&
                decimal.TryParse(gross, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var g))
                capturedAmount = g;
            else if (captured.Amount?.Value is string amt &&
                decimal.TryParse(amt, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a))
                capturedAmount = a;

            decimal? fee = null;
            if (captured.SellerReceivableBreakdown?.PaypalFee?.Value is string feeStr &&
                decimal.TryParse(feeStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var f))
                fee = f;

            decimal? net = null;
            if (captured.SellerReceivableBreakdown?.NetAmount?.Value is string netStr &&
                decimal.TryParse(netStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var n))
                net = n;

            return new CaptureResult(captureId, capturedAmount, fee, net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? e) && e != null)
                throw new PayPalOperationException($"PayPal error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc != null)
                throw new PayPalOperationException("PayPal returned no content.", HttpStatusCode.BadGateway, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("PayPal capture failed.", HttpStatusCode.BadGateway, ex);
        }
        catch (PayPalOperationException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    public Task CaptureAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
        => CaptureWithBreakdownAsync(authorizationId, idempotencyKey, ct);

    public async Task<RenewAuthResult> RenewAuthorizationIfNeededAsync(
        string authorizationId,
        decimal amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
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
            if (ex.Error.TryGetError(out Error? e) && e != null)
                throw new PayPalOperationException($"PayPal error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc != null)
                throw new PayPalOperationException("PayPal returned no content.", HttpStatusCode.BadGateway, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("Failed to retrieve authorization.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }

        bool needsRenewal = auth.ExpirationTime != null &&
            DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry) &&
            expiry < DateTimeOffset.UtcNow;

        if (!needsRenewal)
            return new RenewAuthResult(false, authorizationId);

        try
        {
            var renewed = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: IKey(idempotencyKey),
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest
                {
                    Amount = new Money
                    {
                        CurrencyCode = currency,
                        Value = amount.ToString("F2")
                    }
                },
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var newAuthId = renewed.Id
                ?? throw new PayPalOperationException("Renewed authorization ID missing.", HttpStatusCode.BadGateway);

            return new RenewAuthResult(true, newAuthId);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            string msg = "Authorization cannot be renewed; please create a new order for this shopper.";
            if (ex.Error.TryGetError(out Error? e) && e != null)
                msg = $"Authorization cannot be renewed: {e.Message}. Create a new order.";
            return new RenewAuthResult(false, authorizationId, msg);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    public async Task VoidAsync(string authorizationId, string idempotencyKey, CancellationToken ct = default)
    {
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: IKey(idempotencyKey),
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? e) && e != null)
                throw new PayPalOperationException($"PayPal error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc != null)
                throw new PayPalOperationException("PayPal returned no content.", HttpStatusCode.BadGateway, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("PayPal void failed.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException)
        {
            // VoidPayment returns 204 No Content on success; the SDK may throw JsonException
            // trying to deserialize the empty body. Treat as success.
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? partialAmount,
        string currency,
        string idempotencyKey,
        decimal capturedAmount = 0m,
        CancellationToken ct = default)
    {
        RefundRequest? body = null;
        if (partialAmount.HasValue)
        {
            body = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = currency,
                    Value = partialAmount.Value.ToString("F2")
                }
            };
        }

        try
        {
            var refund = await _client.Payments.RefundCapturedPayment(
                captureId: captureId,
                payPalMockResponse: null,
                payPalRequestId: IKey(idempotencyKey),
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=minimal",
                requestOptions: null,
                ct: ct);

            var refundId = refund.Id
                ?? throw new PayPalOperationException("Refund ID missing from PayPal response.", HttpStatusCode.BadGateway);

            // With return=minimal PayPal does not echo back the amount; the caller derives it from
            // the request (partialAmount) or passes the full captured amount via capturedAmount.
            decimal refundedAmount = partialAmount ?? capturedAmount;

            return new RefundResult(refundId, refundedAmount);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? e) && e != null)
                throw new PayPalOperationException($"PayPal error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetNoContent(out RawError? nc) && nc != null)
                throw new PayPalOperationException("PayPal returned no content.", HttpStatusCode.BadGateway, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("PayPal refund failed.", HttpStatusCode.BadGateway, ex);
        }
        catch (PayPalOperationException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    public async Task<IReadOnlyList<PayPalTransaction>> SearchTransactionsAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct = default)
    {
        var allDetails = new List<TransactionDetails>();
        int page = 1;
        int totalPages = 1;

        try
        {
            do
            {
                var resp = await _client.TransactionSearch.SearchTransactions(
                    startDate: from.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    endDate: to.ToString("yyyy-MM-ddTHH:mm:sszzz"),
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
                    allDetails.AddRange(resp.TransactionDetails);
                page++;
            }
            while (page <= totalPages);
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalOperationException(
                $"PayPal transaction search failed: HTTP {(int)ex.Error.StatusCode}",
                ex.Error.StatusCode,
                ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }

        var results = new List<PayPalTransaction>(allDetails.Count);
        foreach (var detail in allDetails)
        {
            var info = detail.TransactionInfo;
            if (info == null) continue;

            DateTimeOffset? initiated = null;
            if (info.TransactionInitiationDate != null &&
                DateTimeOffset.TryParse(info.TransactionInitiationDate, out var d))
                initiated = d;

            decimal? txAmount = null;
            if (info.TransactionAmount?.Value is string amtStr &&
                decimal.TryParse(amtStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ta))
                txAmount = ta;

            results.Add(new PayPalTransaction(
                TransactionId: info.TransactionId ?? string.Empty,
                ReferenceId: info.PaypalReferenceId,
                Amount: txAmount,
                Status: info.TransactionStatus,
                InitiatedAt: initiated,
                InvoiceId: info.InvoiceId));
        }

        return results;
    }

    public async Task<SavedCardResult> SaveCardAsync(
        string merchantCustomerId,
        CardDetails card,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        SetupTokenResponse setupToken;
        try
        {
            setupToken = await _client.Vault.CreateSetupToken(
                payPalRequestId: IKey(idempotencyKey + "-setup"),
                body: new SetupTokenRequest
                {
                    Customer = new Customer { Id = SafeCustomerId(merchantCustomerId) },
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
                    }
                },
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreateSetupTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? e) && e != null)
                throw new PayPalOperationException($"PayPal vault error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal vault error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("Failed to create PayPal setup token.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }

        var setupTokenId = setupToken.Id
            ?? throw new PayPalOperationException("Setup token ID missing from PayPal response.", HttpStatusCode.BadGateway);

        PaymentTokenResponse paymentToken;
        try
        {
            paymentToken = await _client.Vault.CreatePaymentToken(
                payPalRequestId: IKey(idempotencyKey + "-payment"),
                body: new PaymentTokenRequest
                {
                    Customer = new Customer { Id = SafeCustomerId(merchantCustomerId) },
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Token = new VaultTokenRequest
                        {
                            Id = setupTokenId,
                            Type = VaultTokenRequestType.SetupToken
                        }
                    }
                },
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? e) && e != null)
                throw new PayPalOperationException($"PayPal vault error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal vault error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("Failed to create PayPal payment token.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }

        var tokenId = paymentToken.Id
            ?? throw new PayPalOperationException("Payment token ID missing from PayPal response.", HttpStatusCode.BadGateway);

        var cardEntity = paymentToken.PaymentSource?.Card;
        return new SavedCardResult(
            PaymentTokenId: tokenId,
            LastFourDigits: cardEntity?.LastDigits,
            CardBrand: cardEntity?.Brand?.Value,
            Expiry: cardEntity?.Expiry);
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListSavedCardsAsync(
        string merchantCustomerId,
        CancellationToken ct = default)
    {
        try
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: SafeCustomerId(merchantCustomerId),
                pageSize: 50,
                page: 1,
                totalRequired: false,
                requestOptions: null,
                ct: ct);

            var results = new List<SavedCardInfo>();
            if (response.PaymentTokens == null) return results;

            foreach (var token in response.PaymentTokens)
            {
                if (token.Id == null) continue;
                var cardEntity = token.PaymentSource?.Card;
                results.Add(new SavedCardInfo(
                    PaymentTokenId: token.Id,
                    LastFourDigits: cardEntity?.LastDigits,
                    CardBrand: cardEntity?.Brand?.Value,
                    Expiry: cardEntity?.Expiry));
            }

            return results;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? e) && e != null)
                throw new PayPalOperationException($"PayPal vault error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal vault error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("Failed to list PayPal payment tokens.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    public async Task DeleteSavedCardAsync(string paymentTokenId, CancellationToken ct = default)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(
                id: paymentTokenId,
                requestOptions: null,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? e) && e != null)
                throw new PayPalOperationException($"PayPal vault error: {e.Message}", HttpStatusCode.UnprocessableEntity, ex);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw != null)
                throw new PayPalOperationException($"PayPal vault error: HTTP {(int)raw.StatusCode}", raw.StatusCode, ex);
            throw new PayPalOperationException("Failed to delete PayPal payment token.", HttpStatusCode.BadGateway, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalOperationException("PayPal service unreachable.", HttpStatusCode.BadGateway, ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalOperationException("PayPal returned an unreadable response.", HttpStatusCode.BadGateway, ex);
        }
    }

    // PayPal Customer.Id must be alphanumeric+hyphen, max 22 chars — hash email to a safe ID.
    private static string SafeCustomerId(string merchantCustomerId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(merchantCustomerId.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..22].ToLowerInvariant();
    }
}
