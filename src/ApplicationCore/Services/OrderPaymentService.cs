using System;
using System.Collections.Generic;
using System.Globalization;
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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IUriComposer uriComposer,
        IPaymentGateway gateway,
        PayPalConfiguration configuration,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _logger = logger;
        _currency = configuration.Currency;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentOperationException("An order must contain at least one line item.");
        }

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).Distinct().ToArray()), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentOperationException($"Catalog item {line.CatalogItemId} was not found.");

            // Amounts come from catalog prices, never the caller.
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // The public payments API does not collect a shipping address; use a placeholder so the
        // existing order model (which requires one) is satisfied without inventing a parallel model.
        var shipToAddress = new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, shipToAddress, items);
        order = await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId} awaiting payment, total {Format(order.Total())} {_currency}.");
        return order;
    }

    public async Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, GatewayCardDetails? card, int? savedPaymentMethodId, CancellationToken ct = default)
    {
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent in effect: if the hold is already in place, do not authorize again.
        if (order.Status != OrderStatus.AwaitingPayment && order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} already authorized; returning existing hold.");
            return order;
        }

        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be paid because it is in state {order.Status}.");
        }

        string? vaultId = null;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdSpec(savedPaymentMethodId.Value, buyerId), ct)
                ?? throw new PaymentOperationException($"Saved card {savedPaymentMethodId} was not found for this shopper.");
            vaultId = savedCard.VaultId;
        }
        else if (card is null)
        {
            throw new PaymentOperationException("Provide either card details or the id of a saved card to pay with.");
        }

        var amount = new GatewayAmount(order.Total(), _currency);
        var reference = PaymentReference.For(orderId);
        var idempotencyKey = PaymentReference.IdempotencyKey("authorize", orderId);

        var auth = await _gateway.AuthorizeAsync(amount, card, vaultId, reference, idempotencyKey, ct);

        var payment = new OrderPayment(auth.ProviderOrderId, order.Total(), _currency, reference);
        payment.SetAuthorization(auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        order.AttachAuthorizedPayment(payment);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} authorized (authorization {auth.AuthorizationId}, status {auth.Status}).");
        return order;
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsAndPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent: already fulfilled -> nothing more to take.
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (!order.CanBeFulfilled || order.Payment is null || order.Payment.AuthorizationId is null)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be fulfilled because it is in state {order.Status}.");
        }

        var payment = order.Payment;
        var amount = new GatewayAmount(payment.Amount, _currency);
        var reference = payment.ProviderReference;
        var captureKey = PaymentReference.IdempotencyKey("capture", orderId);

        // A hold that has gone stale before fulfilment is renewed rather than failing the fulfilment.
        var authorizationId = payment.AuthorizationId!;
        if (IsStale(payment))
        {
            authorizationId = await RenewHoldAsync(order, amount, ct);
        }

        GatewayCaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, amount, reference, captureKey, ct);
        }
        catch (PaymentGatewayException ex) when (IndicatesStaleAuthorization(ex))
        {
            // The hold expired between our check and the capture; try to renew once, then capture.
            _logger.LogInformation($"Capture of order {orderId} reported a stale hold ({ex.Message}); attempting to renew.");
            authorizationId = await RenewHoldAsync(order, amount, ct);
            capture = await _gateway.CaptureAsync(authorizationId, amount, reference, captureKey, ct);
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation(
            $"Order {orderId} fulfilled; captured {Format(capture.GrossAmount)} {capture.CurrencyCode} " +
            $"(fee {Format(capture.PayPalFee ?? 0m)}, net {Format(capture.NetAmount ?? 0m)}), capture {capture.CaptureId}.");
        return order;
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsAndPaymentByIdSpec(orderId), ct)
            ?? throw new OrderNotFoundException(orderId);

        // Idempotent: already cancelled.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (!order.CanBeCancelled)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be cancelled because it is in state {order.Status}.");
        }

        // Release the hold so no money ever moved.
        if (order.Payment?.AuthorizationId is not null &&
            !string.Equals(order.Payment.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            await _gateway.VoidAsync(order.Payment.AuthorizationId, PaymentReference.IdempotencyKey("void", orderId), ct);
            order.Payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundOrderAsync(
        string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct)
            ?? throw new OrderNotFoundException(orderId);

        if (order.Payment?.CaptureId is null || !order.CanBeRefunded)
        {
            throw new PaymentOperationException($"Order {orderId} cannot be refunded because it is in state {order.Status}.");
        }

        var payment = order.Payment;

        // Idempotency: the same key must never refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} under key {idempotencyKey} already exists; returning it.");
            return (order, existing);
        }

        // A partly-refunded order must never become refundable beyond what was captured.
        var refundAmount = amount ?? payment.RefundableRemaining();
        if (refundAmount <= 0m)
        {
            throw new PaymentOperationException($"Order {orderId} has nothing left to refund.");
        }
        if (refundAmount > payment.RefundableRemaining())
        {
            throw new PaymentOperationException(
                $"Refund of {Format(refundAmount)} exceeds the refundable remaining amount of {Format(payment.RefundableRemaining())}.");
        }

        // Full refund is signalled to the provider by omitting the amount.
        var isFull = !amount.HasValue || refundAmount == payment.RefundableRemaining();
        var gatewayAmount = isFull && !amount.HasValue ? null : new GatewayAmount(refundAmount, _currency);

        // The app-level guard above already prevents a double refund under the same key. The
        // provider request id is additionally scoped to this capture (unique per run) so a caller
        // key reused across runs can never return a prior run's cached refund.
        var providerRequestId = $"refund-{payment.CaptureId}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId, gatewayAmount, payment.ProviderReference, providerRequestId, ct);

        var refund = payment.RecordRefund(result.RefundId, result.Amount, result.Status, idempotencyKey);
        order.ReflectRefundState();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} refunded {Format(result.Amount)} {result.CurrencyCode} (refund {result.RefundId}).");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpec(buyerId), ct);
        return orders;
    }

    public Task<Order?> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default)
        => LoadOwnedOrderAsync(buyerId, orderId, ct);

    private async Task<Order?> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsAndPaymentByIdSpec(orderId), ct);
        // One shopper must never see or act on another's order.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }
        return order;
    }

    private async Task<string> RenewHoldAsync(Order order, GatewayAmount amount, CancellationToken ct)
    {
        var payment = order.Payment!;
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, amount, ct);
            payment.SetAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Order {order.Id} hold renewed; new authorization {reauth.AuthorizationId}.");
            return reauth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentOperationException(
                $"The payment hold for order {order.Id} has expired and could not be renewed" +
                $"{(string.IsNullOrEmpty(ex.Message) ? "" : $" ({ex.Message})")}. " +
                $"Ask the shopper to place and pay for a new order to charge them" +
                $"{(string.IsNullOrEmpty(ex.DebugId) ? "" : $" [provider debug id: {ex.DebugId}]")}.");
        }
    }

    private static bool IsStale(OrderPayment payment)
        => payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;

    private static bool IndicatesStaleAuthorization(PaymentGatewayException ex)
        => ex.Issues.Any(i => i.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase))
           || ex.Message.Contains("expire", StringComparison.OrdinalIgnoreCase);

    private static string Format(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
