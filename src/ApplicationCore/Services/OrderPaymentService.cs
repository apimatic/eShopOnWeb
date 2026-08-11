using System;
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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPayPalClient _payPalClient;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentConfiguration _paymentConfiguration;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPayPalClient payPalClient,
        IUriComposer uriComposer,
        IPaymentConfiguration paymentConfiguration,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPalClient = payPalClient;
        _uriComposer = uriComposer;
        _paymentConfiguration = paymentConfiguration;
        _logger = logger;
    }

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one line.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var missing = catalogItemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ResourceNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var currency = _paymentConfiguration.Currency;
        var order = new Order(buyerId, shipToAddress, items, currency);

        order = await _orderRepository.AddAsync(order, cancellationToken);

        // Stamp a merchant reference now that the order has an id, so it can be reconciled later.
        // (well under PayPal's 127-char invoice_id limit).
        order.Payment!.AssignMerchantReference($"ESHOP-{order.Id}-{Guid.NewGuid():N}");
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Placed order {order.Id} for buyer {buyerId}, total {order.Total()} {currency}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect: an already-held order is returned as-is.
        if (payment.IsAuthorized && !string.Equals(payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException(order.Id, order.Status, "authorize");
        }

        var instrument = await ResolveInstrumentAsync(buyerId, card, savedPaymentMethodId, cancellationToken);
        var descriptor = await DescribeInstrumentAsync(buyerId, savedPaymentMethodId, cancellationToken);

        // 1) Create the PayPal order (intent AUTHORIZE) if not created yet.
        if (string.IsNullOrEmpty(payment.PayPalOrderId))
        {
            var created = await _payPalClient.CreateAuthorizationOrderAsync(
                payment.Amount, payment.Currency, payment.MerchantReference ?? $"ESHOP-{order.Id}",
                payment.AuthorizeIdempotencyKey + "-ord", cancellationToken);
            payment.RecordPayPalOrder(created.OrderId);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        // 2) Authorize (hold funds) with the chosen instrument.
        var auth = await _payPalClient.AuthorizeOrderAsync(
            payment.PayPalOrderId!, instrument, payment.AuthorizeIdempotencyKey, cancellationToken);

        payment.RecordAuthorization(auth.AuthorizationId, auth.Status, auth.ExpiresAt,
            auth.CardBrand, auth.CardLast4, descriptor);
        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Authorized order {order.Id}: authorization {auth.AuthorizationId} ({auth.Status}).");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect: an already-captured order is returned as-is.
        if (order.Status == OrderStatus.Fulfilled && payment.IsCaptured)
        {
            return order;
        }
        if (order.Status != OrderStatus.Authorized || !payment.IsAuthorized)
        {
            throw new InvalidOrderStateException(order.Id, order.Status, "fulfil");
        }

        // Renew a hold that has gone stale before fulfilment rather than failing the capture.
        if (payment.IsAuthorizationExpired(DateTimeOffset.UtcNow))
        {
            await RenewAuthorizationAsync(order, payment, cancellationToken);
        }

        CaptureResult capture;
        try
        {
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency,
                payment.CaptureIdempotencyKey, finalCapture: true, cancellationToken);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold expired between our check and the capture: renew once, then capture again.
            _logger.LogWarning($"Capture of order {order.Id} failed with an expired hold; renewing and retrying.");
            await RenewAuthorizationAsync(order, payment, cancellationToken);
            capture = await _payPalClient.CaptureAuthorizationAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency,
                payment.CaptureIdempotencyKey, finalCapture: true, cancellationToken);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Fulfilled order {order.Id}: capture {capture.CaptureId} gross {capture.GrossAmount} fee {capture.PayPalFee} net {capture.NetAmount}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = RequirePayment(order);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status != OrderStatus.Authorized || !payment.IsAuthorized)
        {
            throw new InvalidOrderStateException(order.Id, order.Status, "cancel");
        }

        await _payPalClient.VoidAuthorizationAsync(
            payment.AuthorizationId!, payment.AuthorizeIdempotencyKey + "-void", cancellationToken);

        payment.MarkAuthorizationVoided();
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation($"Cancelled order {order.Id}: authorization {payment.AuthorizationId} voided.");
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = RequirePayment(order);

        // Idempotent in effect: replaying the same key returns the original refund, never a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        if (!payment.IsCaptured || (order.Status != OrderStatus.Fulfilled && order.Status != OrderStatus.PartiallyRefunded))
        {
            throw new InvalidOrderStateException(order.Id, order.Status, "refund");
        }

        var remaining = payment.RefundableRemaining;
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {order.Id} has already been fully refunded; nothing remains to refund.");
        }

        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0m)
        {
            throw new PaymentException("A refund amount must be greater than zero.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new PaymentException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the {remaining} {payment.Currency} still refundable on order {order.Id}.");
        }

        // Always send the explicit amount (even for the full remainder): it is deterministic and
        // avoids the empty-body path, which PayPal rejects on an already-partially-refunded capture.
        // The caller's key dedups at our level; PayPal's request id must be unique per capture, so
        // derive it from (capture, key) — stable for retries of this refund, distinct across captures.
        var payPalRequestId = BuildRequestId(payment.CaptureId!, idempotencyKey);
        var result = await _payPalClient.RefundCaptureAsync(
            payment.CaptureId!, refundAmount, payment.Currency, payPalRequestId, cancellationToken);

        var refund = payment.AddRefund(idempotencyKey, result.RefundId, result.GrossAmount, result.Status);
        order.MarkRefunded(fullyRefunded: payment.TotalRefunded >= payment.CapturedGross);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            $"Refunded {result.GrossAmount} {payment.Currency} on order {order.Id}: refund {result.RefundId} ({result.Status}).");
        return (order, refund);
    }

    public async Task<IReadOnlyCollection<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// Builds a PayPal-Request-Id that is stable for retries of the same logical operation but unique
    /// across resources, keeping within PayPal's 108-character limit.
    /// </summary>
    private static string BuildRequestId(string resourceId, string key)
    {
        var combined = $"{resourceId}-{key}";
        return combined.Length <= 108 ? combined : combined.Substring(0, 108);
    }

    private async Task RenewAuthorizationAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            var reauth = await _payPalClient.ReauthorizeAsync(
                payment.AuthorizationId!, payment.Amount, payment.Currency,
                Guid.NewGuid().ToString("N"), cancellationToken);
            payment.RefreshAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Renewed the payment hold for order {order.Id}: authorization {reauth.AuthorizationId}.");
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"The payment hold for order {order.Id} has expired and can no longer be renewed ({ex.PayPalName ?? "cannot reauthorize"}). " +
                "Ask the shopper to pay for the order again; a fresh authorization is required before it can be fulfilled.", ex);
        }
    }

    private static bool IsExpiredAuthorization(PayPalApiException ex)
    {
        var name = ex.PayPalName ?? string.Empty;
        var message = ex.Message ?? string.Empty;
        return name.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || message.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase)
            || name.Contains("AUTHORIZATION", StringComparison.OrdinalIgnoreCase) && message.Contains("no longer valid", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<PaymentInstrument> ResolveInstrumentAsync(string buyerId, CardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await LoadOwnedSavedMethodAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
            return PaymentInstrument.FromVault(saved.PayPalVaultId);
        }
        if (card is not null)
        {
            return PaymentInstrument.FromCard(card);
        }
        throw new PaymentException("Provide either card details or a saved card id to pay with.");
    }

    private async Task<string?> DescribeInstrumentAsync(string buyerId, int? savedPaymentMethodId, CancellationToken cancellationToken)
    {
        if (!savedPaymentMethodId.HasValue)
        {
            return null;
        }
        var saved = await LoadOwnedSavedMethodAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
        return $"{saved.CardBrand} ****{saved.CardLast4}".Trim();
    }

    private async Task<SavedPaymentMethod> LoadOwnedSavedMethodAsync(string buyerId, int savedPaymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _paymentMethodRepository.GetByIdAsync(savedPaymentMethodId, cancellationToken);
        if (saved is null || saved.BuyerId != buyerId)
        {
            // Same response whether missing or someone else's, so ownership is never leaked.
            throw new ForbiddenAccessException($"Saved card {savedPaymentMethodId} is not available to this shopper.");
        }
        return saved;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ForbiddenAccessException($"Order {orderId} is not available to this shopper.");
        }
        return order;
    }

    private static OrderPayment RequirePayment(Order order)
    {
        if (order.Payment is null)
        {
            throw new PaymentException($"Order {order.Id} was not created through the payment API and has no payment to act on.");
        }
        return order.Payment;
    }
}
