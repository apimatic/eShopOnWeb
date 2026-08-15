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
using Microsoft.eShopWeb.ApplicationCore.PaymentProcessing;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    // Capture an authorization a little before PayPal's stated expiry to avoid a race at the edge.
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    // A process-lifetime scope mixed into the create/authorize idempotency key. Order ids are unique
    // within a run, but the in-memory database resets them to 1,2,3… on restart while PayPal remembers
    // a PayPal-Request-Id for far longer — so a bare "pay-order-1" would collide with a previous run's
    // hold. The scope is constant within a run (a double-click still dedupes) and differs across runs.
    private static readonly string RunScope = Guid.NewGuid().ToString("N").Substring(0, 12);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IPayPalGateway _payPal;
    private readonly IPaymentSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IPayPalGateway payPal,
        IPaymentSettings settings,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _payPal = payPal;
        _settings = settings;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipTo, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new PaymentStateException("An order must contain at least one line item.");
        }
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentStateException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new EntityNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var address = shipTo ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, items);

        await _orderRepository.AddAsync(order, ct);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId}, awaiting payment, total {order.Total()}.");
        return order;
    }

    public async Task<Order> AuthorizeAsync(string buyerId, int orderId, PayInstruction instruction, CancellationToken ct = default)
    {
        Guard.Against.Null(instruction, nameof(instruction));
        var order = await LoadOrderAsync(orderId, buyerId, ct);

        // Idempotent in effect: if the hold is already in place, return it rather than re-authorizing.
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            return order;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {orderId} cannot be paid because it is {order.Status}.");
        }

        // Resolve the payment source: a saved card (by id, owned by the caller) or raw card details.
        string? vaultId = null;
        int? paymentMethodId = null;
        if (instruction.SavedPaymentMethodId is int savedId)
        {
            var pm = await _paymentMethodRepository.FirstOrDefaultAsync(
                new PaymentMethodByIdAndBuyerSpecification(savedId, buyerId), ct)
                ?? throw new EntityNotFoundException($"Saved card {savedId} was not found.");
            vaultId = pm.VaultId;
            paymentMethodId = pm.Id;
        }
        else if (instruction.Card is null)
        {
            throw new PaymentStateException("Provide either card details or a saved card id to pay.");
        }

        var amount = order.Total();
        var currency = _settings.Currency;

        var authRequest = new PayPalAuthorizeRequest
        {
            Amount = amount,
            Currency = currency,
            Card = vaultId is null ? instruction.Card : null,
            VaultId = vaultId,
            // Deterministic per order within a run so a double-click reuses the same PayPal-Request-Id
            // and never places a second hold; run-scoped so it cannot collide across restarts.
            IdempotencyKey = $"{RunScope}-pay-{orderId}"
        };

        var auth = await _payPal.AuthorizeAsync(authRequest, ct);

        var payment = new Payment(
            auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.Amount, auth.Currency, auth.ExpiresAt, paymentMethodId);
        order.MarkAuthorized(payment);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} authorized (hold {auth.AuthorizationId}) for {auth.Amount} {auth.Currency}.");
        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, buyerId: null, ct);

        // Already captured → idempotent no-op.
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }
        if (order.Status != OrderStatus.Authorized || order.Payment is null)
        {
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled because it is {order.Status}.");
        }

        var payment = order.Payment;

        // Renew a hold that has gone (or is about to go) stale before capturing.
        if (payment.AuthorizationExpiresAt is DateTimeOffset expiresAt && expiresAt <= DateTimeOffset.UtcNow.Add(ExpiryMargin))
        {
            await RenewHoldAsync(order, ct);
        }

        PayPalCapture capture;
        try
        {
            capture = await _payPal.CaptureAsync(payment.AuthorizationId, $"cap-{payment.AuthorizationId}", ct);
        }
        catch (PayPalGatewayException ex) when (ex is not AuthorizationNotRenewableException)
        {
            // The hold may have expired between our check and the capture; renew once and retry.
            _logger.LogWarning($"Capture of order {orderId} failed ({ex.Message}); attempting to renew the hold and retry.");
            await RenewHoldAsync(order, ct);
            capture = await _payPal.CaptureAsync(payment.AuthorizationId, $"cap-{payment.AuthorizationId}-r2", ct);
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} fulfilled: captured {capture.GrossAmount} {capture.Currency}, fee {capture.PayPalFee}, net {capture.NetAmount}.");
        return order;
    }

    private async Task RenewHoldAsync(Order order, CancellationToken ct)
    {
        var payment = order.Payment!;
        var renewed = await _payPal.ReauthorizeAsync(
            payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency, $"reauth-{payment.AuthorizationId}", ct);
        payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation($"Order {order.Id} hold renewed: new authorization {renewed.AuthorizationId}.");
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var order = await LoadOrderAsync(orderId, buyerId: null, ct);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order; // idempotent
        }
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentStateException($"Order {orderId} has been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
        {
            await _payPal.VoidAsync(order.Payment.AuthorizationId, $"void-{order.Payment.AuthorizationId}", ct);
            order.Payment.MarkVoided();
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, ct);
        _logger.LogInformation($"Order {orderId} cancelled; any held funds released.");
        return order;
    }

    public async Task<Refund> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var order = await LoadOrderAsync(orderId, buyerId, ct);

        if (order.Payment is null || !order.Payment.IsCaptured)
        {
            throw new PaymentStateException($"Order {orderId} cannot be refunded before it is fulfilled.");
        }
        var payment = order.Payment;

        // Idempotent: repeating a request under the same key returns the original refund.
        var existing = payment.FindRefundByKey(idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var remaining = payment.RemainingRefundable;
        var refundAmount = amount ?? remaining;
        if (refundAmount <= 0)
        {
            throw new PaymentStateException("Refund amount must be greater than zero.");
        }
        if (refundAmount > remaining)
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount} exceeds the {remaining} still refundable on order {orderId}.");
        }

        // A full refund of the remainder is sent as a null amount so PayPal refunds the exact remainder.
        var isFullRemainder = refundAmount == remaining && payment.TotalRefunded == 0;

        // The caller's key dedupes refunds of THIS capture. Scope the PayPal-Request-Id to the
        // (globally-unique) capture id so the same caller key used against a different capture — or in
        // a different run — is a legitimately distinct refund and never collides on PayPal's side.
        var payPalRequestId = $"{payment.CaptureId}:{idempotencyKey}";
        var result = await _payPal.RefundAsync(
            payment.CaptureId!, isFullRemainder ? null : refundAmount, payment.Currency, payPalRequestId, ct);

        var refund = new Refund(result.RefundId, result.Status, result.Amount == 0 ? refundAmount : result.Amount, idempotencyKey);
        payment.AddRefund(refund);

        var fullyRefunded = payment.RemainingRefundable <= 0m;
        order.MarkRefunded(fullyRefunded);
        await _orderRepository.UpdateAsync(order, ct);

        _logger.LogInformation($"Order {orderId} refunded {refund.Amount} {payment.Currency} (refund {refund.RefundId}); fully refunded: {fullyRefunded}.");
        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), ct);
    }

    private async Task<Order> LoadOrderAsync(int orderId, string? buyerId, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId, buyerId), ct);
        return order ?? throw new EntityNotFoundException($"Order {orderId} was not found.");
    }
}
