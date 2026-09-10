using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.SavedCardAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Stable for the life of the process, fresh on every restart. Prefixed onto the PayPal-Request-Id
    // for each operation so a double-click within one run dedupes at PayPal, while the same logical
    // operation across restarts (in-memory order ids restart at 1) does not collide with PayPal's
    // 45-day request-id memory.
    private static readonly string RunId = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IPayPalClient _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IPayPalClient payPal,
        IUriComposer uriComposer,
        PayPalSettings settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _settings = settings;
        _logger = logger;
    }

    private string Currency => _settings.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
        {
            throw new ArgumentException("An order must contain at least one line item.");
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new ArgumentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new NotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            // Price comes from the catalog; snapshot the product so later catalog edits don't mutate the order.
            var pictureUri = string.IsNullOrEmpty(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Order {0} placed by {1} awaiting payment, total {2} {3}.", order.Id, buyerId, order.Total(), Currency);
        return order.Id;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId, CancellationToken ct = default)
    {
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        // Idempotent in effect: a second click after a successful hold must not authorize again.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment is not null)
        {
            _logger.LogInformation("Order {0} is already authorized; returning existing hold.", orderId);
            return order;
        }
        if (!order.CanBeAuthorized())
        {
            throw new InvalidOperationException($"Order {orderId} cannot be paid in status {order.Status}.");
        }

        var amount = order.Total();
        if (amount <= 0)
        {
            throw new InvalidOperationException($"Order {orderId} has no payable amount.");
        }

        // Globally-unique correlation token stamped onto the PayPal order as custom_id, so
        // reconciliation can line PayPal's report up against this exact order across runs.
        var customId = $"eshop-{RunId}-order-{order.Id}";
        // Stable per (run, order, operation) so a double-click dedupes at PayPal too (PayPal-Request-Id).
        var idempotencyKey = $"eshop-{RunId}-auth-order-{order.Id}";

        AuthorizationResult result;
        if (savedCardId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedCardByIdForBuyerSpecification(savedCardId.Value, buyerId), ct)
                ?? throw new NotFoundException($"Saved card {savedCardId.Value} was not found.");
            result = await _payPal.AuthorizeOrderWithVaultedCardAsync(amount, Currency, savedCard.PayPalVaultId, customId, idempotencyKey, ct);
        }
        else if (card is not null)
        {
            result = await _payPal.AuthorizeOrderWithCardAsync(amount, Currency, card, customId, idempotencyKey, ct);
        }
        else
        {
            throw new ArgumentException("Provide either card details or a saved card id to pay.");
        }

        var payment = new Payment(result.PayPalOrderId, customId, result.AuthorizationId, result.Status, result.ExpiresAt, amount, Currency);
        order.SetAuthorizedPayment(payment);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {0} authorized: PayPal order {1}, authorization {2} for {3} {4}.",
            orderId, result.PayPalOrderId, result.AuthorizationId, amount, Currency);
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled)
        {
            _logger.LogInformation("Order {0} is already fulfilled; returning existing capture.", orderId);
            return order;
        }
        if (!order.CanBeFulfilled())
        {
            throw new InvalidOperationException($"Order {orderId} cannot be fulfilled in status {order.Status}.");
        }

        var payment = order.Payment!;
        var amount = payment.Amount;

        // An authorization that has gone stale must be renewed rather than failing the fulfilment.
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiry && expiry <= DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Order {0} authorization {1} expired at {2}; reauthorizing before capture.",
                orderId, payment.AuthorizationId, expiry);
            await RenewAuthorizationAsync(order, payment, amount, ct);
        }

        CaptureResult capture;
        try
        {
            capture = await CaptureAsync(payment, amount, ct);
        }
        catch (PayPalApiException ex) when (IsAuthorizationExpired(ex))
        {
            // Renew and retry once — the hold went stale between our check and the capture.
            _logger.LogWarning("Order {0} capture reported an expired authorization; reauthorizing and retrying.", orderId);
            await RenewAuthorizationAsync(order, payment, amount, ct);
            capture = await CaptureAsync(payment, amount, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {0} fulfilled: capture {1}, gross {2}, fee {3}, net {4} {5}.",
            orderId, capture.CaptureId, capture.GrossAmount, capture.PayPalFee, capture.NetAmount, Currency);
        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("Order {0} is already cancelled.", orderId);
            return order;
        }
        if (!order.CanBeCancelled())
        {
            throw new InvalidOperationException($"Order {orderId} cannot be cancelled in status {order.Status}.");
        }

        var payment = order.Payment;
        if (payment is not null && payment.Status == PaymentStatus.Authorized)
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
            payment.MarkVoided();
            _logger.LogInformation("Order {0} authorization {1} voided; held funds released.", orderId, payment.AuthorizationId);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        return order;
    }

    public async Task<PaymentRefund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (!order.CanBeRefunded())
        {
            throw new InvalidOperationException($"Order {orderId} cannot be refunded in status {order.Status} (only fulfilled orders can be refunded).");
        }

        var payment = order.Payment!;

        // Idempotent: repeating a request under the same key must not refund twice.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("Order {0} refund with key {1} already processed as {2}; returning it.", orderId, idempotencyKey, existing.PayPalRefundId);
            return existing;
        }

        var remaining = payment.RefundableRemaining();
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new InvalidOperationException($"Order {orderId} has nothing left to refund.");
        }
        // A partly-refunded order must never become refundable beyond what was captured.
        if (refundAmount > remaining)
        {
            throw new InvalidOperationException(
                $"Refund of {refundAmount} {Currency} exceeds the refundable remaining of {remaining} {Currency} for order {orderId}.");
        }

        // Compose the PayPal-Request-Id from the (globally unique) capture id and the caller's key so
        // it is unique across runs yet deterministic for a retry of the same key against this capture.
        var payPalRequestId = $"eshop-{RunId}-refund-cap-{payment.CaptureId}-{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, Currency, payPalRequestId, ct);
        var refund = payment.AddRefund(result.RefundId, refundAmount, result.Status, idempotencyKey);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Order {0} refunded {1} {2}: refund {3}. Remaining refundable {4}.",
            orderId, refundAmount, Currency, result.RefundId, payment.RefundableRemaining());
        return refund;
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct);
        // One shopper must never see or act on another's order — report not found, don't leak.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<CaptureResult> CaptureAsync(Payment payment, decimal amount, CancellationToken ct)
    {
        // Stable per authorization so a double capture of the same hold dedupes at PayPal.
        var idempotencyKey = $"eshop-{RunId}-capture-auth-{payment.AuthorizationId}";
        return await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, amount, Currency, idempotencyKey, ct);
    }

    private async Task RenewAuthorizationAsync(Order order, Payment payment, decimal amount, CancellationToken ct)
    {
        try
        {
            var reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId, amount, Currency, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Order {0} authorization renewed as {1}.", order.Id, reauth.AuthorizationId);
        }
        catch (PayPalApiException ex)
        {
            // An authorization that can no longer be renewed must say so in operator terms.
            throw new AuthorizationUnrenewableException(
                $"The authorization for order {order.Id} can no longer be renewed ({ex.Issue ?? ex.Message}). " +
                "Ask the shopper to place and pay for a new order, or cancel this one.");
        }
    }

    private static bool IsAuthorizationExpired(PayPalApiException ex) =>
        string.Equals(ex.Issue, "AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase);
}
