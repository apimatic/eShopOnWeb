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

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly KeyedAsyncLock _locks;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalGateway payPal,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger,
        KeyedAsyncLock locks)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _uriComposer = uriComposer;
        _logger = logger;
        _locks = locks;
    }

    private string Currency => _payPal.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentException("Every order line must have a quantity of at least 1.");
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var missing = ids.Where(id => catalogItems.All(c => c.Id != id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ResourceNotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation("Placed order {OrderId} for {Buyer} totalling {Total}", order.Id, buyerId, order.Total());
        return order;
    }

    public async Task<Order> AuthorizeAsync(int orderId, string buyerId, PaymentInstrument instrument, CancellationToken ct = default)
    {
        using var _ = await _locks.LockAsync(OrderKey(orderId), ct);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.AuthorizationId is not null)
        {
            return order; // already held — idempotent
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        var payment = order.StartPayment(Currency);
        var amount = PayPalMoney.FromDecimal(order.Total(), Currency);

        try
        {
            PayPalAuthorizationResult result = instrument switch
            {
                CardPaymentInstrument card =>
                    await _payPal.AuthorizeWithCardAsync(amount, card.Card, payment.AuthorizationIdempotencyKey, ct),
                SavedCardPaymentInstrument saved =>
                    await _payPal.AuthorizeWithVaultedCardAsync(amount, await ResolveVaultIdAsync(buyerId, saved.PaymentMethodId, ct), payment.AuthorizationIdempotencyKey, ct),
                _ => throw new PaymentException("Unsupported payment instrument.")
            };

            payment.SetAuthorization(result.PayPalOrderId ?? string.Empty, result.AuthorizationId, result.Status, result.ExpiresAt);
            order.MarkAuthorized();
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogInformation("Authorized order {OrderId}: auth {AuthId} status {Status}", orderId, result.AuthorizationId, result.Status);
            return order;
        }
        catch (PayPalApiException ex)
        {
            // No hold was placed — rotate the idempotency key so a genuine retry is a fresh attempt.
            payment.RotateAuthorizationKey();
            await _orderRepository.UpdateAsync(order, ct);
            _logger.LogWarning("Authorization failed for order {OrderId}: {Message}", orderId, ex.Message);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.LockAsync(OrderKey(orderId), ct);

        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Fulfilled ||
            order.Status == OrderStatus.PartiallyRefunded ||
            order.Status == OrderStatus.Refunded)
        {
            return order; // already captured — idempotent
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment?.AuthorizationId is null)
        {
            throw new PaymentException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;
        var amount = PayPalMoney.FromDecimal(order.Total(), Currency);
        var captureKey = payment.EnsureCaptureKey();
        await _orderRepository.UpdateAsync(order, ct); // persist capture key before calling out

        PayPalCaptureResult capture;
        try
        {
            capture = await _payPal.CaptureAuthorizationAsync(payment.AuthorizationId, amount, captureKey, ct);
        }
        catch (PayPalApiException ex) when (ex.IsAuthorizationExpired)
        {
            _logger.LogWarning("Authorization {AuthId} for order {OrderId} is stale; attempting reauthorization.", payment.AuthorizationId, orderId);
            capture = await ReauthorizeAndCaptureAsync(order, payment, amount, captureKey, ct);
        }
        catch (PayPalApiException ex) when (ex.IsAuthorizationUnusable)
        {
            throw new PaymentException(
                $"Order {orderId} cannot be fulfilled: its PayPal authorization can no longer be used ({ex.IssueName ?? "unusable"}). " +
                $"The shopper must place and pay for a new order.");
        }

        payment.SetCapture(capture.CaptureId, capture.Status, capture.Gross, capture.PayPalFee, capture.Net);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Fulfilled order {OrderId}: capture {CaptureId} gross {Gross} fee {Fee} net {Net}",
            orderId, capture.CaptureId, capture.Gross, capture.PayPalFee, capture.Net);
        return order;
    }

    private async Task<PayPalCaptureResult> ReauthorizeAndCaptureAsync(Order order, OrderPayment payment, PayPalMoney amount, string captureKey, CancellationToken ct)
    {
        PayPalAuthorizationResult reauth;
        try
        {
            reauth = await _payPal.ReauthorizeAsync(payment.AuthorizationId!, amount, $"{captureKey}-reauth", ct);
        }
        catch (PayPalApiException ex)
        {
            throw new PaymentException(
                $"Order {order.Id} cannot be fulfilled: the authorization has expired and could not be renewed ({ex.IssueName ?? ex.StatusCode.ToString()}). " +
                $"It is beyond PayPal's re-authorization window; the shopper must place and pay for a new order.");
        }

        payment.ReplaceAuthorization(reauth.AuthorizationId, reauth.Status, reauth.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);

        // Fresh authorization → fresh capture idempotency key.
        return await _payPal.CaptureAuthorizationAsync(reauth.AuthorizationId, amount, $"{captureKey}-2", ct);
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        using var _ = await _locks.LockAsync(OrderKey(orderId), ct);

        var order = await LoadOrderAsync(orderId, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentException($"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        var payment = order.Payment;
        if (payment?.AuthorizationId is not null &&
            !string.Equals(payment.AuthorizationStatus, "VOIDED", System.StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(payment.AuthorizationStatus, "CAPTURED", System.StringComparison.OrdinalIgnoreCase))
        {
            await _payPal.VoidAuthorizationAsync(payment.AuthorizationId, ct);
            payment.MarkAuthorizationVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Cancelled order {OrderId}; any held funds released.", orderId);
        return order;
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundAsync(int orderId, string buyerId, string idempotencyKey, decimal? amount, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        using var _ = await _locks.LockAsync(OrderKey(orderId), ct);

        var order = await LoadOwnedOrderAsync(orderId, buyerId, ct);

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
        {
            throw new PaymentException($"Order {orderId} cannot be refunded because it is {order.Status}; only fulfilled orders can be refunded.");
        }
        var payment = order.Payment;
        if (payment?.CaptureId is null)
        {
            throw new PaymentException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotent replay: same key → same refund, never a second refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return (order, existing);
        }

        var remaining = payment.RemainingRefundable();
        if (remaining <= 0m)
        {
            throw new PaymentException($"Order {orderId} is already fully refunded.");
        }

        PayPalMoney? refundMoney = null;
        if (amount is not null)
        {
            if (amount <= 0m)
            {
                throw new PaymentException("Refund amount must be greater than zero.");
            }
            if (amount > remaining)
            {
                throw new PaymentException(
                    $"Refund of {amount:0.00} {Currency} exceeds the remaining refundable amount of {remaining:0.00} {Currency}.");
            }
            refundMoney = PayPalMoney.FromDecimal(amount.Value, Currency);
        }

        // Local dedup above already guarantees "same key never refunds twice". The
        // PayPal-Request-Id is defence-in-depth; scope it by capture so the same caller key
        // used against two different captures does not collide in PayPal's global id space.
        var payPalRequestId = $"{payment.CaptureId}:{idempotencyKey}";
        var result = await _payPal.RefundCaptureAsync(payment.CaptureId, refundMoney, payPalRequestId, ct);
        var refunded = result.Amount > 0m ? result.Amount : (amount ?? remaining);

        var refund = payment.AddRefund(idempotencyKey, refunded, result.RefundId, result.Status);
        order.ApplyRefundOutcome();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation("Refunded {Amount} on order {OrderId}: refund {RefundId} status {Status}",
            refunded, orderId, result.RefundId, result.Status);
        return (order, refund);
    }

    public async Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken ct = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        return order is not null && order.BuyerId == buyerId ? order : null;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
        return orders;
    }

    // ---- helpers ----
    private static string OrderKey(int orderId) => $"order:{orderId}";

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(int orderId, string buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), ct);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task<string> ResolveVaultIdAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), ct);
        var pm = buyer?.FindPaymentMethod(paymentMethodId);
        if (pm is null)
        {
            throw new ResourceNotFoundException($"Saved card {paymentMethodId} was not found.");
        }
        return pm.CardId;
    }
}
