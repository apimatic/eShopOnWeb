using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentOrderService : IPaymentOrderService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentOrderService> _logger;

    public PaymentOrderService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        PayPalSettings settings,
        IAppLogger<PaymentOrderService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order must contain at least one item.");
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentValidationException("Every item quantity must be greater than zero.");

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = new Address(
            shipTo?.Street ?? "N/A",
            shipTo?.City ?? "N/A",
            shipTo?.State ?? "N/A",
            shipTo?.Country ?? "US",
            shipTo?.ZipCode ?? "00000");

        var order = new Order(buyerId, address, items);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Placed order {order.Id} for buyer with total {order.Total()} {Currency}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken ct)
    {
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);

        // Idempotent: a repeat once authorized returns the existing hold without charging again.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized && order.AuthorizationId is not null)
            return order;
        if (order.PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.AuthorizationFailed))
            throw new PaymentConflictException(
                $"Order {orderId} cannot be authorized while its payment status is {order.PaymentStatus}.");

        string? vaultId = null;
        if (savedCardId.HasValue)
        {
            var saved = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpec(savedCardId.Value, buyerId), ct)
                ?? throw new PaymentNotFoundException($"Saved card {savedCardId.Value} was not found.");
            vaultId = saved.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentValidationException("Provide card details or a saved card id to pay.");
        }

        var amount = order.Total();
        try
        {
            var result = await _gateway.AuthorizeAsync(orderId, order.PayPalInvoiceId, order.PaymentReference,
                amount, Currency, card, vaultId, ct);
            order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status,
                result.ExpiresAt, Currency);
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }
        catch (PayPalException)
        {
            order.MarkAuthorizationFailed();
            await _orderRepository.UpdateAsync(order, ct);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        // Idempotent: already captured.
        if (order.PaymentStatus == OrderPaymentStatus.Fulfilled && order.CaptureId is not null)
            return order;
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId is null)
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled while its payment status is {order.PaymentStatus}.");

        var amount = order.Total();

        // A stale authorization must be renewed before capture, not fail the fulfilment.
        var buffer = DateTimeOffset.UtcNow.AddMinutes(1);
        var reauthorized = false;
        if (order.AuthorizationExpiresAt is { } expiry && expiry <= buffer)
        {
            await RenewAuthorizationAsync(order, amount, ct);
            reauthorized = true;
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(order.AuthorizationId!, order.PayPalInvoiceId, amount, Currency, ct);
        }
        catch (PayPalException ex) when (!reauthorized && IsExpiredAuthorization(ex))
        {
            // PayPal rejected the capture as expired even though our clock thought it live — renew and retry once.
            await RenewAuthorizationAsync(order, amount, ct);
            capture = await _gateway.CaptureAsync(order.AuthorizationId!, order.PayPalInvoiceId, amount, Currency, ct);
        }

        order.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee,
            capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Fulfilled order {orderId}; captured {capture.CapturedAmount} {Currency}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await GetOrderAsync(orderId, ct);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
            return order; // idempotent
        if (order.PaymentStatus == OrderPaymentStatus.Fulfilled)
            throw new PaymentConflictException(
                $"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.AuthorizationId is null)
            throw new PaymentConflictException(
                $"Order {orderId} cannot be cancelled while its payment status is {order.PaymentStatus}.");

        await _gateway.VoidAsync(order.AuthorizationId, ct);
        order.Cancel();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Cancelled order {orderId}; authorization released.");
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrderAsync(buyerId, orderId, ct);

        if (order.CaptureId is null ||
            order.PaymentStatus is not (OrderPaymentStatus.Fulfilled or OrderPaymentStatus.PartiallyRefunded))
            throw new PaymentConflictException(
                $"Order {orderId} has no captured payment to refund (status {order.PaymentStatus}).");

        // Idempotent under the caller's key: a repeat returns the refund already made under that key.
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return (order, existing);

        var remaining = order.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
            throw new PaymentValidationException("Refund amount must be greater than zero.");
        if (refundAmount > remaining)
            throw new PaymentValidationException(
                $"Refund of {refundAmount} {Currency} exceeds the {remaining} {Currency} still refundable on order {orderId}.");

        var result = await _gateway.RefundAsync(order.CaptureId, order.PayPalInvoiceId, refundAmount, Currency,
            idempotencyKey, ct);
        var refund = new OrderRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Refunded {result.Amount} {Currency} on order {orderId} (refund {result.RefundId}).");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        return orders;
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (to < from)
            throw new PaymentValidationException("'to' must not be earlier than 'from'.");

        var transactions = await _gateway.SearchTransactionsAsync(from, to, ct);
        var orders = await _orderRepository.ListAsync(ct);

        var byInvoice = orders
            .Where(o => o.CaptureId is not null)
            .ToDictionary(o => o.PayPalInvoiceId, o => o, StringComparer.OrdinalIgnoreCase);
        var byCapture = orders
            .Where(o => o.CaptureId is not null)
            .ToDictionary(o => o.CaptureId!, o => o, StringComparer.OrdinalIgnoreCase);

        var matched = new List<ReconciliationMatch>();
        var unmatchedInPayPal = new List<ReconciliationTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var txn in transactions)
        {
            Order? order = null;
            if (txn.InvoiceId is not null)
                byInvoice.TryGetValue(txn.InvoiceId, out order);
            if (order is null && txn.TransactionId is not null)
                byCapture.TryGetValue(txn.TransactionId, out order);

            if (order is not null)
            {
                matched.Add(new ReconciliationMatch(order.Id, order.CaptureId, txn.TransactionId, txn.Amount,
                    order.CapturedAmount, order.PaymentStatus.ToString()));
                matchedOrderIds.Add(order.Id);
            }
            else
            {
                unmatchedInPayPal.Add(txn);
            }
        }

        var unmatchedInEShop = orders
            .Where(o => o.CaptureId is not null && !matchedOrderIds.Contains(o.Id))
            .Select(o => new ReconciliationEShopOrder(o.Id, o.CaptureId, o.CapturedAmount, o.PaymentStatus.ToString()))
            .ToList();

        return new ReconciliationReport(from, to, transactions.Count, matched, unmatchedInPayPal, unmatchedInEShop);
    }

    // ---- helpers ----

    private async Task RenewAuthorizationAsync(Order order, decimal amount, CancellationToken ct)
    {
        try
        {
            var renewal = await _gateway.ReauthorizeAsync(order.AuthorizationId!, amount, Currency, ct);
            order.RenewAuthorization(renewal.AuthorizationId, renewal.Status, renewal.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Renewed authorization for order {order.Id}: {renewal.AuthorizationId}.");
        }
        catch (PayPalException ex)
        {
            throw new PaymentConflictException(
                $"Order {order.Id} cannot be fulfilled: its authorization has expired and can no longer be renewed " +
                $"(PayPal: {ex.Issue ?? ex.Message}). Collect a new payment for this order.");
        }
    }

    private static bool IsExpiredAuthorization(PayPalException ex) =>
        (ex.Issue?.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await GetOrderAsync(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        return order;
    }
}
