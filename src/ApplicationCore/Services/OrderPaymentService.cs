using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Entities.BuyerAggregate.Buyer> _buyerRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IPaymentSettings _paymentSettings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // A little slack so a hold that is about to lapse is renewed proactively rather than failing capture.
    private static readonly TimeSpan ExpiryBuffer = TimeSpan.FromMinutes(2);

    // A per-process nonce. Order ids restart every run under the in-memory database, but PayPal remembers
    // invoice ids and idempotency keys for hours; the nonce keeps them unique across runs while staying stable
    // within a run, so a double-click is still idempotent yet a fresh run never collides with a stale one.
    private static readonly string RunNonce = Guid.NewGuid().ToString("N").Substring(0, 12);

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Entities.BuyerAggregate.Buyer> buyerRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        IPaymentSettings paymentSettings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _paymentSettings = paymentSettings;
        _logger = logger;
    }

    private string Currency => _paymentSettings.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines == null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line.", nameof(lines));
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new ArgumentException($"Catalog item {line.CatalogItemId} does not exist.");
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Placed order {order.Id} for {buyerId} awaiting payment, total {order.Total()} {Currency}.");
        return order;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        // Idempotent in effect: if the order is already authorized, a repeat pay is a no-op.
        if (order.Status == OrderStatus.Authorized && order.Payment != null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            vaultId = await ResolveSavedCardVaultIdAsync(buyerId, savedPaymentMethodId.Value, cancellationToken);
        }
        else if (card == null)
        {
            throw new ArgumentException("A payment requires either card details or a saved payment method.");
        }

        var total = order.Total();
        // Stable key per order (within the run) so a double-click authorizes at most once at PayPal.
        var idempotencyKey = $"order-{orderId}-authorize-{RunNonce}";

        // invoice_id must be unique per transaction for this merchant; custom_id stays the stable order
        // reference so reconciliation can still line the transaction up against this order.
        var result = await _gateway.AuthorizeAsync(total, Currency, invoiceId: $"{OrderReference(orderId)}-{RunNonce}",
            customId: OrderReference(orderId), card: card, vaultId: vaultId, idempotencyKey: idempotencyKey,
            cancellationToken: cancellationToken);

        order.RecordAuthorization(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt, Currency);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Authorized order {orderId} (PayPal order {result.PayPalOrderId}, auth {result.AuthorizationId}) for {total} {Currency}.");
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Fulfilled)
        {
            return order; // already captured — nothing more to do
        }
        if (order.Status != OrderStatus.Authorized || order.Payment == null)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;
        var captureKey = $"order-{orderId}-capture-{RunNonce}";
        var renewed = false;

        // Proactively renew a hold that has lapsed (or is about to) before trying to capture.
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(orderId, payment, cancellationToken);
            renewed = true;
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, captureKey, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (!renewed && IsExpiredAuthorizationError(ex))
        {
            // Reactive fallback: PayPal rejected the capture because the hold had expired. Renew and retry once.
            await RenewAuthorizationAsync(orderId, payment, cancellationToken);
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId, captureKey, cancellationToken);
        }

        order.RecordFulfilment(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.GrossAmount} {Currency} (fee {capture.PayPalFee}, net {capture.NetAmount}), capture {capture.CaptureId}.");
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment == null)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be cancelled because it is {order.Status}. Only an authorized, unfulfilled order can be cancelled.");
        }

        await _gateway.VoidAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
        order.RecordCancellation();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Cancelled order {orderId}: released hold on authorization {order.Payment.AuthorizationId}.");
        return order;
    }

    public async Task<RefundOutcome> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId == null)
        {
            throw new InvalidOrderStateException($"Order {orderId} cannot be refunded because it has not been fulfilled.");
        }

        var payment = order.Payment;

        // Replay protection: the same caller key returns the original refund without touching PayPal again.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return new RefundOutcome(existing, WasReplay: true);
        }

        if (amount.HasValue)
        {
            if (amount.Value <= 0m)
            {
                throw new ArgumentException("A refund amount must be greater than zero.");
            }
            // A partly-refunded order must never become refundable beyond what was captured.
            if (amount.Value > payment.RefundableRemaining)
            {
                throw new InvalidOrderStateException(
                    $"Refund of {amount.Value} {Currency} exceeds the {payment.RefundableRemaining} {Currency} still refundable on order {orderId}.");
            }
        }
        else if (payment.RefundableRemaining <= 0m)
        {
            throw new InvalidOrderStateException($"Order {orderId} has already been fully refunded.");
        }

        // Scope the PayPal idempotency key to this order (and run) so distinct orders never collide on the same caller key.
        var gatewayKey = $"order-{orderId}-refund-{idempotencyKey}-{RunNonce}";
        var result = await _gateway.RefundCaptureAsync(payment.CaptureId!, amount, Currency, gatewayKey, cancellationToken);

        var refund = payment.AddRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        _logger.LogInformation($"Refunded {result.Amount} {Currency} on order {orderId} (refund {result.RefundId}, status {result.Status}).");
        return new RefundOutcome(refund, WasReplay: false);
    }

    // --- helpers ------------------------------------------------------------------------------

    private static string OrderReference(int orderId) => $"ESHOP-{orderId}";

    private bool IsAuthorizationStale(Payment payment) =>
        payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow.Add(ExpiryBuffer);

    private static bool IsExpiredAuthorizationError(PayPalGatewayException ex)
    {
        if (ex.StatusCode is not (422 or 400)) return false;
        var name = ex.Name?.ToUpperInvariant() ?? string.Empty;
        return name.Contains("AUTHORIZATION") || name.Contains("EXPIRED") || name.Contains("STATE");
    }

    private async Task RenewAuthorizationAsync(int orderId, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewal = await _gateway.ReauthorizeAsync(payment.AuthorizationId, payment.AuthorizedAmount, Currency, cancellationToken);
            payment.RenewAuthorization(renewal.AuthorizationId, renewal.Status, renewal.ExpiresAt);
            _logger.LogInformation($"Renewed the authorization on order {orderId}: new authorization {renewal.AuthorizationId}.");
        }
        catch (PayPalGatewayException ex)
        {
            throw new InvalidOrderStateException(
                $"Order {orderId}: the payment authorization has gone stale and could not be renewed " +
                $"(PayPal reported {ex.Name ?? "an error"}, debug_id {ex.DebugId ?? "n/a"}). " +
                "The hold can no longer be captured — ask the shopper to place and pay for the order again.");
        }
    }

    private async Task<string> ResolveSavedCardVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var method = buyer?.FindPaymentMethod(paymentMethodId);
        if (method == null)
        {
            // Not the caller's saved card (or does not exist): do not reveal which.
            throw new ArgumentException($"Saved payment method {paymentMethodId} was not found for this shopper.");
        }
        return method.VaultId;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        return order ?? throw new OrderNotFoundException(orderId);
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }
}
