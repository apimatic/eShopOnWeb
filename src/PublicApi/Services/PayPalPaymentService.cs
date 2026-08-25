using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using PayPalServerSdk;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Errors;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using EShopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class PayPalException : Exception
{
    public int StatusCode { get; }
    public PayPalException(string message, int statusCode = 502) : base(message) { StatusCode = statusCode; }
    public PayPalException(string message, Exception inner, int statusCode = 502) : base(message, inner) { StatusCode = statusCode; }
}

public record CardDetails(
    string Number,
    string Expiry,
    string? SecurityCode,
    string Name,
    string CountryCode,
    string? AddressLine1,
    string? City,
    string? State,
    string? PostalCode);

public record AuthorizeResult(string PayPalOrderId, string AuthorizationId, string Currency, decimal Amount);
public record CaptureResult(string CaptureId, decimal CapturedAmount, decimal? PayPalFee, decimal? NetAmount);
public record RefundResult(string RefundId, decimal Amount, string Status);
public record VaultResult(string VaultId, string? PayPalCustomerId, string? Last4, string? Brand, string? Expiry);
public record TransactionMatch(
    string TransactionId,
    string? TransactionStatus,
    decimal? Amount,
    string? Currency,
    string? InitiationDate,
    int? MatchedOrderId,
    string? MatchNote);

public interface IPayPalPaymentService
{
    Task<AuthorizeResult> AuthorizeWithCardAsync(EShopOrder order, CardDetails card, string currency, CancellationToken ct);
    Task<AuthorizeResult> AuthorizeWithVaultAsync(EShopOrder order, string vaultId, string currency, CancellationToken ct);
    Task<CaptureResult> CaptureAsync(OrderPayment payment, CancellationToken ct);
    Task VoidAsync(OrderPayment payment, CancellationToken ct);
    Task<RefundResult> RefundAsync(OrderPayment payment, string idempotencyKey, decimal? amount, CancellationToken ct);
    Task<IReadOnlyList<TransactionMatch>> ReconcileAsync(string startDate, string endDate, IReadOnlyList<OrderPayment> knownPayments, CancellationToken ct);
    Task<VaultResult> VaultCardAsync(string buyerId, CardDetails card, CancellationToken ct);
    Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct);
}

public class PayPalPaymentService : IPayPalPaymentService
{
    private readonly PayPalServerSdkClient _client;

    public PayPalPaymentService(PayPalServerSdkClient client)
    {
        _client = client;
    }

    public async Task<AuthorizeResult> AuthorizeWithCardAsync(EShopOrder order, CardDetails card, string currency, CancellationToken ct)
    {
        var (ppOrderId, authId) = await AuthorizeInternalAsync(order, currency,
            new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest
                {
                    Number = card.Number,
                    Expiry = card.Expiry,
                    SecurityCode = card.SecurityCode,
                    Name = card.Name,
                    BillingAddress = new PayPalServerSdk.Models.Address
                    {
                        CountryCode = card.CountryCode,
                        AddressLine1 = card.AddressLine1,
                        AdminArea2 = card.City,
                        AdminArea1 = card.State,
                        PostalCode = card.PostalCode
                    }
                }
            }, ct);
        return new AuthorizeResult(ppOrderId, authId, currency, order.Total());
    }

    public async Task<AuthorizeResult> AuthorizeWithVaultAsync(EShopOrder order, string vaultId, string currency, CancellationToken ct)
    {
        var (ppOrderId, authId) = await AuthorizeInternalAsync(order, currency,
            new OrderAuthorizeRequestPaymentSource
            {
                Card = new CardRequest { VaultId = vaultId }
            }, ct);
        return new AuthorizeResult(ppOrderId, authId, currency, order.Total());
    }

    private async Task<(string ppOrderId, string authId)> AuthorizeInternalAsync(
        EShopOrder order, string currency,
        OrderAuthorizeRequestPaymentSource paymentSource,
        CancellationToken ct)
    {
        var total = order.Total();
        var createKey = $"{order.Id}-create";
        var authKey = $"{order.Id}-auth";

        // Step 1: Create PayPal order with AUTHORIZE intent
        PayPalServerSdk.Models.Order ppOrder;
        try
        {
            ppOrder = await _client.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: createKey,
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
                                Value = total.ToString("F2")
                            },
                            ReferenceId = order.Id.ToString()
                        }
                    }
                },
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            throw MapCreateOrderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        var ppOrderId = ppOrder.Id ?? throw new PayPalException("PayPal did not return an order ID.");

        // Step 2: Authorize the order (attach payment source here)
        OrderAuthorizeResponse authResponse;
        try
        {
            authResponse = await _client.Orders.AuthorizeOrder(
                id: ppOrderId,
                payPalMockResponse: null,
                payPalRequestId: authKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: new OrderAuthorizeRequest { PaymentSource = paymentSource },
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            throw MapAuthorizeOrderError(ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        if (authResponse.Status == OrderStatus.PayerActionRequired)
            throw new PayPalException("This card requires browser-based 3D Secure authentication, which is not supported. Use a different card.", 422);

        var authId = authResponse.PurchaseUnits?[0]?.Payments?.Authorizations?[0]?.Id
            ?? throw new PayPalException("PayPal authorization succeeded but did not return an authorization ID.");

        return (ppOrderId, authId);
    }

    public async Task<CaptureResult> CaptureAsync(OrderPayment payment, CancellationToken ct)
    {
        var authId = payment.AuthorizationId
            ?? throw new PayPalException("No authorization ID on this payment.", 422);

        // Check authorization status; reauthorize if stale
        authId = await EnsureValidAuthorizationAsync(payment, authId, ct);

        var captureKey = payment.CaptureIdempotencyKey ?? $"{payment.OrderId}-capture-{DateTimeOffset.UtcNow.Ticks}";

        CapturedPayment capture;
        try
        {
            capture = await _client.Payments.CaptureAuthorizedPayment(
                authorizationId: authId,
                payPalMockResponse: null,
                payPalRequestId: captureKey,
                payPalAuthAssertion: null,
                body: new CaptureRequest { FinalCapture = true },
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body is not null)
                throw new PayPalException($"Capture failed: {body.Message} ({body.Name})", 422);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("PayPal internal error during capture.", 502);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
                throw new PayPalException($"Capture failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Capture failed with an unknown error.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        var captureId = capture.Id ?? throw new PayPalException("PayPal did not return a capture ID.");
        var capturedAmount = decimal.Parse(capture.Amount?.Value ?? "0");
        var fee = capture.SellerReceivableBreakdown?.PaypalFee?.Value is string feeStr ? decimal.Parse(feeStr) : (decimal?)null;
        var net = capture.SellerReceivableBreakdown?.NetAmount?.Value is string netStr ? decimal.Parse(netStr) : (decimal?)null;

        return new CaptureResult(captureId, capturedAmount, fee, net);
    }

    private async Task<string> EnsureValidAuthorizationAsync(OrderPayment payment, string authId, CancellationToken ct)
    {
        PaymentAuthorization authStatus;
        try
        {
            authStatus = await _client.Payments.GetAuthorizedPayment(
                authorizationId: authId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                ct: ct);
        }
        catch (SdkException<GetAuthorizedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body is not null)
                throw new PayPalException($"Could not retrieve authorization: {body.Message}", 502);
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("PayPal returned no content for authorization lookup.", 502);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
                throw new PayPalException($"Authorization lookup failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Authorization lookup failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        bool isStale = authStatus.Status != AuthorizationStatus.Created;
        if (!isStale && authStatus.ExpirationTime is string expStr)
        {
            if (DateTimeOffset.TryParse(expStr, out var expiry) && expiry <= DateTimeOffset.UtcNow)
                isStale = true;
        }

        if (!isStale) return authId;

        // Check reauth window: only valid days 4-29 after original authorization
        var daysSinceAuth = (DateTimeOffset.UtcNow - (payment.AuthorizedAt ?? payment.CreatedAt)).TotalDays;
        if (daysSinceAuth > 29)
            throw new PayPalException("The payment authorization has expired and can no longer be renewed. Please cancel this order and create a new one.", 422);

        // Attempt reauthorization
        var reauthKey = $"{payment.OrderId}-reauth-{DateTimeOffset.UtcNow.Ticks}";
        PaymentAuthorization reauth;
        try
        {
            reauth = await _client.Payments.ReauthorizePayment(
                authorizationId: authId,
                payPalRequestId: reauthKey,
                payPalAuthAssertion: null,
                body: new ReauthorizeRequest { Amount = null },
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<ReauthorizePaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body is not null)
            {
                if (body.Name == "AUTHORIZATION_ALREADY_CAPTURED" || body.Name?.Contains("EXPIRED") == true)
                    throw new PayPalException("Authorization expired and cannot be renewed. Cancel and reorder.", 422);
                throw new PayPalException($"Reauthorization failed: {body.Message}", 422);
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("PayPal internal error during reauthorization.", 502);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
                throw new PayPalException($"Reauthorization failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Reauthorization failed.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        return reauth.Id ?? throw new PayPalException("Reauthorization succeeded but PayPal did not return a new authorization ID.");
    }

    public async Task VoidAsync(OrderPayment payment, CancellationToken ct)
    {
        var authId = payment.AuthorizationId
            ?? throw new PayPalException("No authorization ID on this payment.", 422);

        var voidKey = $"{payment.OrderId}-void";
        try
        {
            await _client.Payments.VoidPayment(
                authorizationId: authId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: voidKey,
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body is not null)
            {
                if (body.Name == "AUTHORIZATION_ALREADY_VOIDED")
                    return; // idempotent — already voided
                if (body.Name == "AUTHORIZATION_ALREADY_CAPTURED")
                    throw new PayPalException("Cannot cancel: the payment has already been captured.", 409);
                throw new PayPalException($"Void failed: {body.Message} ({body.Name})", 422);
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("PayPal internal error during void.", 502);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
                throw new PayPalException($"Void failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Void failed with an unknown error.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException)
        {
            // PayPal void returns 204 No Content; the SDK tries to deserialize the empty body as
            // PaymentAuthorization and throws JsonException. An empty-body 204 means success.
        }
    }

    public async Task<RefundResult> RefundAsync(OrderPayment payment, string idempotencyKey, decimal? amount, CancellationToken ct)
    {
        var captureId = payment.CaptureId
            ?? throw new PayPalException("No capture ID: this order has not been fulfilled.", 422);

        PayPalServerSdk.Models.Refund refund;
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
                        ? new Money { CurrencyCode = payment.Currency, Value = amount.Value.ToString("F2") }
                        : null
                },
                prefer: "return=minimal",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            if (ex.Error.TryGetError(out Error? body) && body is not null)
            {
                if (body.Name == "DUPLICATE_INVOICE_ID" || body.Name == "DUPLICATE_REQUEST_ID")
                    throw new PayPalException("A refund with this idempotency key was already submitted.", 409);
                if (body.Name == "REFUND_AMOUNT_EXCEEDED")
                    throw new PayPalException("Refund amount exceeds the capturable amount.", 422);
                throw new PayPalException($"Refund failed: {body.Message} ({body.Name})", 422);
            }
            if (ex.Error.TryGetNoContent(out _))
                throw new PayPalException("PayPal internal error during refund.", 502);
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
            {
                if (raw.StatusCode == HttpStatusCode.Conflict)
                    throw new PayPalException("Refund conflict: duplicate or over-refund.", 409);
                throw new PayPalException($"Refund failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            }
            throw new PayPalException("Refund failed with an unknown error.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        var refundId = refund.Id ?? throw new PayPalException("PayPal did not return a refund ID.");
        var refundedAmount = decimal.Parse(refund.Amount?.Value ?? (amount?.ToString("F2") ?? "0"));
        var status = refund.Status?.Value ?? "COMPLETED";

        return new RefundResult(refundId, refundedAmount, status);
    }

    public async Task<IReadOnlyList<TransactionMatch>> ReconcileAsync(
        string startDate, string endDate,
        IReadOnlyList<OrderPayment> knownPayments,
        CancellationToken ct)
    {
        var allTransactions = new List<TransactionDetails>();
        int page = 1;
        int? totalPages = null;

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
                    fields: "transaction_info",
                    balanceAffectingRecordsOnly: "Y",
                    pageSize: 100,
                    page: page,
                    ct: ct);
            }
            catch (SdkException<RawError> ex)
            {
                throw new PayPalException($"Transaction search failed: HTTP {(int)ex.Error.StatusCode} — {ex.Error.ReadAsString()}", (int)ex.Error.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                throw new PayPalException("PayPal service unreachable.", ex);
            }
            catch (JsonException ex)
            {
                throw new PayPalException("PayPal returned an unprocessable response.", ex);
            }

            if (response.TransactionDetails != null)
                allTransactions.AddRange(response.TransactionDetails);

            totalPages = response.TotalPages;
            page++;
        }
        while (totalPages.HasValue && page <= totalPages.Value);

        // Build lookup of PayPal IDs we know about
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var orderByAuthId = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        var orderByCaptureId = new Dictionary<string, OrderPayment>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in knownPayments)
        {
            if (p.AuthorizationId != null) { knownIds.Add(p.AuthorizationId); orderByAuthId[p.AuthorizationId] = p; }
            if (p.CaptureId != null) { knownIds.Add(p.CaptureId); orderByCaptureId[p.CaptureId] = p; }
            foreach (var r in p.Refunds) knownIds.Add(r.RefundId);
        }

        var results = new List<TransactionMatch>();
        var seenPayPalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tx in allTransactions)
        {
            var info = tx.TransactionInfo;
            if (info is null) continue;
            var txId = info.TransactionId ?? string.Empty;
            seenPayPalIds.Add(txId);

            int? matchedOrderId = null;
            string? note = null;

            if (orderByAuthId.TryGetValue(txId, out var byAuth))
            {
                matchedOrderId = byAuth.OrderId;
                note = "Matched by authorization ID";
            }
            else if (orderByCaptureId.TryGetValue(txId, out var byCap))
            {
                matchedOrderId = byCap.OrderId;
                note = "Matched by capture ID";
            }
            else
            {
                note = "No matching eShop order found";
            }

            results.Add(new TransactionMatch(
                TransactionId: txId,
                TransactionStatus: info.TransactionStatus,
                Amount: info.TransactionAmount?.Value is string v ? decimal.Parse(v) : null,
                Currency: info.TransactionAmount?.CurrencyCode,
                InitiationDate: info.TransactionInitiationDate,
                MatchedOrderId: matchedOrderId,
                MatchNote: note));
        }

        // Flag eShop payments not found in PayPal's report
        foreach (var p in knownPayments)
        {
            void CheckId(string? id, string label)
            {
                if (id != null && !seenPayPalIds.Contains(id))
                {
                    results.Add(new TransactionMatch(
                        TransactionId: id,
                        TransactionStatus: null,
                        Amount: null,
                        Currency: null,
                        InitiationDate: null,
                        MatchedOrderId: p.OrderId,
                        MatchNote: $"eShop {label} not found in PayPal transaction report"));
                }
            }
            CheckId(p.AuthorizationId, "authorization");
            CheckId(p.CaptureId, "capture");
        }

        return results;
    }

    public async Task<VaultResult> VaultCardAsync(string buyerId, CardDetails card, CancellationToken ct)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        PaymentTokenResponse tokenResponse;
        try
        {
            tokenResponse = await _client.Vault.CreatePaymentToken(
                payPalRequestId: idempotencyKey,
                body: new PaymentTokenRequest
                {
                    PaymentSource = new PaymentTokenRequestPaymentSource
                    {
                        Card = new PaymentTokenRequestCard
                        {
                            Name = string.IsNullOrEmpty(card.Name) ? null : card.Name,
                            Number = card.Number,
                            Expiry = card.Expiry,
                            SecurityCode = card.SecurityCode,
                            BillingAddress = card.CountryCode is null ? null : new PayPalServerSdk.Models.Address
                            {
                                CountryCode = card.CountryCode,
                                AddressLine1 = card.AddressLine1,
                                AdminArea2 = card.City,
                                AdminArea1 = card.State,
                                PostalCode = card.PostalCode
                            }
                        }
                    },
                    Customer = null
                },
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? body) && body is not null)
            {
                throw new PayPalException($"Failed to save card: {body.Message} ({body.Name})", 422);
            }
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
                throw new PayPalException($"Failed to save card: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            throw new PayPalException("Failed to save card.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }

        var vaultId = tokenResponse.Id ?? throw new PayPalException("PayPal did not return a vault token ID.");
        var payPalCustomerId = tokenResponse.Customer?.Id;
        var cardEntity = tokenResponse.PaymentSource?.Card;

        return new VaultResult(
            VaultId: vaultId,
            PayPalCustomerId: payPalCustomerId,
            Last4: cardEntity?.LastDigits,
            Brand: cardEntity?.Brand?.Value,
            Expiry: cardEntity?.Expiry);
    }

    public async Task DeleteVaultTokenAsync(string vaultId, CancellationToken ct)
    {
        try
        {
            await _client.Vault.DeletePaymentToken(id: vaultId, ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            if (ex.Error.TryGetError1(out Error1? body) && body is not null)
            {
                if (body.Name?.Contains("NOT_FOUND") == true)
                    return; // already deleted — idempotent
                throw new PayPalException($"Failed to delete vault token: {body.Message}", 422);
            }
            if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
            {
                if (raw.StatusCode == HttpStatusCode.NotFound)
                    return; // already gone
                throw new PayPalException($"Failed to delete vault token: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
            }
            throw new PayPalException("Failed to delete vault token.", 502);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PayPalException("PayPal service unreachable.", ex);
        }
        catch (JsonException ex)
        {
            throw new PayPalException("PayPal returned an unprocessable response.", ex);
        }
    }

    private static PayPalException MapCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body is not null)
            return new PayPalException($"PayPal order creation failed: {body.Message} ({body.Name})", 422);
        if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
            return new PayPalException($"PayPal order creation failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
        return new PayPalException("PayPal order creation failed.", 502);
    }

    private static PayPalException MapAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out Error? body) && body is not null)
            return new PayPalException($"PayPal authorization failed: {body.Message} ({body.Name})", 422);
        if (ex.Error.TryGetRawError(out RawError? raw) && raw is not null)
            return new PayPalException($"PayPal authorization failed: HTTP {(int)raw.StatusCode}", (int)raw.StatusCode);
        return new PayPalException("PayPal authorization failed.", 502);
    }
}
