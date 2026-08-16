using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentMethodAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Coordinates the order/payment state machine with the payment gateway. All money-moving operations
/// are serialized per order (an in-process keyed lock) and guarded by the order's own state, so a
/// double-click can never authorize or capture the shopper twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const string Provider = "PayPal";

    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _orderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPaymentGateway paymentGateway,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public string Currency => _paymentGateway.Currency;

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines,
        ShippingAddressRequest? shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new OrderPaymentException("An order must contain at least one line item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new OrderPaymentException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var itemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new OrderPaymentException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shipTo is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(shipTo.Street, shipTo.City, shipTo.State, shipTo.Country, shipTo.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation($"Order {order.Id} placed by {buyerId} awaiting payment, total {order.Total():0.00}.");
        return order.Id;
    }

    public async Task<Order> AuthorizeOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedCardId,
        CancellationToken cancellationToken = default)
    {
        if ((card is null) == (savedCardId is null))
        {
            throw new OrderPaymentException("Provide exactly one of card details or a saved card id to pay with.");
        }

        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId, callerIsAdmin: false);

            // Idempotent in effect: an already-authorized order is returned unchanged.
            if (order.Status == OrderStatus.PaymentAuthorized)
            {
                return order;
            }
            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new OrderPaymentException($"Order {orderId} is {order.Status} and can no longer be paid.");
            }

            var amount = order.Total();
            var reference = BuildReference(orderId);

            GatewayAuthorizationResult auth;
            string description;

            if (savedCardId is not null)
            {
                var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdForBuyerSpecification(buyerId, savedCardId.Value), cancellationToken);
                if (savedCard is null)
                {
                    throw new OrderPaymentException($"Saved card {savedCardId} was not found for this shopper.");
                }

                var key = $"auth-{orderId}-vault-{savedCard.PayPalVaultId}";
                auth = await _paymentGateway.AuthorizeWithVaultedCardAsync(reference, amount, savedCard.PayPalVaultId, key, cancellationToken);
                description = savedCard.DisplayLabel;
            }
            else
            {
                var key = $"auth-{orderId}-card-{ShortHash(card!.Number + card.ExpiryYearMonth)}";
                auth = await _paymentGateway.AuthorizeWithCardAsync(reference, amount, card!, key, cancellationToken);
                description = DescribeCard(auth.CardBrand, auth.CardLast4, card!.Number);
            }

            var payment = new OrderPayment(Provider, _paymentGateway.Currency, amount, reference, description);
            payment.RecordAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
            order.AuthorizePayment(payment);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {orderId} authorized: paypalOrder={auth.PayPalOrderId}, auth={auth.AuthorizationId}, amount={amount:0.00} {_paymentGateway.Currency}.");
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            // Idempotent: an already-fulfilled order is returned unchanged (money already captured once).
            if (order.Status == OrderStatus.Fulfilled)
            {
                return order;
            }
            if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null)
            {
                throw new OrderPaymentException($"Order {orderId} is {order.Status} and cannot be fulfilled; only an authorized order can be captured.");
            }

            var payment = order.Payment;
            var amount = payment.Amount;
            var authorizationId = payment.AuthorizationId!;

            // Renew a hold that has gone stale before capture, rather than failing outright.
            if (IsAuthorizationStale(payment))
            {
                authorizationId = await RenewAuthorizationAsync(payment, amount, cancellationToken);
            }

            GatewayCaptureResult capture;
            try
            {
                capture = await _paymentGateway.CaptureAsync(authorizationId, amount, $"capture-{authorizationId}", cancellationToken);
            }
            catch (PaymentGatewayException ex) when (IsExpiredAuthorization(ex))
            {
                // Stale even though it looked fresh — renew once and retry the capture.
                _logger.LogWarning($"Capture of order {orderId} reported an expired hold ({ex.Issue}); renewing and retrying.");
                authorizationId = await RenewAuthorizationAsync(payment, amount, cancellationToken);
                capture = await _paymentGateway.CaptureAsync(authorizationId, amount, $"capture-{authorizationId}", cancellationToken);
            }

            payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            order.MarkFulfilled();

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {orderId} fulfilled: capture={capture.CaptureId}, gross={capture.GrossAmount:0.00}, fee={capture.PayPalFee:0.00}, net={capture.NetAmount:0.00}.");
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return order; // idempotent
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new OrderPaymentException($"Order {orderId} has been fulfilled and cannot be cancelled; refund it instead.");
            }

            // Release any held funds before cancelling.
            if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null
                && order.Payment.Status == PaymentStatus.Authorized)
            {
                try
                {
                    await _paymentGateway.VoidAsync(order.Payment.AuthorizationId!, $"void-{order.Payment.AuthorizationId}", cancellationToken);
                    order.Payment.MarkVoided();
                }
                catch (PaymentGatewayException ex)
                {
                    _logger.LogWarning($"Void of order {orderId} hold returned {ex.Issue ?? ex.Message}; treating hold as released.");
                    order.Payment.MarkVoided();
                }
            }

            order.Cancel();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundOutcome> RefundOrderAsync(string callerBuyerId, bool callerIsAdmin, int orderId,
        decimal? amount, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var gate = await LockAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, callerBuyerId, callerIsAdmin);

            if (order.Status != OrderStatus.Fulfilled || order.Payment?.CaptureId is null)
            {
                throw new OrderPaymentException($"Order {orderId} has not been captured, so there is nothing to refund.");
            }

            var payment = order.Payment;

            // Idempotent: the same key never refunds twice.
            var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
            if (existing is not null)
            {
                return new RefundOutcome(existing.RefundId, existing.Amount, existing.Status,
                    payment.TotalRefunded, payment.RefundableRemaining, AlreadyProcessed: true);
            }

            if (amount is not null)
            {
                if (amount.Value <= 0m)
                {
                    throw new OrderPaymentException("A partial refund amount must be greater than zero.");
                }
                if (amount.Value > payment.RefundableRemaining + 0.0001m)
                {
                    throw new OrderPaymentException(
                        $"Refund of {amount.Value:0.00} {payment.Currency} exceeds the refundable remaining of {payment.RefundableRemaining:0.00} {payment.Currency}.");
                }
            }

            var result = await _paymentGateway.RefundAsync(payment.CaptureId!, amount, idempotencyKey, cancellationToken);
            var refund = payment.AddRefund(result.RefundId, result.GrossAmount, result.Status, idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {orderId} refunded {refund.Amount:0.00} {payment.Currency} (refund={refund.RefundId}); total refunded {payment.TotalRefunded:0.00}.");

            return new RefundOutcome(refund.RefundId, refund.Amount, refund.Status,
                payment.TotalRefunded, payment.RefundableRemaining, AlreadyProcessed: false);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order?> GetOrderForCallerAsync(int orderId, string callerBuyerId, bool callerIsAdmin,
        CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsAndPaymentSpecification(orderId), cancellationToken);
        if (order is null)
        {
            return null;
        }
        if (!callerIsAdmin && order.BuyerId != callerBuyerId)
        {
            // Do not reveal another shopper's order even by existence.
            return null;
        }
        return order;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        return orders.OrderByDescending(o => o.OrderDate).ToList();
    }

    // --- helpers ---

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderByIdWithItemsAndPaymentSpecification(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderPaymentException($"Order {orderId} was not found.");
        }
        return order;
    }

    private static void EnsureOwner(Order order, string callerBuyerId, bool callerIsAdmin)
    {
        if (!callerIsAdmin && order.BuyerId != callerBuyerId)
        {
            // Surface as not-found so ownership is never leaked.
            throw new OrderPaymentException($"Order {order.Id} was not found.");
        }
    }

    private bool IsAuthorizationStale(OrderPayment payment)
    {
        if (payment.AuthorizationExpiresAt is null)
        {
            return false;
        }
        // Renew slightly ahead of the real expiry to avoid racing PayPal's clock.
        return payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow.AddMinutes(1);
    }

    private async Task<string> RenewAuthorizationAsync(OrderPayment payment, decimal amount, CancellationToken cancellationToken)
    {
        try
        {
            var key = $"reauth-{payment.PaymentReference}-{payment.AuthorizationId}";
            var reauth = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, amount, key, cancellationToken);
            payment.RecordReauthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
            _logger.LogInformation($"Renewed hold for {payment.PaymentReference}: new auth {reauth.AuthorizationId} expiring {reauth.ExpiresAt:o}.");
            return reauth.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new OrderPaymentException(
                "The payment hold has expired and can no longer be renewed. Ask the shopper to authorize the order again " +
                $"before it can be fulfilled (PayPal: {ex.Issue ?? ex.Message}).", ex);
        }
    }

    private static bool IsExpiredAuthorization(PaymentGatewayException ex)
    {
        var issue = ex.Issue?.ToUpperInvariant() ?? string.Empty;
        var message = ex.Message.ToUpperInvariant();
        return issue.Contains("AUTHORIZATION_EXPIRED")
            || issue.Contains("AUTH_CAPTURE")
            || issue.Contains("EXPIRED")
            || message.Contains("EXPIRED");
    }

    private static string BuildReference(int orderId) => $"ESHOP-{orderId}-{Guid.NewGuid():N}";

    private static string DescribeCard(string? brand, string? last4, string number)
    {
        var safeBrand = string.IsNullOrWhiteSpace(brand) ? "Card" : brand;
        var safeLast4 = !string.IsNullOrWhiteSpace(last4)
            ? last4
            : (number.Length >= 4 ? number.Substring(number.Length - 4) : "****");
        return $"{safeBrand} ending {safeLast4}";
    }

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 8).ToLowerInvariant();
    }

    private static async Task<SemaphoreSlim> LockAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _orderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return gate;
    }
}
