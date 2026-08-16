using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly PaymentInstance _instance;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalClient payPal,
        PayPalSettings settings,
        PaymentInstance instance,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _settings = settings;
        _instance = instance;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines, Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentFlowException("An order must contain at least one item.", 400);
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentFlowException("Every order line must have a quantity of at least 1.", 400);
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentFlowException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", 400);
        }

        // Amounts always come from catalog prices, never from the caller.
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? DefaultAddress();
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Order {order.Id} placed for buyer {buyerId}, total {order.Total()} {Currency}.");
        return order.Id;
    }

    public async Task<PaymentView> PayOrderAsync(string buyerId, int orderId, CardPaymentDetails? card, int? savedPaymentMethodId, CancellationToken ct)
    {
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentFlowException("Provide either card details or a saved payment method id to pay.", 400);
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentFlowException("Provide either card details or a saved payment method id, not both.", 400);
        }

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new PaymentFlowException($"Order {orderId} was not found.", 404);
        }
        EnsureOwnership(order.BuyerId, buyerId, orderId);

        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (existing is not null)
        {
            // Idempotent in effect: a double-click never authorizes twice.
            if (existing.Status == PaymentStatus.Failed)
            {
                await _paymentRepository.DeleteAsync(existing, ct);
            }
            else if (existing.Status == PaymentStatus.Cancelled)
            {
                throw new PaymentFlowException($"Order {orderId} was cancelled and cannot be paid.", 409);
            }
            else
            {
                return ToView(existing);
            }
        }

        var amount = order.Total();
        var reference = PaymentReferenceFor(orderId);
        var idempotencyKey = $"authorize-{reference}";

        AuthorizationResult auth;
        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByBuyerAndIdSpecification(buyerId, savedPaymentMethodId.Value), ct);
            if (savedCard is null)
            {
                throw new PaymentFlowException($"Saved card {savedPaymentMethodId} was not found for this shopper.", 404);
            }

            auth = await _payPal.AuthorizeWithVaultedCardAsync(amount, Currency, savedCard.PaymentTokenId,
                idempotencyKey, reference, reference, ct);
        }
        else
        {
            auth = await _payPal.AuthorizeWithCardAsync(amount, Currency, card!,
                idempotencyKey, reference, reference, ct);
        }

        var payment = new OrderPayment(orderId, buyerId, Currency, amount, reference,
            auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        payment = await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} authorized: paypalOrder={auth.PayPalOrderId} auth={auth.AuthorizationId} hold={amount} {Currency}.");
        return ToView(payment);
    }

    public async Task<PaymentView> FulfilAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new PaymentFlowException($"Order {orderId} has no payment to fulfil; it must be paid first.", 409);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToView(payment); // already fulfilled — idempotent.
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentFlowException($"Order {orderId} is {payment.Status} and cannot be fulfilled.", 409);
        }

        var capture = await CaptureWithRenewalAsync(payment, ct);
        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} fulfilled: capture={capture.CaptureId} gross={capture.GrossAmount} fee={capture.PayPalFee} net={capture.NetAmount} {Currency}.");
        return ToView(payment);
    }

    private async Task<CaptureResult> CaptureWithRenewalAsync(OrderPayment payment, CancellationToken ct)
    {
        var captureKey = $"capture-{payment.PaymentReference}";

        // Proactively renew an authorization that has already gone stale.
        var expired = payment.AuthorizationExpiresAt.HasValue &&
                      payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;
        if (expired)
        {
            await RenewAuthorizationAsync(payment, ct);
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, $"{captureKey}-{payment.AuthorizationId}", ct);
        }

        try
        {
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, captureKey, ct);
        }
        catch (PayPalException ex) when (IsStaleAuthorization(ex))
        {
            _logger.LogInformation($"Order {payment.OrderId} authorization {payment.AuthorizationId} is stale; attempting to renew before fulfilment.");
            await RenewAuthorizationAsync(payment, ct);
            return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, $"{captureKey}-{payment.AuthorizationId}", ct);
        }
    }

    private async Task RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.Amount, payment.Currency, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        }
        catch (PayPalException ex)
        {
            throw new PaymentFlowException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). " +
                "Ask the shopper to place and pay for the order again.", 409);
        }
    }

    private static bool IsStaleAuthorization(PayPalException ex)
    {
        var m = ex.Message?.ToUpperInvariant() ?? string.Empty;
        // PayPal signals a no-longer-capturable hold with these issue codes.
        return m.Contains("AUTH_EXPIRED")
            || m.Contains("AUTHORIZATION_EXPIRED")
            || m.Contains("AUTHORIZATION_VOIDED")
            || m.Contains("PREVIOUSLY_VOIDED");
    }

    public async Task<PaymentView> CancelAsync(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new PaymentFlowException($"Order {orderId} has no payment to cancel.", 409);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return ToView(payment); // idempotent
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentFlowException(
                $"Order {orderId} is {payment.Status}; only an authorized (not yet fulfilled) order can be cancelled. " +
                "Use a refund to return money after fulfilment.", 409);
        }

        await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} cancelled: authorization {payment.AuthorizationId} voided, no money moved.");
        return ToView(payment);
    }

    public async Task<(string RefundId, PaymentView Payment)> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), ct);
        if (payment is null)
        {
            throw new PaymentFlowException($"Order {orderId} has no payment to refund.", 404);
        }
        EnsureOwnership(payment.BuyerId, buyerId, orderId);

        if (payment.CaptureId is null)
        {
            throw new PaymentFlowException($"Order {orderId} has not been fulfilled/captured, so there is nothing to refund.", 409);
        }

        // Idempotent per caller-supplied key: repeating a request never refunds twice.
        var priorRefund = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (priorRefund is not null)
        {
            return (priorRefund.RefundId, ToView(payment));
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentFlowException("Refund amount must be greater than zero.", 400);
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new PaymentFlowException(
                $"Refund of {refundAmount:0.00} {Currency} exceeds the refundable remaining {remaining:0.00} {Currency} for order {orderId}.", 409);
        }

        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundAmount, Currency, idempotencyKey,
            $"Refund for eShop order {orderId}", ct);
        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} {Currency}: refund={result.RefundId} (remaining now {payment.RefundableRemaining()}).");
        return (refund.RefundId, ToView(payment));
    }

    public async Task<IReadOnlyList<OrderSummaryView>> GetMyOrdersAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpecification(buyerId), ct);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o =>
            {
                paymentByOrder.TryGetValue(o.Id, out var payment);
                var items = o.OrderItems
                    .Select(i => new OrderItemView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
                    .ToList();
                var status = payment is null ? PaymentStatus.AwaitingPayment.ToString() : payment.Status.ToString();
                return new OrderSummaryView(o.Id, o.OrderDate, o.Total(), status,
                    payment is null ? null : ToView(payment), items);
            })
            .ToList();
    }

    private string PaymentReferenceFor(int orderId) => $"ESHOP-{_instance.RunId}-{orderId}";

    private static void EnsureOwnership(string ownerBuyerId, string callerBuyerId, int orderId)
    {
        if (!string.Equals(ownerBuyerId, callerBuyerId, StringComparison.Ordinal))
        {
            // Do not reveal existence of another shopper's order.
            throw new PaymentFlowException($"Order {orderId} was not found.", 404);
        }
    }

    private static Address DefaultAddress() =>
        new Address("123 Main St", "Redmond", "WA", "US", "98052");

    private static PaymentView ToView(OrderPayment p) => new(
        p.OrderId,
        p.Status.ToString(),
        p.Currency,
        p.Amount,
        p.PayPalOrderId,
        p.AuthorizationId,
        p.AuthorizationStatus,
        p.AuthorizationExpiresAt,
        p.CaptureId,
        p.CaptureStatus,
        p.CapturedAmount,
        p.PayPalFee,
        p.NetAmount,
        p.TotalRefunded(),
        p.RefundableRemaining(),
        p.FailureReason,
        p.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.RefundId, r.Amount, r.Currency, r.Status, r.CreatedAt))
            .ToList());
}
