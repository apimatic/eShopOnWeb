using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;

namespace Microsoft.eShopWeb.Infrastructure.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepo;
    private readonly IReadRepository<CatalogItem> _catalogRepo;
    private readonly IReadRepository<SavedCard> _savedCardRepo;
    private readonly PayPalClient _paypal;
    private readonly PayPalSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepo,
        IReadRepository<CatalogItem> catalogRepo,
        IReadRepository<SavedCard> savedCardRepo,
        PayPalClient paypal,
        PayPalSettings settings)
    {
        _orderRepo = orderRepo;
        _catalogRepo = catalogRepo;
        _savedCardRepo = savedCardRepo;
        _paypal = paypal;
        _settings = settings;
    }

    public async Task<int> CreateOrderAsync(string buyerId, List<OrderItemRequest> items)
    {
        var ids = items.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogRepo.ListAsync(new CatalogItemsSpecification(ids));

        var orderItems = new List<OrderItem>();
        foreach (var req in items)
        {
            var catalog = catalogItems.FirstOrDefault(c => c.Id == req.CatalogItemId)
                ?? throw new InvalidOperationException($"Catalog item {req.CatalogItemId} not found.");
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                catalog.Price,
                req.Quantity));
        }

        var order = new Order(buyerId, new Address("", "", "", "", ""), orderItems);
        await _orderRepo.AddAsync(order);
        return order.Id;
    }

    public async Task<PayOrderResult> PayOrderWithCardAsync(int orderId, string buyerId, PayOrderWithCardRequest card)
    {
        var order = await GetBuyerOrderAsync(orderId, buyerId);
        return await AuthorizeAsync(order, async (payPalOrderId, idempotencyKey) =>
            await _paypal.AuthorizeOrderWithCardAsync(
                payPalOrderId,
                card.CardNumber,
                $"{card.CardExpiryYear:D4}-{card.CardExpiryMonth:D2}",
                card.Cvv,
                card.CardholderName,
                card.BillingCountryCode,
                card.BillingPostalCode,
                idempotencyKey + "-auth"));
    }

    public async Task<PayOrderResult> PayOrderWithSavedCardAsync(int orderId, string buyerId, int savedCardId)
    {
        var order = await GetBuyerOrderAsync(orderId, buyerId);
        var savedCard = await _savedCardRepo.GetByIdAsync(savedCardId)
            ?? throw new InvalidOperationException("Saved card not found.");
        if (savedCard.BuyerId != buyerId)
            throw new UnauthorizedAccessException("Card does not belong to this buyer.");

        return await AuthorizeAsync(order, async (payPalOrderId, idempotencyKey) =>
            await _paypal.AuthorizeOrderWithTokenAsync(
                payPalOrderId,
                savedCard.VaultTokenId,
                idempotencyKey + "-auth"));
    }

    private async Task<PayOrderResult> AuthorizeAsync(
        Order order,
        Func<string, string, Task<PayPal.Models.AuthorizeOrderResponse>> doAuthorize)
    {
        // Idempotency: if already authorized, return existing
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment != null)
        {
            return new PayOrderResult(
                order.Payment.AuthorizationId!,
                order.Payment.AuthorizationStatus!,
                order.Payment.AuthorizationExpiry!.Value,
                order.Payment.Currency,
                order.Total());
        }

        if (order.Status != OrderStatus.PendingPayment)
            throw new InvalidOperationException($"Order is in status {order.Status} and cannot be paid.");

        // Use a unique suffix per attempt so PayPal idempotency keys don't collide across server restarts.
        // Server-side state machine is the primary double-submit guard; the PayPal key is secondary.
        var attemptKey = Guid.NewGuid().ToString("N")[..16];
        var idempotencyKey = $"eshop-pay-{order.Id}-{attemptKey}";
        var total = order.Total();
        var currency = _settings.Currency;

        // Create PayPal order
        var ppOrder = await _paypal.CreateOrderAsync(currency, total, order.Id, idempotencyKey + "-create");

        // Authorize
        var authResponse = await doAuthorize(ppOrder.Id, idempotencyKey);

        if (authResponse.Status == "PAYER_ACTION_REQUIRED")
            throw new InvalidOperationException(
                "PayPal requires payer browser approval (3DS challenge). " +
                "STOP: this cannot be completed headlessly.");

        var auth = authResponse.PurchaseUnits?.FirstOrDefault()?.Payments?.Authorizations?.FirstOrDefault()
            ?? throw new InvalidOperationException($"No authorization in PayPal response. Status: {authResponse.Status}");

        if (auth.Status == "DENIED")
            throw new InvalidOperationException($"PayPal authorization denied for order {order.Id}.");

        var expiry = auth.ExpirationTime != null
            ? DateTimeOffset.Parse(auth.ExpirationTime, null, DateTimeStyles.RoundtripKind)
            : DateTimeOffset.UtcNow.AddDays(3);

        var payment = new OrderPayment(order.Id, currency, ppOrder.Id);
        payment.SetAuthorization(auth.Id, auth.Status, expiry);

        // SetPaymentAuthorized links payment to order; UpdateAsync saves both
        order.SetPaymentAuthorized(payment);
        await _orderRepo.UpdateAsync(order);

        return new PayOrderResult(auth.Id, auth.Status, expiry, currency, total);
    }

    public async Task<FulfilOrderResult> FulfilOrderAsync(int orderId)
    {
        var order = await GetOrderWithPaymentAsync(orderId);
        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOperationException($"Order {orderId} is not in PaymentAuthorized state.");

        var payment = order.Payment!;
        var authId = payment.AuthorizationId!;
        // Unique per attempt; server-side state machine (status=PaymentAuthorized check) is the primary guard.
        var idempotencyKey = $"eshop-capture-{orderId}-{Guid.NewGuid():N}"[..36];

        // Check if authorization is expired; reauthorize if needed
        if (payment.AuthorizationExpiry.HasValue && DateTimeOffset.UtcNow >= payment.AuthorizationExpiry.Value)
        {
            try
            {
                var reauth = await _paypal.ReauthorizeAsync(authId, payment.Currency, order.Total());
                payment.UpdateAuthorizationStatus(reauth.Status);
                if (reauth.ExpirationTime != null)
                    payment.UpdateAuthorizationExpiry(
                        DateTimeOffset.Parse(reauth.ExpirationTime, null, DateTimeStyles.RoundtripKind));
            }
            catch (PayPalException ex)
            {
                throw new InvalidOperationException(
                    $"Authorization for order {orderId} has expired and could not be renewed. " +
                    $"PayPal response: {ex.ResponseBody}. " +
                    "Action required: cancel this order and ask the customer to re-submit payment.");
            }
        }

        var capture = await _paypal.CaptureAuthorizationAsync(authId, idempotencyKey);

        if (capture.Status == "DECLINED" || capture.Status == "FAILED")
            throw new InvalidOperationException($"PayPal capture {capture.Status} for order {orderId}.");

        var gross = ParseAmount(capture.SellerReceivableBreakdown?.GrossAmount?.Value) ?? ParseAmount(capture.Amount?.Value) ?? order.Total();
        var fee = ParseAmount(capture.SellerReceivableBreakdown?.PayPalFee?.Value) ?? 0m;
        var net = ParseAmount(capture.SellerReceivableBreakdown?.NetAmount?.Value) ?? (gross - fee);

        payment.SetCapture(capture.Id, gross, fee, net);
        order.SetFulfilled();
        await _orderRepo.UpdateAsync(order);

        return new FulfilOrderResult(capture.Id, capture.Status, gross, fee, net);
    }

    public async Task CancelOrderAsync(int orderId)
    {
        var order = await GetOrderWithPaymentAsync(orderId);
        if (order.Status != OrderStatus.PaymentAuthorized)
            throw new InvalidOperationException($"Order {orderId} cannot be cancelled in status {order.Status}.");

        await _paypal.VoidAuthorizationAsync(order.Payment!.AuthorizationId!);

        order.SetCancelled();
        await _orderRepo.UpdateAsync(order);
    }

    public async Task<RefundResult> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey)
    {
        var order = await GetOrderWithPaymentAsync(orderId);
        var payment = order.Payment;

        // Idempotency check first: a repeated key returns the same result regardless of current state
        if (payment != null)
        {
            var existing = payment.FindRefundByKey(idempotencyKey);
            if (existing != null)
                return new RefundResult(existing.RefundId, existing.Status, existing.Amount, AlreadyExisted: true);
        }

        if (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded)
            throw new InvalidOperationException($"Order {orderId} cannot be refunded in status {order.Status}.");

        var capturedAmount = payment.CapturedAmount ?? 0m;
        var alreadyRefunded = payment.TotalRefunded();
        var available = capturedAmount - alreadyRefunded;

        if (available <= 0)
            throw new InvalidOperationException("No refundable amount remaining.");

        if (amount.HasValue && amount.Value > available)
            throw new InvalidOperationException(
                $"Requested refund {amount.Value:F2} exceeds available amount {available:F2}.");

        // Scope PayPal idempotency key to the specific capture so it doesn't collide across capture IDs.
        var paypalRefundKey = $"{idempotencyKey}-{payment.CaptureId![..Math.Min(8, payment.CaptureId.Length)]}";
        var refundResponse = await _paypal.RefundCaptureAsync(
            payment.CaptureId!, amount, payment.Currency, paypalRefundKey);

        var refundedAmount = amount ?? ParseAmount(refundResponse.Amount?.Value) ?? available;
        var record = payment.AddRefund(refundResponse.Id, idempotencyKey, refundedAmount, refundResponse.Status);

        var newTotal = payment.TotalRefunded();
        bool fullyRefunded = newTotal >= capturedAmount - 0.01m;
        order.SetRefunded(partial: !fullyRefunded);
        await _orderRepo.UpdateAsync(order);

        return new RefundResult(record.RefundId, record.Status, record.Amount, AlreadyExisted: false);
    }

    public async Task<List<OrderSummary>> GetMyOrdersAsync(string buyerId)
    {
        var orders = await _orderRepo.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId));
        return orders.Select(MapToSummary).ToList();
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to)
    {
        // PayPal transaction search max range is 31 days per call; chunk if range is wider
        var allTransactions = new List<PayPal.Models.TransactionDetail>();
        var chunkStart = from;
        while (chunkStart < to)
        {
            var chunkEnd = chunkStart.AddDays(31) < to ? chunkStart.AddDays(31) : to;
            int page = 1;
            int totalPages = 1;
            while (page <= totalPages)
            {
                var response = await _paypal.SearchTransactionsAsync(chunkStart, chunkEnd, page: page, pageSize: 500);
                if (response.TransactionDetails != null)
                    allTransactions.AddRange(response.TransactionDetails);
                totalPages = response.TotalPages ?? 1;
                page++;
            }
            chunkStart = chunkEnd;
        }

        // Load all orders - reconcile against those in the requested date range
        var allOrders = await _orderRepo.ListAsync(new AllOrdersWithPaymentSpec());
        var ordersInRange = allOrders.Where(o => o.OrderDate >= from && o.OrderDate <= to).ToList();

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        // Build index: authId -> order, captureId -> order
        var authIndex = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        var captureIndex = new Dictionary<string, Order>(StringComparer.OrdinalIgnoreCase);
        foreach (var o in ordersInRange)
        {
            if (o.Payment?.AuthorizationId != null) authIndex[o.Payment.AuthorizationId] = o;
            if (o.Payment?.CaptureId != null) captureIndex[o.Payment.CaptureId] = o;
        }

        foreach (var txn in allTransactions)
        {
            var info = txn.TransactionInfo;
            if (info == null) continue;

            Order? matchedOrder = null;

            if (info.PayPalReferenceId != null)
            {
                authIndex.TryGetValue(info.PayPalReferenceId, out matchedOrder);
                if (matchedOrder == null)
                    captureIndex.TryGetValue(info.PayPalReferenceId, out matchedOrder);
            }

            if (matchedOrder == null && info.TransactionId != null)
            {
                authIndex.TryGetValue(info.TransactionId, out matchedOrder);
                if (matchedOrder == null)
                    captureIndex.TryGetValue(info.TransactionId, out matchedOrder);
            }

            if (matchedOrder != null)
                matchedOrderIds.Add(matchedOrder.Id);

            entries.Add(new ReconciliationEntry(
                TransactionId: info.TransactionId ?? "(unknown)",
                EShopOrderId: matchedOrder?.Id.ToString(),
                PayPalReferenceId: info.PayPalReferenceId,
                EventCode: info.TransactionEventCode,
                Status: info.TransactionStatus,
                Amount: info.TransactionAmount?.Value,
                Currency: info.TransactionAmount?.CurrencyCode,
                Fee: info.FeeAmount?.Value,
                InitiatedDate: info.TransactionInitiationDate,
                MatchStatus: matchedOrder != null ? "matched" : "unmatched-paypal-only"));
        }

        var unmatchedOrders = ordersInRange
            .Where(o => o.Payment != null && !matchedOrderIds.Contains(o.Id))
            .Select(o => o.Id.ToString())
            .ToList();

        return new ReconciliationReport(
            From: from,
            To: to,
            Entries: entries,
            UnmatchedPayPalTransactions: entries
                .Where(e => e.MatchStatus == "unmatched-paypal-only")
                .Select(e => e.TransactionId)
                .ToList(),
            UnmatchedEShopOrderIds: unmatchedOrders);
    }

    private async Task<Order> GetOrderWithPaymentAsync(int orderId)
    {
        var order = await _orderRepo.FirstOrDefaultAsync(new OrderWithPaymentSpec(orderId))
            ?? throw new InvalidOperationException($"Order {orderId} not found.");
        return order;
    }

    private async Task<Order> GetBuyerOrderAsync(int orderId, string buyerId)
    {
        var order = await GetOrderWithPaymentAsync(orderId);
        if (order.BuyerId != buyerId)
            throw new UnauthorizedAccessException($"Order {orderId} does not belong to the current user.");
        return order;
    }

    private static OrderSummary MapToSummary(Order o) => new(
        OrderId: o.Id,
        OrderDate: o.OrderDate,
        Total: o.Total(),
        Status: o.Status.ToString(),
        PayPalOrderId: o.Payment?.PayPalOrderId,
        AuthorizationId: o.Payment?.AuthorizationId,
        CaptureId: o.Payment?.CaptureId,
        CapturedAmount: o.Payment?.CapturedAmount,
        PayPalFee: o.Payment?.PayPalFee,
        NetAmount: o.Payment?.NetAmount,
        Refunds: o.Payment?.Refunds
            .Select(r => new RefundSummary(r.RefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList() ?? new List<RefundSummary>());

    private static decimal? ParseAmount(string? value) =>
        decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
}
