using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Specification;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using PayPalServerSdk;
using PayPalServerSdk.Models;
using PayPalServerSdk.Models.Enums;
using PayPalServerSdk.Core.Exceptions;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Errors;
using System.Net.Http;
using EShopOrder = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Order;
using EShopAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using EShopOrderItem = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.OrderItem;
using EShopCatalogItemOrdered = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.CatalogItemOrdered;
using EShopOrderStatus = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.OrderStatus;
using PayPalOrder = PayPalServerSdk.Models.Order;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class PayPalPaymentService : IPaymentService
{
    private readonly PayPalServerSdkClient _paypal;
    private readonly IRepository<EShopOrder> _orderRepo;
    private readonly IRepository<Payment> _paymentRepo;
    private readonly IRepository<OrderRefund> _refundRepo;
    private readonly IRepository<SavedCard> _cardRepo;
    private readonly IReadRepository<CatalogItem> _catalogRepo;
    private readonly string _currency;

    public PayPalPaymentService(
        PayPalServerSdkClient paypal,
        IRepository<EShopOrder> orderRepo,
        IRepository<Payment> paymentRepo,
        IRepository<OrderRefund> refundRepo,
        IRepository<SavedCard> cardRepo,
        IReadRepository<CatalogItem> catalogRepo,
        string currency)
    {
        _paypal = paypal;
        _orderRepo = orderRepo;
        _paymentRepo = paymentRepo;
        _refundRepo = refundRepo;
        _cardRepo = cardRepo;
        _catalogRepo = catalogRepo;
        _currency = currency;
    }

    public async Task<EShopOrder> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemInput> items, CancellationToken ct = default)
    {
        var catalogIds = items.Select(i => i.CatalogItemId).ToList();
        var catalogSpec = new CatalogItemsByIdsSpec(catalogIds);
        var catalogItems = await _catalogRepo.ListAsync(catalogSpec, ct);

        var orderItems = items.Select(input =>
        {
            var cat = catalogItems.FirstOrDefault(c => c.Id == input.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {input.CatalogItemId} not found.", 404);
            return new EShopOrderItem(new EShopCatalogItemOrdered(cat.Id, cat.Name!, cat.PictureUri ?? ""), cat.Price, input.Quantity);
        }).ToList();

        var address = new EShopAddress("1 Main St", "AnyCity", "CA", "US", "90210");
        var order = new EShopOrder(buyerId, address, orderItems);
        await _orderRepo.AddAsync(order, ct);
        return order;
    }

    public async Task<IReadOnlyList<(EShopOrder Order, Payment? Payment)>> GetShopperOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepo.ListAsync(new CustomerOrdersSpecification(buyerId), ct);
        if (!orders.Any())
            return Array.Empty<(EShopOrder, Payment?)>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _paymentRepo.ListAsync(new PaymentsByOrderIdsSpec(orderIds), ct);
        var paymentMap = payments.ToDictionary(p => p.EShopOrderId);

        return orders.Select(o => (o, paymentMap.TryGetValue(o.Id, out var p) ? p : (Payment?)null)).ToList();
    }

    public async Task<Payment> AuthorizePaymentAsync(int orderId, string buyerId, PaymentInput input, CancellationToken ct = default)
    {
        var order = await GetOrderForBuyerAsync(orderId, buyerId, ct);

        var existing = await _paymentRepo.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (existing != null)
            return existing;

        var total = order.OrderItems.Sum(i => i.UnitPrice * i.Units);
        var createKey = Guid.NewGuid().ToString("N");
        var authorizeKey = Guid.NewGuid().ToString("N");

        PayPalOrder ppOrder;
        try
        {
            // Records use required-init object initializer syntax — no constructor-arg named params.
            // Source: checkout-sample-sdk/Models/AmountWithBreakdown.cs, PurchaseUnitRequest.cs, OrderRequest.cs
            var purchaseUnit = new PurchaseUnitRequest
            {
                Amount = new AmountWithBreakdown
                {
                    CurrencyCode = _currency,
                    Value = total.ToString("F2")
                }
            };

            var createBody = new OrderRequest
            {
                Intent = CheckoutPaymentIntent.Authorize,
                PurchaseUnits = new List<PurchaseUnitRequest> { purchaseUnit }
            };

            // SDK methods have no Async suffix (they return Task<T> and are awaitable).
            // 5 nullable header params have no default — must pass explicitly.
            // Source: checkout-sample-sdk/Api/Orders.cs — CreateOrder method.
            ppOrder = await _paypal.Orders.CreateOrder(
                payPalMockResponse: null,
                payPalRequestId: createKey,
                payPalPartnerAttributionId: null,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: createBody,
                ct: ct);
        }
        catch (SdkException<CreateOrderError> ex)
        {
            HandleCreateOrderError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during order creation.", 503, ex);
        }

        if (ppOrder.Id == null)
            throw new PaymentException("PayPal returned an order with no ID.", 502);

        var payment = new Payment(orderId, ppOrder.Id, createKey, authorizeKey, DateTime.UtcNow);

        // Authorize the order
        try
        {
            // Build authorize body with card details — required for headless direct-card authorization.
            // For saved card use VaultId; for raw card use Number/Expiry/SecurityCode.
            // prefer: "return=representation" is required to receive PurchaseUnits.Payments.Authorizations in response.
            // Source: paypal-plan.md Step 5; checkout-sample-sdk/Api/Orders.cs — AuthorizeOrder method.
            OrderAuthorizeRequest? authBody = null;
            if (!string.IsNullOrEmpty(input.SavedCardId))
            {
                authBody = new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = new CardRequest { VaultId = input.SavedCardId }
                    }
                };
            }
            else if (!string.IsNullOrEmpty(input.CardNumber))
            {
                authBody = new OrderAuthorizeRequest
                {
                    PaymentSource = new OrderAuthorizeRequestPaymentSource
                    {
                        Card = new CardRequest
                        {
                            Number = input.CardNumber,
                            Expiry = input.CardExpiry,
                            SecurityCode = input.CardCvv,
                            BillingAddress = new Address { CountryCode = input.BillingCountryCode ?? "US" }
                        }
                    }
                };
            }

            var authResponse = await _paypal.Orders.AuthorizeOrder(
                id: ppOrder.Id,
                payPalMockResponse: null,
                payPalRequestId: authorizeKey,
                payPalClientMetadataId: null,
                payPalAuthAssertion: null,
                body: authBody,
                prefer: "return=representation",
                ct: ct);

            var auth = authResponse?.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault();
            if (auth?.Id == null)
                throw new PaymentException("PayPal authorization returned no authorization ID.", 502);

            // ExpirationTime is string? — use null-coalescing directly.
            // Source: map/models/records-2-Pa-Ve.md — AuthorizationWithAdditionalData.ExpirationTime: string?
            string? expiryTimeStr = auth.ExpirationTime ?? DateTime.UtcNow.AddDays(29).ToString("O");

            var authStatus = auth.Status?.Value ?? "CREATED";
            payment.SetAuthorization(auth.Id, authStatus, expiryTimeStr);
        }
        catch (SdkException<AuthorizeOrderError> ex)
        {
            HandleAuthorizeOrderError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during authorization.", 503, ex);
        }

        order.SetStatus(EShopOrderStatus.PaymentAuthorized);
        await _paymentRepo.AddAsync(payment, ct);
        await _orderRepo.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, ct);

        if (payment.CaptureId != null)
            return payment;

        if (payment.AuthorizationId == null)
            throw new PaymentException("Order has no PayPal authorization to capture.", 422);

        if (payment.AuthorizationExpiryTime != null &&
            DateTime.TryParse(payment.AuthorizationExpiryTime, out var expiryDt) &&
            expiryDt <= DateTime.UtcNow.AddDays(1))
            throw new PaymentException("PayPal authorization has expired or expires too soon. Re-authorize first.", 422);

        var captureKey = payment.CaptureIdempotencyKey ?? Guid.NewGuid().ToString("N");

        try
        {
            // 4 nullable params after authorizationId have no default — must pass explicitly.
            // Source: checkout-sample-sdk/Api/Payments.cs — CaptureAuthorizedPayment method.
            var captureResponse = await _paypal.Payments.CaptureAuthorizedPayment(
                authorizationId: payment.AuthorizationId,
                payPalMockResponse: null,
                payPalRequestId: captureKey,
                payPalAuthAssertion: null,
                body: null,
                prefer: "return=representation",
                ct: ct);

            var captureId = captureResponse?.Id ?? throw new PaymentException("PayPal capture returned no ID.", 502);
            var captureStatus = captureResponse.Status?.Value ?? "COMPLETED";
            var capturedAmount = captureResponse.Amount?.Value ?? "0.00";
            var capturedCurrency = captureResponse.Amount?.CurrencyCode ?? _currency;
            var feeAmount = captureResponse.SellerReceivableBreakdown?.PaypalFee?.Value ?? "0.00";
            var feeCurrency = captureResponse.SellerReceivableBreakdown?.PaypalFee?.CurrencyCode ?? _currency;
            var netAmount = captureResponse.SellerReceivableBreakdown?.NetAmount?.Value ?? capturedAmount;
            var netCurrency = captureResponse.SellerReceivableBreakdown?.NetAmount?.CurrencyCode ?? capturedCurrency;

            payment.SetCapture(captureId, captureStatus, captureKey,
                capturedAmount, capturedCurrency,
                feeAmount, feeCurrency, netAmount, netCurrency);
        }
        catch (SdkException<CaptureAuthorizedPaymentError> ex)
        {
            HandleCaptureError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during capture.", 503, ex);
        }

        var order = await _orderRepo.GetByIdAsync(orderId, ct)
            ?? throw new PaymentException($"Order {orderId} not found.", 404);
        order.SetStatus(EShopOrderStatus.Fulfilled);

        await _paymentRepo.UpdateAsync(payment, ct);
        await _orderRepo.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentOrThrowAsync(orderId, ct);

        if (payment.VoidedAt.HasValue)
            return payment;

        if (payment.CaptureId != null)
            throw new PaymentException("Cannot cancel an already-captured order. Use refund instead.", 422);

        if (payment.AuthorizationId == null)
            throw new PaymentException("Order has no authorization to void.", 422);

        var voidKey = Guid.NewGuid().ToString("N");
        try
        {
            // Operation name is VoidPayment (not VoidAuthorizedPayment).
            // payPalMockResponse and payPalAuthAssertion have no default — must pass explicitly.
            // VoidPayment returns 204 No Content; the SDK attempts to deserialize the empty body
            // as PaymentAuthorization and throws JsonException — caught below; the void succeeded.
            // Source: checkout-sample-sdk/Api/Payments.cs — VoidPayment method.
            await _paypal.Payments.VoidPayment(
                authorizationId: payment.AuthorizationId,
                payPalMockResponse: null,
                payPalAuthAssertion: null,
                payPalRequestId: voidKey,
                ct: ct);
        }
        catch (System.Text.Json.JsonException)
        {
            // PayPal returns 204 No Content on success; SDK tries to parse the empty body
            // as PaymentAuthorization, which throws. The void itself succeeded.
        }
        catch (SdkException<VoidPaymentError> ex)
        {
            HandleVoidError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during void.", 503, ex);
        }

        payment.SetVoided();

        var order = await _orderRepo.GetByIdAsync(orderId, ct)
            ?? throw new PaymentException($"Order {orderId} not found.", 404);
        order.SetStatus(EShopOrderStatus.Cancelled);

        await _paymentRepo.UpdateAsync(payment, ct);
        await _orderRepo.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<(OrderRefund Refund, string RefundId)> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        var payment = await _paymentRepo.FirstOrDefaultAsync(new PaymentWithRefundsByOrderIdSpec(orderId), ct)
            ?? throw new PaymentException($"No payment found for order {orderId}.", 404);

        if (payment.CaptureId == null)
            throw new PaymentException("Order has not been captured. Cannot refund.", 422);

        var existingRefund = payment.Refunds.FirstOrDefault(r => r.CallerIdempotencyKey == idempotencyKey);
        if (existingRefund != null)
            return (existingRefund, existingRefund.PayPalRefundId);

        RefundRequest? refundBody = null;
        if (amount.HasValue)
        {
            // Money and RefundRequest use required-init object initializer syntax.
            // Source: checkout-sample-sdk/Models/Money.cs, RefundRequest.cs
            refundBody = new RefundRequest
            {
                Amount = new Money
                {
                    CurrencyCode = _currency,
                    Value = amount.Value.ToString("F2")
                }
            };
        }

        Refund refundResponse;
        try
        {
            // 4 nullable params after captureId have no default — must pass explicitly.
            // Source: checkout-sample-sdk/Api/Payments.cs — RefundCapturedPayment method.
            refundResponse = await _paypal.Payments.RefundCapturedPayment(
                captureId: payment.CaptureId,
                payPalMockResponse: null,
                payPalRequestId: idempotencyKey,
                payPalAuthAssertion: null,
                body: refundBody,
                prefer: "return=representation",
                ct: ct);
        }
        catch (SdkException<RefundCapturedPaymentError> ex)
        {
            HandleRefundError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during refund.", 503, ex);
        }

        var refundId = refundResponse.Id ?? throw new PaymentException("PayPal refund returned no ID.", 502);
        var refundStatus = refundResponse.Status?.Value ?? "COMPLETED";
        var refundAmount = refundResponse.Amount?.Value ?? amount?.ToString("F2") ?? "0.00";
        var refundCurrency = refundResponse.Amount?.CurrencyCode ?? _currency;

        var orderRefund = new OrderRefund(payment.Id, refundId, idempotencyKey, refundStatus, refundAmount, refundCurrency);
        await _refundRepo.AddAsync(orderRefund, ct);

        var order = await _orderRepo.GetByIdAsync(orderId, ct);
        if (order != null)
        {
            var totalRefunded = payment.TotalRefundedAmount() + (amount ?? decimal.Parse(refundAmount));
            var capturedAmount = decimal.TryParse(payment.CapturedAmountValue, out var cap) ? cap : 0m;
            order.SetStatus(totalRefunded >= capturedAmount ? EShopOrderStatus.Refunded : EShopOrderStatus.PartiallyRefunded);
            await _orderRepo.UpdateAsync(order, ct);
        }

        return (orderRefund, refundId);
    }

    public async Task<IReadOnlyList<ReconciliationEntry>> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var payments = await _paymentRepo.ListAsync(new AllPaymentsSpec(), ct);
        var paymentsByPpOrderId = payments.ToDictionary(p => p.PayPalOrderId);

        var entries = new List<ReconciliationEntry>();

        try
        {
            var startDate = from.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var endDate = to.ToString("yyyy-MM-ddTHH:mm:ssZ");

            // SearchTransactions returns single SearchResponse — manual pagination required.
            // pageSize and page are int?. 8 nullable params have no default — must pass explicitly.
            // TransactionStatus is string? — no .Value accessor.
            // Source: checkout-sample-sdk/Api/TransactionSearch.cs; map/models/records-2-Pa-Ve.md.
            int currentPage = 1;
            int totalPages = 1;
            while (currentPage <= totalPages)
            {
                var pageResponse = await _paypal.TransactionSearch.SearchTransactions(
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
                    balanceAffectingRecordsOnly: null,
                    pageSize: 100,
                    page: currentPage,
                    ct: ct);

                if (pageResponse == null) break;
                totalPages = pageResponse.TotalPages ?? 1;

                foreach (var txn in pageResponse.TransactionDetails ?? [])
                {
                    var txnInfo = txn.TransactionInfo;
                    if (txnInfo == null) continue;

                    Payment? matchedPayment = null;

                    if (txnInfo.TransactionId != null && paymentsByPpOrderId.TryGetValue(txnInfo.TransactionId, out var byId))
                        matchedPayment = byId;

                    if (matchedPayment == null && txnInfo.InvoiceId != null)
                        matchedPayment = payments.FirstOrDefault(p => p.CreateIdempotencyKey == txnInfo.InvoiceId);

                    entries.Add(new ReconciliationEntry
                    {
                        PayPalTransactionId = txnInfo.TransactionId,
                        PayPalAmount = txnInfo.TransactionAmount?.Value,
                        PayPalCurrency = txnInfo.TransactionAmount?.CurrencyCode,
                        PayPalStatus = txnInfo.TransactionStatus,
                        PayPalDate = txnInfo.TransactionInitiationDate,
                        EShopOrderId = matchedPayment?.EShopOrderId,
                        MatchStatus = matchedPayment != null ? "matched" : "unmatched"
                    });
                }

                currentPage++;
            }
        }
        catch (SdkException<RawError> ex)
        {
            throw new PaymentException($"PayPal transaction list failed: {ex.Error.ReadAsString()}", (int)ex.Error.StatusCode, ex);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable during reconciliation.", 503, ex);
        }

        return entries;
    }

    public async Task<SavedCard> SaveCardAsync(string shopperId, SaveCardInput cardInput, CancellationToken ct = default)
    {
        var customerId = $"eshop_{shopperId.Replace("@", "_at_").Replace(".", "_")}";

        PaymentTokenResponse tokenResponse;
        try
        {
            // PaymentTokenRequestPaymentSource.Card is PaymentTokenRequestCard (not VaultedDigitalWalletBase).
            // Records use object initializer syntax.
            // Source: checkout-sample-sdk/Models/PaymentTokenRequestCard.cs, PaymentTokenRequestPaymentSource.cs.
            var source = new PaymentTokenRequestPaymentSource
            {
                Card = new PaymentTokenRequestCard
                {
                    Name = cardInput.CardHolderName,
                    Number = cardInput.CardNumber,
                    Expiry = cardInput.CardExpiry
                }
            };

            // Customer has only Id and MerchantCustomerId fields.
            // PaymentSource is required; Customer is optional.
            // Source: checkout-sample-sdk/Models/Customer.cs, PaymentTokenRequest.cs.
            var tokenRequest = new PaymentTokenRequest
            {
                Customer = new Customer { MerchantCustomerId = customerId },
                PaymentSource = source
            };

            // payPalRequestId has no default — must pass explicitly.
            // Source: checkout-sample-sdk/Api/Vault.cs — CreatePaymentToken method.
            tokenResponse = await _paypal.Vault.CreatePaymentToken(
                payPalRequestId: Guid.NewGuid().ToString("N"),
                body: tokenRequest,
                ct: ct);
        }
        catch (SdkException<CreatePaymentTokenError> ex)
        {
            HandleCreateTokenError(ex);
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable while saving card.", 503, ex);
        }

        var tokenId = tokenResponse.Id ?? throw new PaymentException("PayPal returned no payment token ID.", 502);
        var ppCustomerId = tokenResponse.Customer?.Id ?? "";
        var card = tokenResponse.PaymentSource?.Card;

        var savedCard = new SavedCard(
            shopperId: shopperId,
            payPalPaymentTokenId: tokenId,
            payPalCustomerId: ppCustomerId,
            merchantCustomerId: customerId,
            lastFourDigits: card?.LastDigits,
            cardBrand: card?.Brand?.Value,
            cardExpiry: card?.Expiry,
            cardHolderName: card?.Name);

        await _cardRepo.AddAsync(savedCard, ct);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string shopperId, CancellationToken ct = default)
    {
        return await _cardRepo.ListAsync(new SavedCardsByShopperSpec(shopperId), ct);
    }

    public async Task DeleteSavedCardAsync(string shopperId, string paymentMethodId, CancellationToken ct = default)
    {
        var card = await _cardRepo.FirstOrDefaultAsync(new SavedCardByTokenIdSpec(paymentMethodId), ct)
            ?? throw new PaymentException($"Saved card {paymentMethodId} not found.", 404);

        if (card.ShopperId != shopperId)
            throw new PaymentException("Saved card does not belong to this shopper.", 403);

        try
        {
            // DeletePaymentToken is Case A — catch SdkException<DeletePaymentTokenError>.
            // Source: checkout-sample-sdk/Api/Vault.cs — DeletePaymentToken method.
            await _paypal.Vault.DeletePaymentToken(
                id: paymentMethodId,
                ct: ct);
        }
        catch (SdkException<DeletePaymentTokenError> ex)
        {
            // Vault errors use TryGetError1(out Error1) — distinct from TryGetError(out Error) on non-vault ops.
            // 404 arrives via TryGetRawError (fallback; not in the 400/403/500 typed list).
            // Source: checkout-sample-sdk/Errors/DeletePaymentTokenError.cs line 25.
            if (ex.Error.TryGetError1(out var err))
                throw new PaymentException($"PayPal vault delete failed: {err?.Message ?? "unknown"}", 422, ex);
            if (ex.Error.TryGetRawError(out var raw) && (int)raw.StatusCode != 404)
                throw new PaymentException($"PayPal vault delete failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
            // 404 via TryGetRawError: already deleted on PayPal side — allow local soft-delete to proceed
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new PaymentException("PayPal unreachable while deleting card.", 503, ex);
        }

        card.SoftDelete();
        await _cardRepo.UpdateAsync(card, ct);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<EShopOrder> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepo.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new PaymentException($"Order {orderId} not found.", 404);
        if (order.BuyerId != buyerId)
            throw new PaymentException("Order does not belong to this buyer.", 403);
        return order;
    }

    private async Task<Payment> GetPaymentOrThrowAsync(int orderId, CancellationToken ct)
    {
        return await _paymentRepo.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentException($"No payment found for order {orderId}.", 404);
    }

    private static string FormatPayPalError(Error? err) =>
        err == null ? "unknown"
        : err.Details?.Count > 0
            ? $"{err.Name} - {err.Message} | {string.Join("; ", err.Details.Select(d => $"{d.Issue}: {d.Description}"))}"
            : $"{err.Name} - {err.Message}";

    private static void HandleCreateOrderError(SdkException<CreateOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            throw new PaymentException($"PayPal create order failed: {FormatPayPalError(err)}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal create order failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }

    private static void HandleAuthorizeOrderError(SdkException<AuthorizeOrderError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            throw new PaymentException($"PayPal authorization failed: {FormatPayPalError(err)}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal authorization failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }

    private static void HandleCaptureError(SdkException<CaptureAuthorizedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            throw new PaymentException($"PayPal capture failed: {err?.Message ?? "unknown"}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal capture failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }

    private static void HandleVoidError(SdkException<VoidPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            throw new PaymentException($"PayPal void failed: {err?.Message ?? "unknown"}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal void failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }

    private static void HandleRefundError(SdkException<RefundCapturedPaymentError> ex)
    {
        if (ex.Error.TryGetError(out var err))
            throw new PaymentException($"PayPal refund failed: {err?.Message ?? "unknown"}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal refund failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }

    private static void HandleCreateTokenError(SdkException<CreatePaymentTokenError> ex)
    {
        // Vault errors use TryGetError1(out Error1) — distinct from TryGetError(out Error) on non-vault ops.
        // Source: checkout-sample-sdk/Errors/CreatePaymentTokenError.cs line 25.
        if (ex.Error.TryGetError1(out var err))
            throw new PaymentException($"PayPal vault token creation failed: {err?.Message ?? "unknown"}", 422, ex);
        else if (ex.Error.TryGetRawError(out var raw))
            throw new PaymentException($"PayPal vault token creation failed: {raw.ReadAsString()}", (int)raw.StatusCode, ex);
    }
}
