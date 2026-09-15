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
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the pay-for-an-order flow: place → authorize (hold) → fulfil (capture) →
/// cancel (void) / refund. Idempotency is enforced by a per-order lock plus the persisted order
/// state, so a double-click can never authorize or capture twice; PayPal request ids add a
/// second, HTTP-level guard.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IReadRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentConcurrencyGuard _concurrency;
    private readonly IPaymentSettings _settings;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IReadRepository<SavedPaymentMethod> paymentMethodRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        IPaymentConcurrencyGuard concurrency,
        IPaymentSettings settings)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _concurrency = concurrency;
        _settings = settings;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IEnumerable<OrderLine> lines, Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var lineList = lines?.ToList() ?? new List<OrderLine>();
        if (lineList.Count == 0)
        {
            throw new PaymentOperationException("An order must contain at least one line item.");
        }
        foreach (var line in lineList)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lineList.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentResourceNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lineList.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        order.SetCurrency(_settings.Currency);

        order = await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        using var _ = await _concurrency.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: a hold already placed is returned as-is; a second click never re-authorizes.
        if (order.PaymentStatus == OrderPaymentStatus.Authorized)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {orderId} is in state {order.PaymentStatus} and can no longer be paid.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var saved = (await _paymentMethodRepository.ListAsync(new SavedPaymentMethodByIdSpec(savedPaymentMethodId.Value, buyerId), cancellationToken)).FirstOrDefault();
            if (saved is null)
            {
                throw new PaymentResourceNotFoundException($"Saved card {savedPaymentMethodId} was not found for this shopper.");
            }
            vaultId = saved.PayPalVaultId;
        }
        else if (card is null)
        {
            throw new PaymentOperationException("A payment requires either card details or a saved card id.");
        }

        var currency = order.Currency ?? _settings.Currency;
        var amount = order.Total();

        // A globally-unique invoice reference: the bare order id is not unique across in-memory
        // restarts and PayPal blocks duplicate invoice ids. This value is stored on the order and is
        // the key reconciliation lines PayPal's transactions up against.
        var invoiceReference = $"{order.Id}-{Guid.NewGuid():N}";

        var request = new AuthorizeCardRequest
        {
            OrderReference = invoiceReference,
            Amount = amount,
            Currency = currency,
            Card = vaultId is null ? card : null,
            VaultId = vaultId
        };

        // Deterministic request id: a literal double-click (same order, same source) dedupes at PayPal,
        // while a retry after a decline with a different card gets a fresh id and is not replayed.
        var sourceKey = vaultId ?? (card is not null ? $"{card.Last4}{card.Expiry}" : "none");
        var requestId = $"eshop-auth-{invoiceReference}-{Stable(sourceKey)}";

        var auth = await _gateway.AuthorizeAsync(request, requestId, cancellationToken);
        order.MarkAuthorized(invoiceReference, auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _concurrency.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);

        // Already captured → nothing more to take. Idempotent.
        if (order.PaymentStatus is OrderPaymentStatus.Paid or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.PayPalAuthorizationId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled from state {order.PaymentStatus}; it must be authorized first.");
        }

        var currency = order.Currency ?? _settings.Currency;
        var amount = order.Total();
        var reauthorized = false;

        // Renew a stale hold before trying to take the money.
        var current = await _gateway.GetAuthorizationAsync(order.PayPalAuthorizationId, cancellationToken);
        if (IsUncapturable(current))
        {
            await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            reauthorized = true;
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(order.PayPalAuthorizationId!, amount, currency, $"eshop-capture-{order.PayPalAuthorizationId}", order.PayPalInvoiceReference ?? order.Id.ToString(), cancellationToken);
        }
        catch (PayPalApiException ex) when (!reauthorized && IsExpiredError(ex))
        {
            // The hold went stale between our check and the capture — renew and take it once more.
            await RenewAuthorizationAsync(order, amount, currency, cancellationToken);
            capture = await _gateway.CaptureAsync(order.PayPalAuthorizationId!, amount, currency, $"eshop-capture-{order.PayPalAuthorizationId}", order.PayPalInvoiceReference ?? order.Id.ToString(), cancellationToken);
        }

        order.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var _ = await _concurrency.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return order;
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized || order.PayPalAuthorizationId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be cancelled from state {order.PaymentStatus}; cancellation only releases a hold that has not been captured.");
        }

        await _gateway.VoidAsync(order.PayPalAuthorizationId, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<Order> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        using var _ = await _concurrency.AcquireAsync(OrderKey(orderId), cancellationToken);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Repeating a refund under the same key must not refund twice.
        var existing = order.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return order;
        }

        if (order.PayPalCaptureId is null)
        {
            throw new PaymentOperationException($"Order {orderId} has no captured payment to refund.");
        }

        var refundAmount = amount ?? order.RefundableRemaining();
        order.EnsureRefundable(refundAmount);

        var currency = order.Currency ?? _settings.Currency;
        var result = await _gateway.RefundAsync(order.PayPalCaptureId, refundAmount, currency, idempotencyKey, order.PayPalInvoiceReference ?? order.Id.ToString(), cancellationToken);

        var refund = new OrderRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.AddRefund(refund);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new OrdersByBuyerWithPaymentSpec(buyerId), cancellationToken);
        return orders;
    }

    // ---- helpers ----

    private async Task RenewAuthorizationAsync(Order order, decimal amount, string currency, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(order.PayPalAuthorizationId!, amount, currency, $"eshop-reauth-{order.PayPalAuthorizationId}", cancellationToken);
            order.MarkReauthorized(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        }
        catch (PayPalApiException ex)
        {
            throw new AuthorizationNotRenewableException(
                $"The authorization holding funds for order {order.Id} has expired and PayPal could not renew it ({ex.PayPalName ?? "reauthorization failed"}). " +
                "Ask the shopper to pay again on a new order; this order can no longer be fulfilled on its current hold.");
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = (await _orderRepository.ListAsync(new OrderByIdWithPaymentSpec(orderId), cancellationToken)).FirstOrDefault();
        if (order is null)
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // Owner-scoped: never reveal that another shopper's order exists.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    /// <summary>An authorization that can no longer be captured directly (expired or not in a capturable state).</summary>
    private static bool IsUncapturable(AuthorizationResult auth)
    {
        if (auth.ExpiresAt.HasValue && auth.ExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return true;
        }
        return !string.Equals(auth.Status, "CREATED", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(auth.Status, "PENDING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpiredError(PayPalApiException ex) =>
        (ex.PayPalName?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.Message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
        || ex.StatusCode == 422;

    private static string OrderKey(int orderId) => $"order:{orderId}";

    /// <summary>A short, stable, request-id-safe token derived from an arbitrary string.</summary>
    private static string Stable(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash.ToString("x8");
        }
    }
}
