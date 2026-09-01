using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // A fresh instance per order: EF owns ShipToAddress and cannot re-key a shared instance.
    private static Address DefaultShipTo() => new Address("N/A", "N/A", "N/A", "N/A", "00000");

    // PayPal replays the original response (success OR failure) for a reused PayPal-Request-Id,
    // so keys must be stable within a run (double-click protection) but unique across runs
    // (the in-memory store resets ids on restart).
    private static readonly string InstanceTag = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly PayPalSettings _payPalSettings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway paymentGateway,
        PayPalSettings payPalSettings)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _paymentGateway = paymentGateway;
        _payPalSettings = payPalSettings;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentConflictException("An order requires at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be positive.");
        }

        var spec = new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, ct);

        var missing = lines.Select(l => l.CatalogItemId).Distinct()
            .Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentConflictException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri ?? string.Empty);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo(), items);
        await _orderRepository.AddAsync(order, ct);
        return order;
    }

    public async Task<Payment> PayOrderAsync(int orderId, string buyerId, GatewayCardDetails? card,
        int? savedCardId, CancellationToken ct = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);
        var existing = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        // Idempotent in effect: a repeat pay against an already-authorized order replays state.
        if (order.Status == OrderStatus.PaymentAuthorized && existing is { IsAuthorized: true })
        {
            return existing;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be paid.");
        }
        if (card is null && savedCardId is null)
        {
            throw new PaymentConflictException("Provide either card details or a saved paymentMethodId.");
        }

        var amount = order.Total();
        var reference = $"eshop-order-{orderId}-{InstanceTag}";
        var idempotencyKey = $"eshop-authorize-{orderId}-{InstanceTag}";

        GatewayAuthorization authorization;
        string? vaultTokenId = null;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId.Value, ct);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new SavedCardNotFoundException(savedCardId.Value);
            }
            vaultTokenId = savedCard.PayPalPaymentTokenId;
            authorization = await _paymentGateway.AuthorizeWithSavedCardAsync(
                reference, amount, _payPalSettings.Currency, vaultTokenId, idempotencyKey, ct);
        }
        else
        {
            authorization = await _paymentGateway.AuthorizeWithCardAsync(
                reference, amount, _payPalSettings.Currency, card!, idempotencyKey, ct);
        }

        if (authorization.Status != "CREATED")
        {
            throw new PaymentGatewayException(
                $"PayPal authorization returned status {authorization.Status}; the payment was not authorized.", 400);
        }

        var payment = existing ?? new Payment(orderId, buyerId, amount, _payPalSettings.Currency);
        payment.MarkAuthorized(
            authorization.PayPalOrderId,
            authorization.AuthorizationId,
            authorization.Status,
            authorization.ExpiresAt,
            vaultTokenId);

        if (existing is null)
        {
            await _paymentRepository.AddAsync(payment, ct);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkPaymentAuthorized();
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        if (order.Status == OrderStatus.Fulfilled && payment is { IsCaptured: true })
        {
            return payment;
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be fulfilled.");
        }
        if (payment is null || !payment.IsAuthorized || payment.AuthorizationId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no active authorization to capture.");
        }

        // A stale authorization must be renewed before it can be captured.
        var state = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId, ct);
        var usable = state.Status == "CREATED"
            && (state.ExpiresAt is null || state.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(5));
        if (!usable)
        {
            GatewayAuthorizationState renewed;
            try
            {
                renewed = await _paymentGateway.ReauthorizeAsync(
                    payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                    $"eshop-reauthorize-{orderId}-{InstanceTag}", ct);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentAuthorizationRenewalException(
                    $"Order {orderId}: the PayPal authorization has expired and can no longer be renewed. " +
                    "Cancel this order and ask the shopper to place and pay for a new one.", ex);
            }

            if (renewed.Status != "CREATED")
            {
                throw new PaymentAuthorizationRenewalException(
                    $"Order {orderId}: PayPal renewed the authorization with status {renewed.Status}. " +
                    "Cancel this order and ask the shopper to place and pay for a new one.");
            }
            payment.MarkAuthorizationRenewed(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        }

        var capture = await _paymentGateway.CaptureAuthorizationAsync(
            payment.AuthorizationId!, payment.AuthorizedAmount, payment.Currency,
            $"eshop-capture-{orderId}-{InstanceTag}", ct);

        if (capture.Status != "COMPLETED")
        {
            throw new PaymentGatewayException(
                $"PayPal capture for order {orderId} returned status {capture.Status}; the order was not fulfilled.", 422);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.Fee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        return payment;
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is already fulfilled; issue a refund instead of cancelling.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment ?? new Payment(orderId, order.BuyerId, order.Total(), _payPalSettings.Currency);
        }

        if (payment is { IsAuthorized: true } && payment.AuthorizationId is not null)
        {
            await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId, $"eshop-void-{orderId}-{InstanceTag}", ct);
            payment.MarkVoided("VOIDED");
            await _paymentRepository.UpdateAsync(payment, ct);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return payment ?? new Payment(orderId, order.BuyerId, order.Total(), _payPalSettings.Currency);
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnedOrderAsync(orderId, buyerId, ct);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);

        if (order.Status != OrderStatus.Fulfilled || payment is not { IsCaptured: true })
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        // Caller-supplied idempotency key: a repeat under the same key replays, never refunds twice.
        var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existingRefund is not null)
        {
            return existingRefund;
        }

        var refundAmount = amount ?? payment.RemainingRefundable;
        if (refundAmount <= 0m || refundAmount > payment.RemainingRefundable)
        {
            throw new PaymentConflictException(
                $"Refund amount {refundAmount:F2} exceeds the remaining refundable amount " +
                $"{payment.RemainingRefundable:F2} on order {orderId}.");
        }

        var refund = await _paymentGateway.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency, idempotencyKey, ct);

        var recorded = payment.AddRefund(refund.RefundId, refund.Amount, refund.Status, idempotencyKey);
        await _paymentRepository.UpdateAsync(payment, ct);
        return recorded;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, GatewayCardDetails card, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var vaulted = await _paymentGateway.SaveCardAsync(buyerId, card, Guid.NewGuid().ToString("N"), ct);

        var savedCard = new SavedCard(buyerId, vaulted.PaymentTokenId, vaulted.PayPalCustomerId,
            vaulted.Brand, vaulted.LastDigits, vaulted.Expiry, vaulted.Name);
        await _savedCardRepository.AddAsync(savedCard, ct);
        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListSavedCardsAsync(string buyerId, CancellationToken ct = default)
    {
        var cards = await _savedCardRepository.ListAsync(new SavedCardsByBuyerSpec(buyerId), ct);
        return cards;
    }

    public async Task DeleteSavedCardAsync(string buyerId, int savedCardId, CancellationToken ct = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(savedCardId, ct);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedCardNotFoundException(savedCardId);
        }

        await _paymentGateway.DeleteSavedCardAsync(savedCard.PayPalPaymentTokenId, ct);
        await _savedCardRepository.DeleteAsync(savedCard, ct);
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
