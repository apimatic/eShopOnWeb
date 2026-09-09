using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> itemRepository,
        IReadRepository<PaymentMethod> paymentMethodRepository,
        IPayPalPaymentGateway gateway,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one line item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new InvalidPaymentOperationException("Every order line must have a quantity of at least one.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidPaymentOperationException(
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultAddress(), orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for {buyerId} totalling {order.Total()}.");
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, CardDetails? card, int? paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a double-click never authorizes twice.
        if (order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} is already authorized; returning existing hold.");
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be paid from status {order.Status}.");
        }

        string? vaultTokenId = null;
        if (paymentMethodId.HasValue)
        {
            var method = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdForOwnerSpecification(paymentMethodId.Value, buyerId), cancellationToken);
            if (method is null)
            {
                throw new PaymentMethodNotFoundException(paymentMethodId.Value);
            }
            vaultTokenId = method.VaultTokenId;
        }
        else if (card is null)
        {
            throw new InvalidPaymentOperationException(
                "A payment requires either card details or a saved card id.");
        }

        var amount = new Money(order.Total(), _settings.Currency);
        var invoiceId = BuildInvoiceId(orderId);
        // Stable for this order across pay retries (double-click safety) yet unique per order instance.
        var idempotencyKey = $"authorize-{OrderKey(order)}";

        var authorization = await _gateway.AuthorizeAsync(amount, card, vaultTokenId, invoiceId,
            orderId.ToString(), idempotencyKey, cancellationToken);

        var payment = new Payment(authorization.PayPalOrderId, authorization.AuthorizationId,
            authorization.Status, authorization.ExpiresAt, order.Total(), _settings.Currency, invoiceId);
        order.AuthorizePayment(payment);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Authorized order {orderId}: PayPal order {authorization.PayPalOrderId}, " +
            $"authorization {authorization.AuthorizationId}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, buyerId: null, cancellationToken);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // already fulfilled: idempotent
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} cannot be fulfilled from status {order.Status}.");
        }

        var payment = order.Payment;

        // If the authorization has already been captured (e.g. a prior fulfil that saved the capture
        // but not the order status), just complete the order.
        if (payment.Status == PaymentStatus.Captured && payment.CaptureId is not null)
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        }

        var amount = new Money(payment.Amount, payment.Currency);

        // Renew a stale hold rather than failing the fulfilment outright.
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(payment, amount, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, amount, payment.InvoiceId,
                $"capture-{OrderKey(order)}", cancellationToken);
        }
        catch (PayPalApiException ex) when (LooksLikeExpiredAuthorization(ex))
        {
            // The hold lapsed between our check and the capture: renew and try once more.
            _logger.LogWarning($"Capture of order {orderId} failed ({ex.PayPalName}); renewing authorization.");
            await RenewAuthorizationAsync(payment, amount, cancellationToken);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId, amount, payment.InvoiceId,
                $"capture-{OrderKey(order)}-retry", cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount,
            capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.GrossAmount} " +
            $"(fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, buyerId: null, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} is already fulfilled and cannot be cancelled; issue a refund instead.");
        }

        // Release the hold if one exists (no money ever moved).
        if (order.Payment is { Status: PaymentStatus.Authorized } payment)
        {
            await _gateway.VoidAsync(payment.AuthorizationId, cancellationToken);
            payment.MarkVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Cancelled order {orderId}; any held funds released.");
        return order;
    }

    public async Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        if (order.Payment is null
            || (order.Payment.Status != PaymentStatus.Captured
                && order.Payment.Status != PaymentStatus.PartiallyRefunded))
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} has no captured payment to refund.");
        }

        var payment = order.Payment;

        // Idempotent: repeating a refund under the same key must not refund twice.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} under key {idempotencyKey} already exists.");
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        if (remaining <= 0m)
        {
            throw new InvalidPaymentOperationException(
                $"Order {orderId} has been fully refunded; nothing remains to refund.");
        }

        // A null amount means "refund what remains"; an explicit amount must not exceed it, so a
        // partly-refunded order never becomes refundable beyond what was captured.
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new InvalidPaymentOperationException("A refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {refundAmount} exceeds the {remaining} still refundable on order {orderId}.");
        }

        // Send an explicit amount unless this is a first, whole-capture refund (PayPal treats a
        // bodyless refund as a full refund of the capture).
        var isWholeCaptureRefund = amount is null && payment.TotalRefunded() == 0m
            && refundAmount == payment.CapturedGross;
        var money = isWholeCaptureRefund ? null : new Money(refundAmount, payment.Currency);

        // Scope the PayPal-Request-Id to this capture so the same caller key used on a different
        // capture (or in an earlier run against the shared sandbox account) does not collide. A true
        // repeat of the same key on the same capture is already short-circuited above via the stored
        // refund, so PayPal never sees a duplicate for a legitimate retry.
        var payPalRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId!, money, payPalRequestId, cancellationToken);
        var refund = new Refund(result.RefundId, idempotencyKey,
            result.Amount > 0 ? result.Amount : refundAmount, result.Status);
        payment.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Refunded {refund.Amount} on order {orderId} (refund {refund.PayPalRefundId}).");
        return refund;
    }

    private async Task<Order> LoadOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithPaymentByIdSpec(orderId, buyerId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task RenewAuthorizationAsync(Payment payment, Money amount, CancellationToken cancellationToken)
    {
        // Throws AuthorizationNotRenewableException when PayPal will no longer reauthorize.
        var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId, amount,
            $"reauth-{payment.AuthorizationId}", cancellationToken);
        payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        _logger.LogInformation($"Renewed authorization -> {renewed.AuthorizationId} ({renewed.Status}).");
    }

    private static bool IsAuthorizationStale(Payment payment)
    {
        // Treat a hold within its final few minutes (or already past) as stale, so we renew before
        // rather than race the capture against expiry.
        return payment.AuthorizationExpiresAt is { } expiry
            && expiry <= DateTimeOffset.UtcNow.AddMinutes(5);
    }

    private static bool LooksLikeExpiredAuthorization(PayPalApiException ex)
    {
        var haystack = $"{ex.PayPalName} {ex.RawBody}".ToUpperInvariant();
        return haystack.Contains("AUTHORIZATION")
            && (haystack.Contains("EXPIRED") || haystack.Contains("VOIDED")
                || haystack.Contains("CANNOT_BE_CAPTURED") || haystack.Contains("INVALID"));
    }

    // A key that is stable for a given order instance (so pay/capture retries dedupe) but differs
    // across app runs (the in-memory DB restarts order ids, but the placement time moves on).
    private static string OrderKey(Order order) => $"order-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}";

    private static string BuildInvoiceId(int orderId)
    {
        var invoiceId = $"ESHOP-{orderId}-{Guid.NewGuid():N}";
        return invoiceId.Length > 127 ? invoiceId[..127] : invoiceId;
    }

    private static Address DefaultAddress() =>
        new("N/A", "N/A", "N/A", "N/A", "00000");
}
