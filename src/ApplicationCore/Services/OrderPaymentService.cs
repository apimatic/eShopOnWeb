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
using Microsoft.eShopWeb.ApplicationCore.Paypal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _payPal;
    private readonly PayPalOptions _options;
    private readonly IAppLogger<OrderPaymentService> _logger;

    // A hold is treated as stale if it expires within this window, so we renew proactively.
    private static readonly TimeSpan AuthorizationStaleBuffer = TimeSpan.FromMinutes(5);

    // Stable for the lifetime of the process, unique across restarts. Order ids reset every run under
    // the in-memory store, so this keeps the PayPal-Request-Id / invoice id stable within a run (a
    // double-click reuses it and does not authorize twice) yet unique across runs (no stale collisions).
    private static readonly string InstanceNonce = Guid.NewGuid().ToString("N").Substring(0, 12);

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Buyer> buyerRepository,
        IRepository<CatalogItem> itemRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway payPal,
        PayPalOptions options,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _buyerRepository = buyerRepository;
        _itemRepository = itemRepository;
        _uriComposer = uriComposer;
        _payPal = payPal;
        _options = options;
        _logger = logger;
    }

    private string Currency => _options.Currency!;

    public async Task<Order> PlaceOrderAsync(string buyerId, PlaceOrderInput input, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(input, nameof(input));
        if (input.Items is null || input.Items.Count == 0)
            throw new ArgumentException("An order must contain at least one item.", nameof(input));

        // Collapse duplicate lines and reject non-positive quantities.
        var lines = input.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineInput(g.Key, g.Sum(i => i.Quantity)))
            .ToList();
        if (lines.Any(l => l.Quantity <= 0))
            throw new ArgumentException("Item quantities must be greater than zero.", nameof(input));

        var catalogItems = await _itemRepository.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), ct);

        var missing = lines.Select(l => l.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Any())
            throw new ArgumentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.", nameof(input));

        // Amounts always come from catalog prices, never from the caller.
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, ToAddress(input.ShipTo), orderItems);
        await _orderRepository.AddAsync(order, ct);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId} awaiting payment, total {order.Total()} {Currency}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PayOrderInput input, CancellationToken ct = default)
    {
        Guard.Against.Null(input, nameof(input));
        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);

        // Idempotent in effect: a double-click on an already-authorized order does not authorize again.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            _logger.LogInformation($"Order {orderId} already authorized; returning existing hold.");
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidOrderOperationException($"Order {orderId} is {order.Status} and cannot be paid.");

        var (card, vaultId) = await ResolvePaymentSourceAsync(buyerId, input, ct);

        // Globally-unique custom_id (order id is only unique within a run under the in-memory store),
        // so reconciliation lines transactions up to the right order without cross-run collisions.
        var customId = $"{InstanceNonce}-{order.Id}";
        var request = new PayPalAuthorizationRequest
        {
            ReferenceId = order.Id.ToString(),
            CustomId = customId,
            Amount = order.Total(),
            CurrencyCode = Currency,
            IdempotencyKey = $"{InstanceNonce}-auth-order-{order.Id}",
            Card = card,
            VaultId = vaultId
        };

        var result = await _payPal.AuthorizeOrderAsync(request, ct);

        order.AuthorizePayment(result.PayPalOrderId, Currency, result.AuthorizationId, result.Status,
            result.ExpiresAt, result.InstrumentSummary, result.VaultId ?? vaultId, customId);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Authorized order {order.Id}: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status == OrderStatus.Fulfilled)
            return order; // idempotent
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            throw new InvalidOrderOperationException($"Order {orderId} is {order.Status} and cannot be fulfilled.");

        var payment = order.Payment;

        // Renew proactively if the hold is stale (or about to be), so fulfilment does not fail outright.
        if (IsAuthorizationStale(payment))
        {
            _logger.LogInformation($"Authorization {payment.AuthorizationId} on order {orderId} is stale; renewing before capture.");
            await RenewAuthorizationAsync(order, ct);
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(
                order.Payment!.AuthorizationId!, order.Total(), Currency,
                $"capture-{order.Payment!.AuthorizationId}", ct);
        }
        catch (PayPalApiException ex) when (IsExpiredAuthorization(ex))
        {
            // The hold went stale between our check and the capture; renew once and retry.
            _logger.LogInformation($"Capture of order {orderId} failed as expired ({ex.Name}); renewing and retrying.");
            await RenewAuthorizationAsync(order, ct);
            capture = await _payPal.CaptureAuthorizationAsync(
                order.Payment!.AuthorizationId!, order.Total(), Currency,
                $"capture-{order.Payment!.AuthorizationId}", ct);
        }

        order.Fulfil(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Fulfilled order {order.Id}: capture={capture.CaptureId} amount={capture.Amount} " +
            $"fee={capture.PayPalFee} net={capture.NetAmount} {capture.CurrencyCode}.");
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);
        if (order.Status == OrderStatus.Cancelled)
            return order; // idempotent
        if (order.Status == OrderStatus.Fulfilled)
            throw new InvalidOrderOperationException($"Order {orderId} is fulfilled; issue a refund instead of cancelling.");

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null)
        {
            await _payPal.VoidAuthorizationAsync(order.Payment.AuthorizationId, ct);
            _logger.LogInformation($"Voided authorization {order.Payment.AuthorizationId} for order {orderId}.");
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(string buyerId, int orderId,
        RefundOrderInput input, CancellationToken ct = default)
    {
        Guard.Against.Null(input, nameof(input));
        Guard.Against.NullOrEmpty(input.IdempotencyKey, nameof(input.IdempotencyKey));

        var order = await LoadOwnedOrderAsync(buyerId, orderId, ct);
        if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId is null)
            throw new InvalidOrderOperationException($"Order {orderId} is {order.Status} and cannot be refunded.");

        // Repeating a request under the same key must not refund twice.
        var existing = order.Payment.FindRefundByIdempotencyKey(input.IdempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation($"Refund for order {orderId} under key '{input.IdempotencyKey}' already exists ({existing.PayPalRefundId}).");
            return (order, existing);
        }

        var amount = input.Amount ?? order.Payment.RefundableRemaining;
        if (amount <= 0)
            throw new InvalidOrderOperationException("Refund amount must be greater than zero.");
        if (amount > order.Payment.RefundableRemaining)
            throw new InvalidOrderOperationException(
                $"Refund of {amount} exceeds the refundable remaining amount of {order.Payment.RefundableRemaining}.");

        var result = await _payPal.RefundCaptureAsync(order.Payment.CaptureId, amount, Currency, input.IdempotencyKey, ct);
        var refund = order.Refund(result.RefundId, result.Amount, result.Status ?? "COMPLETED", input.IdempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Refunded {result.Amount} {result.CurrencyCode} on order {order.Id}: refund={result.RefundId}.");
        return (order, refund);
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders;
    }

    public Task<Order> GetOrderForBuyerAsync(string buyerId, int orderId, CancellationToken ct = default)
        => LoadOwnedOrderAsync(buyerId, orderId, ct);

    // ---- helpers ----

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), ct);
        if (order is null) throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(string buyerId, int orderId, CancellationToken ct)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpecification(orderId), ct);
        // Not-found and not-yours are indistinguishable to the caller.
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new OrderNotFoundException(orderId);
        return order;
    }

    private async Task<(PayPalCardDetails? Card, string? VaultId)> ResolvePaymentSourceAsync(
        string buyerId, PayOrderInput input, CancellationToken ct)
    {
        if (input.SavedPaymentMethodId is int savedId)
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
            var method = buyer?.FindPaymentMethod(savedId);
            if (method is null)
                throw new PaymentMethodNotFoundException(savedId);
            return (null, method.PayPalVaultId);
        }

        if (input.Card is not null)
            return (input.Card, null);

        throw new ArgumentException("A payment must supply either card details or a saved card id.", nameof(input));
    }

    private bool IsAuthorizationStale(OrderPayment payment)
        => payment.AuthorizationExpiresAt.HasValue &&
           payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow + AuthorizationStaleBuffer;

    private static bool IsExpiredAuthorization(PayPalApiException ex)
        => ex.StatusCode == 422 &&
           (string.Equals(ex.Name, "UNPROCESSABLE_ENTITY", StringComparison.OrdinalIgnoreCase) ||
            (ex.Message?.Contains("EXPIRED", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (ex.Message?.Contains("AUTH_CAPTURE", StringComparison.OrdinalIgnoreCase) ?? false));

    private async Task RenewAuthorizationAsync(Order order, CancellationToken ct)
    {
        var authId = order.Payment!.AuthorizationId!;
        try
        {
            var renewed = await _payPal.ReauthorizeAsync(authId, order.Total(), Currency, ct);
            order.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation($"Renewed authorization for order {order.Id}: {authId} -> {renewed.AuthorizationId}.");
        }
        catch (AuthorizationNotRenewableException ex)
        {
            _logger.LogWarning($"Authorization {authId} on order {order.Id} could not be renewed: {ex.Message}");
            throw;
        }
    }

    private static Address ToAddress(ShippingAddressInput? shipTo)
    {
        // Shipping is not the focus of this task; when the caller omits it we record a clear placeholder
        // so the (required) address value object is still satisfied.
        if (shipTo is null)
            return new Address("N/A", "N/A", "N/A", "N/A", "00000");
        return new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);
    }
}
