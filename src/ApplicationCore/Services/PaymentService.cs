using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentService> _logger;

    // Per-order in-process gate so a double-click can't authorize, capture or refund twice while the
    // first request is still in flight. Combined with the persisted order/payment status this makes
    // every money operation idempotent in effect.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    public PaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineInput> lines,
        Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Units <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        // Merge duplicate lines for the same catalog item.
        var merged = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLineInput(g.Key, g.Sum(l => l.Units)))
            .ToList();

        var ids = merged.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in merged)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentException($"Catalog item {line.CatalogItemId} does not exist.", 404);

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri ?? "eCatalog-item-default.png");
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Amounts come from catalog prices, never from the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Units));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {0} for {1} awaiting payment (total {2}).",
            order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card,
        int? paymentMethodId, CancellationToken ct = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOwnOrderAsync(orderId, buyerId, ct);

            // Idempotent: already authorized (or beyond) → return current state, never re-hold.
            if (order.Status != OrderStatus.AwaitingPayment)
            {
                if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
                {
                    return order;
                }
                throw new PaymentException(
                    $"Order {orderId} is {order.Status} and can no longer be paid.", 409);
            }

            string? vaultId = null;
            if (paymentMethodId.HasValue)
            {
                var pm = await _paymentMethodRepository.FirstOrDefaultAsync(
                    new PaymentMethodByIdForBuyerSpecification(paymentMethodId.Value, buyerId), ct)
                    ?? throw new PaymentException("Saved card not found.", 404);
                vaultId = pm.VaultId;
                card = null; // never mix a one-off card with a saved card
            }
            else if (card is null)
            {
                throw new PaymentException("Provide card details or a saved paymentMethodId to pay.");
            }

            var amount = order.Total();
            var idempotencyKey = $"{order.PaymentReference}:auth";
            var result = await _payPal.AuthorizeOrderAsync(order.PaymentReference, amount, card,
                vaultId, idempotencyKey, ct);

            var payment = new Payment(_payPal.Currency, amount, result.PayPalOrderId,
                result.AuthorizationId, result.Status, result.ExpiresAt, paymentMethodId);
            order.SetAuthorizedPayment(payment);
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Authorized {0} on order {1} (auth {2}).",
                amount, orderId, result.AuthorizationId);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);

            if (order.Status == OrderStatus.Fulfilled)
            {
                return order; // idempotent: already captured
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            {
                throw new PaymentException(
                    $"Order {orderId} is {order.Status}; only an order with authorized payment can be fulfilled.",
                    409);
            }

            var payment = order.Payment;
            var capture = await CaptureWithRenewalAsync(order, payment, payment.AuthorizedAmount, ct);
            payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount,
                capture.PayPalFee, capture.NetAmount);
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation(
                "Fulfilled order {0}: captured {1}, PayPal fee {2}, net {3} (capture {4}).",
                orderId, capture.Amount, capture.PayPalFee, capture.NetAmount, capture.CaptureId);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Capture the held funds, renewing a stale authorization first rather than failing outright.
    /// If the authorization can no longer be renewed, surface an operator-actionable error.
    /// </summary>
    private async Task<PayPalCaptureResult> CaptureWithRenewalAsync(Order order, Payment payment,
        decimal amount, CancellationToken ct)
    {
        var captureKey = $"{order.PaymentReference}:capture";
        var likelyStale = payment.AuthorizationExpiresAt.HasValue
            && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;

        if (!likelyStale)
        {
            try
            {
                return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, amount, captureKey, ct);
            }
            catch (PayPalApiException ex) when (ex.IndicatesReauthorizationNeeded())
            {
                _logger.LogWarning("Authorization {0} on order {1} is stale; renewing before capture.",
                    payment.AuthorizationId, order.Id);
            }
        }

        PayPalReauthorizationResult reauth;
        try
        {
            reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, amount, ct);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The payment authorization for order {order.Id} has expired and can no longer be renewed " +
                $"(PayPal: {ex.Message}). Ask the shopper to place and pay for the order again.", 409);
        }

        payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        return await _payPal.CaptureAuthorizationAsync(reauth.AuthorizationId, amount, $"{captureKey}:re", ct);
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order; // idempotent
            }
            if (order.Status != OrderStatus.AwaitingPayment && order.Status != OrderStatus.PaymentAuthorized)
            {
                throw new PaymentException(
                    $"Order {orderId} is {order.Status}; only an order awaiting payment or with a held " +
                    "authorization can be cancelled. Use a refund after fulfilment.", 409);
            }

            if (order.Payment is { Status: PaymentStatus.Authorized } heldPayment)
            {
                await _payPal.VoidAuthorizationAsync(heldPayment.AuthorizationId, ct);
                heldPayment.MarkVoided();
                _logger.LogInformation("Voided authorization {0} on cancelled order {1}; funds released.",
                    heldPayment.AuthorizationId, orderId);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, ct);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(Order Order, Refund Refund)> RefundAsync(int orderId, string buyerId,
        decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        if (amount.HasValue && amount.Value <= 0)
        {
            throw new PaymentException("A refund amount, when given, must be greater than zero.", 422);
        }

        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var order = await LoadOwnOrderAsync(orderId, buyerId, ct);
            var payment = order.Payment;
            if (payment is null || payment.CaptureId is null
                || (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded))
            {
                throw new PaymentException(
                    $"Order {orderId} has no captured payment to refund. Only a fulfilled order can be refunded.",
                    409);
            }

            // Idempotent on the caller's key: repeating a refund request never refunds twice.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return (order, existing);
            }

            var remaining = payment.RefundableRemaining();
            if (remaining <= 0)
            {
                throw new PaymentException(
                    $"Order {orderId} has already been fully refunded; nothing remains to refund.", 409);
            }
            if (amount.HasValue && amount.Value > remaining)
            {
                throw new PaymentException(
                    $"A refund of {amount.Value:0.00} exceeds the {remaining:0.00} still refundable on this " +
                    "capture.", 422);
            }

            // Pass an explicit amount for a partial refund, or when a prior refund means a bare
            // "full" refund would exceed what remains; use PayPal's full-refund shape otherwise.
            decimal? apiAmount = amount ?? (payment.TotalRefunded() > 0 ? remaining : (decimal?)null);

            // Namespace the PayPal idempotency key with this order's unique reference so it is globally
            // unique across runs/accounts, while the caller's key still governs "don't refund twice".
            var payPalRequestId = $"{order.PaymentReference}:refund:{idempotencyKey}";
            var result = await _payPal.RefundCaptureAsync(payment.CaptureId, apiAmount, payment.Currency,
                payPalRequestId, ct);
            var refund = new Refund(result.RefundId, result.Amount, result.Currency, result.Status, idempotencyKey);
            payment.AddRefund(refund);
            order.ApplyRefundStatus();
            await _orderRepository.UpdateAsync(order, ct);

            _logger.LogInformation("Refunded {0} on order {1} (refund {2}); status now {3}.",
                result.Amount, orderId, result.RefundId, order.Status);
            return (order, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), ct)
            ?? throw new PaymentException($"Order {orderId} was not found.", 404);
    }

    private async Task<Order> LoadOwnOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), ct);
        // 404 rather than 403 on someone else's order so its existence isn't revealed.
        if (order is null || order.BuyerId != buyerId)
        {
            throw new PaymentException($"Order {orderId} was not found.", 404);
        }
        return order;
    }
}
