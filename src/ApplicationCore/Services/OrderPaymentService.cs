using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalPaymentService _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // Renew a hold this long before its stated expiry, so a capture never races the expiry boundary.
    private static readonly TimeSpan ReauthorizationMargin = TimeSpan.FromMinutes(2);

    // Per-process salt for the authorize idempotency key. The key must be STABLE within a run (so a
    // double-click authorizes once) yet UNIQUE across runs — because the in-memory database restarts
    // order ids at 1 every run, and PayPal caches the response for a PayPal-Request-Id (including a
    // failure), so a bare "auth-order-1" would collide with a previous run's cached result.
    private static readonly string InstanceSalt = Guid.NewGuid().ToString("N").Substring(0, 8);

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalPaymentService payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines,
        Address? shipToAddress, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new InvalidOrderStateException("An order must contain at least one line item.");
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidOrderStateException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(pictureUri))
            {
                pictureUri = "eCatalog-item-default.png";
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            // Price comes from the catalog, never the caller.
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipToAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Placed order {order.Id} for buyer with total {order.Total()}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken ct)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        // Idempotency: a double-click never authorizes twice. An already-authorized order returns as-is.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        string? vaultId = null;
        string? savedCardDescriptor = null;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodsByBuyerSpecification(buyerId, savedPaymentMethodId.Value), ct)
                ?? throw new EntityNotFoundException($"Saved card {savedPaymentMethodId} was not found.");
            vaultId = saved.VaultId;
            savedCardDescriptor = $"{saved.CardBrand} ending {saved.LastFourDigits}";
        }

        var currency = _settings.Currency;
        var amount = order.Total();
        // Stable within this run (repeat POSTs for the same order authorize once, at PayPal too),
        // unique across runs (see InstanceSalt).
        var idempotencyKey = $"auth-order-{orderId}-{InstanceSalt}";
        // A unique correlation token for reconciliation — never the resettable order id.
        var paymentReference = $"eshop-{orderId}-{Guid.NewGuid():N}";

        var request = new AuthorizeCardPaymentRequest(amount, currency, paymentReference,
            idempotencyKey, card, vaultId);
        var auth = await _payPal.AuthorizeAsync(request, ct);

        var payment = new OrderPayment(auth.PayPalOrderId, auth.AuthorizationId, auth.Status,
            amount, currency, auth.ExpiresAt, paymentReference, savedCardDescriptor);
        order.MarkAuthorized(payment);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Authorized order {orderId}: PayPal order {auth.PayPalOrderId}, authorization {auth.AuthorizationId}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // already captured — idempotent
        }

        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;
        var currency = payment.Currency;

        // Proactively renew a hold that is at/near expiry, rather than failing the capture.
        if (payment.AuthorizationExpiresAt.HasValue &&
            DateTimeOffset.UtcNow >= payment.AuthorizationExpiresAt.Value - ReauthorizationMargin)
        {
            await ReauthorizeAsync(order, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId,
                $"capture-auth-{payment.AuthorizationId}", ct);
        }
        catch (PaymentAuthorizationExpiredException)
        {
            // Reactive fallback: the hold expired between our check and the capture — renew, then capture.
            _logger.LogWarning($"Authorization {payment.AuthorizationId} for order {orderId} expired; renewing before capture.");
            await ReauthorizeAsync(order, ct);
            capture = await _payPal.CaptureAsync(payment.AuthorizationId,
                $"capture-auth-{payment.AuthorizationId}", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount,
            capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.CapturedAmount} {capture.Currency}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // already voided — idempotent
        }

        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be cancelled because it is {order.Status}. " +
                "A fulfilled order must be refunded instead.");
        }

        await _payPal.VoidAsync(order.Payment.AuthorizationId,
            $"void-auth-{order.Payment.AuthorizationId}", ct);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Cancelled order {orderId}: voided authorization {order.Payment.AuthorizationId}.");
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, string buyerId, decimal? amount,
        string idempotencyKey, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded)
            || order.Payment?.CaptureId is null)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId} cannot be refunded because it is {order.Status}.");
        }

        var payment = order.Payment;

        // Idempotency: the same caller key never refunds twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        // Cap the refund at what PayPal actually captured, net of prior refunds.
        var capturedAtPayPal = await _payPal.GetCapturedAmountAsync(payment.CaptureId!, ct);
        var alreadyRefunded = payment.TotalRefunded();
        var requested = amount ?? (capturedAtPayPal - alreadyRefunded); // null ⇒ refund the remainder

        if (requested <= 0m)
        {
            throw new InvalidOrderStateException($"Order {orderId} has nothing left to refund.");
        }

        if (alreadyRefunded + requested > capturedAtPayPal + 0.001m)
        {
            throw new InvalidOrderStateException(
                $"Refund of {requested} would exceed the remaining refundable amount " +
                $"({capturedAtPayPal - alreadyRefunded}) on order {orderId}.");
        }

        var refundResult = await _payPal.RefundAsync(payment.CaptureId!, requested, payment.Currency,
            idempotencyKey, ct);
        var refund = payment.AddRefund(refundResult.RefundId, idempotencyKey, refundResult.Amount,
            refundResult.Status);
        order.ReflectRefundState();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Refunded {refundResult.Amount} on order {orderId} (refund {refundResult.RefundId}); order now {order.Status}.");
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders;
    }

    // Renew (re-authorize) the current hold in place; the payment keeps the new authorization id.
    private async Task ReauthorizeAsync(Order order, CancellationToken ct)
    {
        var payment = order.Payment!;
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount,
                payment.Currency, $"reauth-auth-{payment.AuthorizationId}", ct);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            _logger.LogInformation($"Renewed authorization for order {order.Id}: new authorization {renewed.AuthorizationId}.");
        }
        catch (PaymentRejectedException ex)
        {
            throw new PaymentRejectedException(
                $"The authorization for order {order.Id} has expired and can no longer be renewed " +
                $"({ex.Message}). The shopper must pay for the order again.");
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentSpecification(orderId), ct)
            ?? throw new EntityNotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Hide existence: another shopper's order is indistinguishable from a missing one.
            throw new EntityNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }
}
