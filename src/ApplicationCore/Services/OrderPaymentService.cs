using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderPaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedPaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IReadRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentConfiguration _configuration;
    private readonly IPaymentLock _paymentLock;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IReadRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        IPaymentConfiguration configuration,
        IPaymentLock paymentLock,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _configuration = configuration;
        _paymentLock = paymentLock;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least one.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);
        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipToAddress ?? new Address("Digital delivery", "N/A", "N/A", "US", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Stable, unique per order and <=127 chars: used as PayPal invoice_id so a reporting
        // transaction can be lined back up against this order during reconciliation.
        var reference = $"ESHOP-{order.Id}-{Guid.NewGuid():N}";
        var payment = new OrderPayment(order.Id, buyerId, order.Total(), _configuration.Currency, reference);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed for {buyerId}, total {payment.Amount} {payment.CurrencyCode}, awaiting payment.");
        return order.Id;
    }

    public async Task<OrderPaymentView> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (card is null && savedPaymentMethodId is null)
        {
            throw new PaymentException("Provide either card details or a saved card to pay with.");
        }
        if (card is not null && savedPaymentMethodId is not null)
        {
            throw new PaymentException("Provide either card details or a saved card, not both.");
        }

        using var _ = await _paymentLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var (order, payment) = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (payment.Status == PaymentStatus.Authorized)
        {
            // Idempotent: the hold already exists (e.g. a double-click). Return current state.
            return ToView(order, payment);
        }
        if (payment.Status != PaymentStatus.PendingPayment && payment.Status != PaymentStatus.Failed)
        {
            throw new PaymentException($"Order {orderId} cannot be paid from state {payment.Status}.");
        }

        string? vaultId = null;
        string? fallbackBrand = null;
        string? fallbackLast4 = null;
        if (savedPaymentMethodId is not null)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new PaymentNotFoundException($"Saved card {savedPaymentMethodId} was not found for the caller.");
            }
            vaultId = savedCard.VaultId;
            fallbackBrand = savedCard.Brand;
            fallbackLast4 = savedCard.Last4;
        }

        var request = new PaymentAuthorizationRequest(
            payment.Amount, payment.CurrencyCode, payment.Reference, card, vaultId, IdempotencyKey("pay", payment.Reference));

        AuthorizationResult result;
        try
        {
            result = await _gateway.AuthorizeAsync(request, cancellationToken);
        }
        catch (PaymentChallengeRequiredException)
        {
            throw; // surfaced as-is; this integration does not build an approval round-trip.
        }
        catch (PaymentException ex)
        {
            payment.MarkFailed();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentException($"Payment authorization failed for order {orderId}: {ex.Message}", ex);
        }

        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt,
            result.CardBrand ?? fallbackBrand, result.CardLast4 ?? fallbackLast4);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} authorized (hold {result.AuthorizationId}, status {result.Status}).");
        return ToView(order, payment);
    }

    public async Task<OrderPaymentView> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _paymentLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return ToView(order, payment); // Idempotent: money already taken.
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled from state {payment.Status}; it must be authorized first.");
        }

        var authorizationId = payment.AuthorizationId!;

        // Renew a hold that has already gone stale before attempting the capture.
        if (IsAuthorizationStale(payment))
        {
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.CurrencyCode,
                IdempotencyKey("capture", payment.Reference), cancellationToken);
        }
        catch (PaymentException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold expired between our check and the capture: renew once and retry.
            authorizationId = await RenewAuthorizationAsync(payment, cancellationToken);
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.CurrencyCode,
                IdempotencyKey("capture", payment.Reference), cancellationToken);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.Amount} (fee {capture.PayPalFee}, net {capture.NetAmount}).");
        return ToView(order, payment);
    }

    public async Task<OrderPaymentView> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _paymentLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return ToView(order, payment); // Idempotent.
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be cancelled from state {payment.Status}. Only an authorized, not-yet-fulfilled order can be cancelled; use a refund after fulfilment.");
        }

        await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} cancelled: hold {payment.AuthorizationId} released.");
        return ToView(order, payment);
    }

    public async Task<RefundView> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        using var _ = await _paymentLock.AcquireAsync(OrderKey(orderId), cancellationToken);

        var (_, payment) = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} has not been captured; there is nothing to refund.");
        }

        // Idempotent by caller-supplied key: replaying the same key returns the same refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return new RefundView(existing.Id, existing.PayPalRefundId, existing.Amount, existing.Status, existing.CreatedAt);
        }

        var refundable = payment.RefundableAmount();
        var refundAmount = amount ?? refundable;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("Refund amount must be greater than zero.");
        }
        if (refundAmount > refundable)
        {
            throw new PaymentException(
                $"Refund of {refundAmount} exceeds the remaining refundable amount {refundable} for order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId!, refundAmount, payment.CurrencyCode,
            payment.Reference, idempotencyKey, cancellationToken);

        var refund = new Refund(result.RefundId, idempotencyKey, result.Amount, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation($"Order {orderId} refunded {result.Amount} (PayPal refund {result.RefundId}); status now {payment.Status}.");
        return new RefundView(refund.Id, refund.PayPalRefundId, refund.Amount, refund.Status, refund.CreatedAt);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => paymentByOrder.TryGetValue(o.Id, out var p)
                ? ToView(o, p)
                : ToViewWithoutPayment(o))
            .ToList();
    }

    // --- helpers ---

    private static string OrderKey(int orderId) => $"order:{orderId}";

    private static string IdempotencyKey(string action, string reference) => $"{action}:{reference}";

    private bool IsAuthorizationStale(OrderPayment payment)
    {
        if (string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payment.AuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        // A little slack so we renew just before, not after, the honor window closes.
        return payment.AuthorizationExpiresAt is { } expiry && expiry <= DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private static bool IsExpiredAuthorization(PaymentException ex)
    {
        // PayPal reports a capture against a lapsed hold with issues such as
        // AUTHORIZATION_EXPIRED / AUTH_CAPTURE ... _EXPIRED. Match on the shared token.
        var message = ex.Message?.ToUpperInvariant() ?? string.Empty;
        return message.Contains("EXPIRED");
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount,
                payment.CurrencyCode, cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            _logger.LogInformation($"Order {payment.OrderId}: stale hold renewed as {renewed.AuthorizationId}.");
            return renewed.AuthorizationId;
        }
        catch (PaymentException ex)
        {
            throw new PaymentException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). " +
                "Ask the shopper to authorize the order again before fulfilling it.", ex);
        }
    }

    private async Task<(Order order, OrderPayment payment)> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentNotFoundException($"No payment found for order {orderId}.");
        }
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return (order, payment);
    }

    private async Task<(Order order, OrderPayment payment)> LoadOwnedOrderAsync(int orderId, string buyerId,
        CancellationToken cancellationToken)
    {
        var (order, payment) = await LoadOrderAsync(orderId, cancellationToken);
        if (payment.BuyerId != buyerId || order.BuyerId != buyerId)
        {
            // Do not reveal another shopper's order.
            throw new PaymentNotFoundException($"Order {orderId} was not found for the caller.");
        }
        return (order, payment);
    }

    private OrderPaymentView ToView(Order order, OrderPayment payment)
    {
        var items = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();
        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(r.Id, r.PayPalRefundId, r.Amount, r.Status, r.CreatedAt))
            .ToList();

        return new OrderPaymentView(
            order.Id, payment.BuyerId, order.Total(), payment.CurrencyCode, payment.Status.ToString(), order.OrderDate,
            payment.PayPalOrderId, payment.AuthorizationId, payment.AuthorizationStatus, payment.AuthorizationExpiresAt,
            payment.CaptureId, payment.CapturedAmount, payment.PayPalFee, payment.NetAmount,
            payment.TotalRefunded(), payment.RefundableAmount(),
            payment.CardBrand, payment.CardLast4, items, refunds);
    }

    private OrderPaymentView ToViewWithoutPayment(Order order)
    {
        var items = order.OrderItems
            .Select(i => new OrderLineView(i.ItemOrdered.CatalogItemId, i.ItemOrdered.ProductName, i.UnitPrice, i.Units))
            .ToList();
        return new OrderPaymentView(
            order.Id, order.BuyerId, order.Total(), _configuration.Currency, PaymentStatus.PendingPayment.ToString(),
            order.OrderDate, null, null, null, null, null, null, null, null, 0m, 0m, null, null, items,
            Array.Empty<RefundView>());
    }
}
