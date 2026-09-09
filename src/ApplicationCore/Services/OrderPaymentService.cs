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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Renew an authorization if it expires within this margin of fulfilment time.
    private static readonly TimeSpan StaleMargin = TimeSpan.FromMinutes(5);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IReadRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedCard> savedCardRepository,
        IReadRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines is null || lines.Count == 0)
            throw new PaymentOperationException("An order must contain at least one item.", PaymentOperationError.InvalidState);
        if (lines.Any(l => l.Quantity <= 0))
            throw new PaymentOperationException("Each order line must have a quantity of at least 1.", PaymentOperationError.InvalidState);

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), ct);

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new PaymentOperationException($"Catalog item {line.CatalogItemId} was not found.", PaymentOperationError.InvalidState);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        order = await _orderRepository.AddAsync(order, ct);

        var payment = new OrderPayment(order.Id, buyerId, _gateway.CurrencyCode, order.Total());
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId}; total {order.Total()} {_gateway.CurrencyCode}, awaiting payment.");
        return order.Id;
    }

    public async Task<OrderPayment> AuthorizeAsync(int orderId, string buyerId, CardDetails? card, int? savedCardId, CancellationToken ct = default)
    {
        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            throw new PaymentOperationException($"Order {orderId} was not found.", PaymentOperationError.NotFound);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentOperationException("This order belongs to another shopper.", PaymentOperationError.Forbidden);

        var payment = await GetPaymentOrThrow(orderId, ct);

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
                return payment; // idempotent: already held, do not authorize twice
            case PaymentStatus.Fulfilled:
            case PaymentStatus.Refunded:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Cancelled:
                throw new PaymentOperationException(
                    $"Order {orderId} cannot be paid because its payment is already {payment.Status}.",
                    PaymentOperationError.Conflict);
        }

        var hasCard = card is not null;
        var hasSaved = savedCardId.HasValue;
        if (hasCard == hasSaved)
            throw new PaymentOperationException(
                "Provide exactly one of: card details, or a saved-card id.", PaymentOperationError.InvalidState);

        string? vaultId = null;
        if (hasSaved)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdSpec(savedCardId!.Value, buyerId), ct);
            if (savedCard is null)
                throw new PaymentOperationException("Saved card was not found for this shopper.", PaymentOperationError.NotFound);
            vaultId = savedCard.PayPalVaultId;
        }

        var request = new AuthorizeCardRequest
        {
            OrderReference = orderId.ToString(),
            CurrencyCode = payment.CurrencyCode,
            Amount = payment.Amount,
            Description = $"eShopOnWeb order {orderId}",
            Card = card,
            VaultId = vaultId,
            IdempotencyKey = $"auth-{payment.IdempotencySeed}"
        };

        var result = await _gateway.AuthorizeAsync(request, ct);
        payment.MarkAuthorized(result.PayPalOrderId, result.AuthorizationId, result.Status, result.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Authorized order {orderId}: paypalOrder={result.PayPalOrderId} auth={result.AuthorizationId} status={result.Status}.");
        return payment;
    }

    public async Task<OrderPayment> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentOrThrow(orderId, ct);

        if (payment.Status == PaymentStatus.Fulfilled)
            return payment; // idempotent

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentOperationException(
                $"Order {orderId} is not in an authorized state ({payment.Status}); it cannot be fulfilled.",
                PaymentOperationError.InvalidState);

        var authorizationId = payment.AuthorizationId;
        var renewed = false;

        // A stale authorization must be renewed rather than failing the fulfilment outright.
        if (IsStale(payment.AuthorizationExpiresAt))
        {
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            renewed = true;
        }

        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.CurrencyCode, $"cap-{payment.IdempotencySeed}", ct);
        }
        catch (PaymentGatewayException ex) when (!renewed && IndicatesExpiredAuthorization(ex))
        {
            // Authorization went stale despite our timestamp — renew once, then capture again.
            authorizationId = await RenewAuthorizationAsync(payment, ct);
            capture = await _gateway.CaptureAsync(authorizationId, payment.Amount, payment.CurrencyCode, $"cap-{payment.IdempotencySeed}", ct);
        }

        payment.MarkFulfilled(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Fulfilled order {orderId}: capture={capture.CaptureId} captured={capture.CapturedAmount} fee={capture.PayPalFee} net={capture.NetAmount}.");
        return payment;
    }

    public async Task<OrderPayment> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await GetPaymentOrThrow(orderId, ct);

        if (payment.Status == PaymentStatus.Cancelled)
            return payment; // idempotent

        if (payment.Status == PaymentStatus.Fulfilled || payment.Status == PaymentStatus.Refunded || payment.Status == PaymentStatus.PartiallyRefunded)
            throw new PaymentOperationException(
                $"Order {orderId} is already {payment.Status}; a captured order is returned via a refund, not a cancellation.",
                PaymentOperationError.Conflict);

        if (payment.Status != PaymentStatus.Authorized || payment.AuthorizationId is null)
            throw new PaymentOperationException(
                $"Order {orderId} has no held funds to release ({payment.Status}).",
                PaymentOperationError.InvalidState);

        await _gateway.VoidAsync(payment.AuthorizationId, $"void-{payment.IdempotencySeed}", ct);
        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Cancelled order {orderId}: authorization {payment.AuthorizationId} voided, funds released.");
        return payment;
    }

    public async Task<(OrderPayment Payment, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.GetByIdAsync(orderId, ct);
        if (order is null)
            throw new PaymentOperationException($"Order {orderId} was not found.", PaymentOperationError.NotFound);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw new PaymentOperationException("This order belongs to another shopper.", PaymentOperationError.Forbidden);

        var payment = await GetPaymentOrThrow(orderId, ct);

        // Idempotent: a repeat request under the same key returns the recorded refund, no second PayPal call.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing is not null)
            return (payment, existing);

        if (payment.Status is not (PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded) || payment.CaptureId is null)
            throw new PaymentOperationException(
                $"Order {orderId} has no captured payment to refund ({payment.Status}).",
                PaymentOperationError.InvalidState);

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (!payment.CanRefund(refundAmount))
            throw new PaymentOperationException(
                $"Refund of {refundAmount} {payment.CurrencyCode} exceeds the refundable remaining {payment.RefundableRemaining} {payment.CurrencyCode}.",
                PaymentOperationError.InvalidState);

        // The app-side guard above already dedupes by the raw caller key per payment; the value sent to
        // PayPal as PayPal-Request-Id is namespaced per payment so the same caller key is still globally
        // unique across captures (PayPal rejects a reused PayPal-Request-Id with DUPLICATE_REQUEST_ID).
        var payPalRequestId = $"rf-{payment.IdempotencySeed}-{idempotencyKey}";
        var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.CurrencyCode, payPalRequestId, ct);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId, refundAmount, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Refunded order {orderId}: refund={result.RefundId} amount={refundAmount} status={result.Status}; total refunded {payment.TotalRefunded}/{payment.CapturedAmount}.");
        return (payment, refund);
    }

    public async Task<IReadOnlyList<(Order Order, OrderPayment? Payment)>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);
        var byOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => (o, byOrderId.TryGetValue(o.Id, out var p) ? p : null))
            .ToList();
    }

    private async Task<OrderPayment> GetPaymentOrThrow(int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        if (payment is null)
            throw new PaymentOperationException($"No payment exists for order {orderId}.", PaymentOperationError.NotFound);
        return payment;
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var reauth = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.CurrencyCode, ct);
            payment.RenewAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Renewed stale authorization for order {payment.OrderId}: new auth={reauth.AuthorizationId}.");
            return reauth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentGatewayException(
                $"The authorization for order {payment.OrderId} has expired and can no longer be renewed ({ex.Message}). " +
                "A new order must be placed and authorized before it can be fulfilled.",
                PaymentGatewayErrorKind.CannotReauthorize, ex.StatusCode, ex.DebugId, ex, ex.Issue);
        }
    }

    private static bool IsStale(DateTimeOffset? expiresAt) =>
        expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow.Add(StaleMargin);

    private static bool IndicatesExpiredAuthorization(PaymentGatewayException ex) =>
        (ex.Issue?.IndexOf("EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0)
        || (ex.Message.IndexOf("AUTHORIZATION_EXPIRED", StringComparison.OrdinalIgnoreCase) >= 0);
}
