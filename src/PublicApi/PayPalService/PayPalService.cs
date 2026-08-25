using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PayPalServerSdk;
using PayPalServerSdk.Core.Authentication.OAuth2.ClientCredentials;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Servers;

namespace Microsoft.eShopWeb.PublicApi.PayPalService;

public class PayPalService : IPayPalService
{
    private readonly PayPalServerSdkClient _client;
    private readonly string _currency;
    private readonly ILogger<PayPalService> _logger;

    public string Currency => _currency;

    public PayPalService(PayPalServerSdkClient client, IOptions<PayPalSettings> settings, ILogger<PayPalService> logger)
    {
        _client = client;
        _currency = settings.Value.Currency;
        _logger = logger;
    }

    public async Task<string> CreatePayPalOrderAsync(decimal amount, string currency, CancellationToken ct = default)
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
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return order.Id ?? throw new PayPalException("PayPal returned an order with no ID.");
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapOrderError(ex.Error, "create order");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<string> AuthorizeOrderAsync(
        string paypalOrderId,
        string idempotencyKey,
        CardPaymentDetails? card,
        string? vaultId,
        CancellationToken ct = default)
    {
        CardRequest? cardRequest = null;
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
                Name = card.Name
            };
        }

        var body = new OrderAuthorizeRequest
        {
            PaymentSource = cardRequest != null
                ? new OrderAuthorizeRequestPaymentSource { Card = cardRequest }
                : null
        };

        try
        {
            var response = await _client.Orders.AuthorizeOrder(
                id: paypalOrderId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var authId = response.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id;
            return authId ?? throw new PayPalException("PayPal authorization succeeded but returned no authorization ID.");
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapOrderError(ex.Error, "authorize order");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<(bool IsStale, bool CanReauthorize)> CheckAuthorizationAsync(
        string authorizationId,
        CancellationToken ct = default)
    {
        try
        {
            var auth = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                requestOptions: null,
                ct: ct);

            if (auth.Status?.Value == AuthorizationStatus.Voided.Value ||
                auth.Status?.Value == AuthorizationStatus.Denied.Value)
            {
                return (IsStale: true, CanReauthorize: false);
            }

            // Check expiration: ExpirationTime = 3-day honor period expiry
            // CreateTime = original creation; reauth window is day 4-29
            var isExpired = false;
            if (auth.ExpirationTime != null &&
                DateTimeOffset.TryParse(auth.ExpirationTime, out var expiry))
            {
                isExpired = expiry < DateTimeOffset.UtcNow;
            }

            if (!isExpired)
                return (IsStale: false, CanReauthorize: false);

            // Within reauth window?
            var canReauth = true;
            if (auth.CreateTime != null &&
                DateTimeOffset.TryParse(auth.CreateTime, out var created))
            {
                var daysSinceCreation = (DateTimeOffset.UtcNow - created).TotalDays;
                canReauth = daysSinceCreation >= 4 && daysSinceCreation < 29;
            }

            return (IsStale: true, CanReauthorize: canReauth);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, "get authorization");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<string> ReauthorizeAsync(
        string authorizationId,
        decimal amount,
        string currency,
        CancellationToken ct = default)
    {
        var body = new ReauthorizeRequest
        {
            Amount = new Money { CurrencyCode = currency, Value = amount.ToString("F2") }
        };

        try
        {
            var auth = await _client.Payments.ReauthorizePayment(
                authorizationId: authorizationId,
                payPalRequestId: null,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            return auth.Id ?? throw new PayPalException("PayPal reauthorization returned no authorization ID.");
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, "reauthorize payment");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<CaptureResult> CaptureAsync(
        string authorizationId,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var body = new CaptureRequest { FinalCapture = true };

        try
        {
            var capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authorizationId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: body,
                prefer: "return=representation",
                requestOptions: null,
                ct: ct);

            var captureId = capture.Id ?? throw new PayPalException("PayPal capture returned no capture ID.");
            var grossAmount = ParseMoney(capture.SellerReceivableBreakdown?.GrossAmount?.Value);
            var fee = ParseMoney(capture.SellerReceivableBreakdown?.PaypalFee?.Value);
            var net = ParseMoney(capture.SellerReceivableBreakdown?.NetAmount?.Value);

            return new CaptureResult(captureId, grossAmount, fee, net);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, "capture payment");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
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
            // 409 = already voided/captured — idempotent success
            if (ex.Error.TryGetError(out var err) && err != null)
            {
                _logger.LogWarning("PayPal void returned error {Name}: {Message}", err.Name, err.Message);
            }
            // treat as success for idempotency on cancel
        }
        catch (System.Text.Json.JsonException)
        {
            // VoidPayment returns 204 (no body); SDK may throw JsonException trying to parse empty response.
            // A 204 means the void succeeded — treat as success.
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<RefundResult> RefundAsync(
        string captureId,
        decimal? amount,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default)
    {
        var body = amount.HasValue
            ? new RefundRequest { Amount = new Money { CurrencyCode = currency, Value = amount.Value.ToString("F2") } }
            : new RefundRequest();

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

            return new RefundResult(refund.Id ?? throw new PayPalException("PayPal refund returned no refund ID."));
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            throw MapPaymentsError(ex.Error, "refund payment");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    // PayPal customer ID must be alphanumeric only, max 22 chars.
    private static string ToPayPalCustomerId(string buyerId)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(buyerId));
        return Convert.ToHexString(bytes)[..22].ToLowerInvariant();
    }

    public async Task<string> VaultCardAsync(string customerId, CardVaultRequest request, CancellationToken ct = default)
    {
        var paypalCustomerId = ToPayPalCustomerId(customerId);
        var body = new PaymentTokenRequest
        {
            Customer = new Customer { Id = paypalCustomerId },
            PaymentSource = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Number = request.Number,
                    Expiry = request.Expiry,
                    SecurityCode = request.SecurityCode,
                    Name = request.Name
                }
            }
        };

        try
        {
            var response = await _client.Vault.CreatePaymentToken(
                payPalRequestId: null,
                body: body,
                requestOptions: null,
                ct: ct);

            return response.Id ?? throw new PayPalException("PayPal vault returned no token ID.");
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out var err) && err != null)
                throw new PayPalException($"Card vault failed: {err.Message}", HttpStatusCode.UnprocessableEntity);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Card vault failed: {raw?.ReadAsString()}", HttpStatusCode.UnprocessableEntity);
            throw new PayPalException("Card vault failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<SavedCardInfo>> ListCardsAsync(string customerId, CancellationToken ct = default)
    {
        var paypalCustomerId = ToPayPalCustomerId(customerId);
        try
        {
            var response = await _client.Vault.ListCustomerPaymentTokens(
                customerId: paypalCustomerId,
                pageSize: 20,
                page: 1,
                totalRequired: false,
                requestOptions: null,
                ct: ct);

            var result = new List<SavedCardInfo>();
            if (response.PaymentTokens == null) return result;

            foreach (var token in response.PaymentTokens)
            {
                var card = token.PaymentSource?.Card;
                result.Add(new SavedCardInfo(
                    PaymentMethodId: token.Id ?? "",
                    Last4: card?.LastDigits,
                    Brand: card?.Brand?.Value,
                    Expiry: card?.Expiry));
            }
            return result;
        }
        catch (SdkException<ListCustomerPaymentTokensError> ex)
        {
            if (ex.Error.TryGetError1(out var err) && err != null)
                throw new PayPalException($"List cards failed: {err.Message}");
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"List cards failed: {raw?.ReadAsString()}");
            throw new PayPalException("List cards failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task DeleteCardAsync(string tokenId, CancellationToken ct = default)
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
            if (ex.Error.TryGetError1(out var err) && err != null)
                throw new PayPalException($"Delete card failed: {err.Message}", HttpStatusCode.UnprocessableEntity);
            if (ex.Error.TryGetRawError(out var raw))
                throw new PayPalException($"Delete card failed: {raw?.ReadAsString()}", HttpStatusCode.UnprocessableEntity);
            throw new PayPalException("Delete card failed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    public async Task<IReadOnlyList<TransactionRecord>> GetTransactionsAsync(
        string from,
        string to,
        CancellationToken ct = default)
    {
        var all = new List<TransactionRecord>();

        try
        {
            // Page 1
            var first = await _client.TransactionSearch.SearchTransactions(
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
                page: 1,
                requestOptions: null,
                ct: ct);

            AppendTransactions(all, first);

            var totalPages = first.TotalPages ?? 1;
            for (int page = 2; page <= totalPages; page++)
            {
                var pageResult = await _client.TransactionSearch.SearchTransactions(
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
                    requestOptions: null,
                    ct: ct);

                AppendTransactions(all, pageResult);
            }

            return all;
        }
        catch (SdkException<RawError> ex)
        {
            throw new PayPalException(
                $"Transaction search failed: HTTP {(int)ex.Error.StatusCode} — {ex.Error.ReadAsString()}",
                (HttpStatusCode)ex.Error.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal is unreachable.", ex);
        }
    }

    private static void AppendTransactions(List<TransactionRecord> list, SearchResponse page)
    {
        if (page.TransactionDetails == null) return;
        foreach (var detail in page.TransactionDetails)
        {
            var info = detail.TransactionInfo;
            list.Add(new TransactionRecord(
                TransactionId: info?.TransactionId,
                PaypalReferenceId: info?.PaypalReferenceId,
                InvoiceId: info?.InvoiceId,
                Status: info?.TransactionStatus,
                Amount: info?.TransactionAmount?.Value,
                Currency: info?.TransactionAmount?.CurrencyCode,
                FeeAmount: info?.FeeAmount?.Value,
                InitiationDate: info?.TransactionInitiationDate));
        }
    }

    private static PayPalException MapOrderError(CreateOrderError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static PayPalException MapOrderError(AuthorizeOrderError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static PayPalException MapPaymentsError(GetAuthorizedPaymentError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetNoContent(out var nc) && nc != null)
            return new PayPalException($"PayPal {operation} returned unexpected empty response.", HttpStatusCode.BadGateway);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static PayPalException MapPaymentsError(ReauthorizePaymentError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetNoContent(out var nc) && nc != null)
            return new PayPalException($"PayPal {operation} returned unexpected empty response.", HttpStatusCode.BadGateway);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static PayPalException MapPaymentsError(CaptureAuthorizedPaymentError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetNoContent(out var nc) && nc != null)
            return new PayPalException($"PayPal {operation} returned unexpected empty response.", HttpStatusCode.BadGateway);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static PayPalException MapPaymentsError(RefundCapturedPaymentError error, string operation)
    {
        if (error.TryGetError(out var e) && e != null)
            return new PayPalException($"PayPal {operation}: {e.Name} — {e.Message}", HttpStatusCode.UnprocessableEntity);
        if (error.TryGetNoContent(out var nc) && nc != null)
            return new PayPalException($"PayPal {operation} returned unexpected empty response.", HttpStatusCode.BadGateway);
        if (error.TryGetRawError(out var raw) && raw != null)
            return new PayPalException($"PayPal {operation} failed: {raw.ReadAsString()}", (HttpStatusCode)raw.StatusCode);
        return new PayPalException($"PayPal {operation} failed.");
    }

    private static decimal ParseMoney(string? value)
    {
        if (value == null) return 0m;
        return decimal.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }
}
