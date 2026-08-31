using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Order payment lifecycle: authorize (hold) at checkout, capture at fulfilment,
/// void on cancel, refund on return. All money movement goes through IPaymentGateway
/// and is idempotent in effect.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipToAddress =
        new Address("123 Main St.", "Kent", "OH", "United States", "44240");

    // PayPal blocks duplicate invoice ids and reuses request ids account-wide, forever.
    // A per-process component keeps those values unique even when the database is
    // reset between runs (in-memory provider), while staying deterministic within a
    // run so idempotent replays still line up.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IPaymentGateway _paymentGateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IPaymentGateway paymentGateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger,
        IOptions<PaymentOptions> paymentOptions)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _paymentGateway = paymentGateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _currency = paymentOptions.Value.Currency;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineRequest> items, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(items, nameof(items));
        if (items.Count == 0 || items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentStateException("An order needs at least one item with a quantity of one or more.");
        }

        var spec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(spec, cancellationToken);

        var missingIds = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentStateException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = items.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, DefaultShipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation($"Order {order.Id} placed by {buyerId}, total {order.Total()} {_currency}, awaiting payment.");
        return order;
    }

    public async Task<Payment> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(command, nameof(command));

        var order = await GetOwnedOrderWithPaymentAsync(buyerId, orderId, cancellationToken);

        // Idempotent replay: a double-click on an already-authorized order returns its payment.
        if (order.Status == OrderStatus.PaymentAuthorized && order.Payment?.Status == PaymentStatus.Authorized)
        {
            return order.Payment;
        }
        if (order.Status != OrderStatus.AwaitingPayment)
        {
            throw new PaymentStateException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        string? vaultTokenId = null;
        string? savedCardDescription = null;
        if (command.SavedPaymentMethodId.HasValue)
        {
            if (command.Card is not null)
            {
                throw new PaymentStateException("Provide either card details or a saved payment method id, not both.");
            }
            var saved = await _paymentMethodRepository.GetByIdAsync(command.SavedPaymentMethodId.Value, cancellationToken);
            if (saved is null || saved.OwnerId != buyerId)
            {
                throw new SavedPaymentMethodNotFoundException(command.SavedPaymentMethodId.Value);
            }
            vaultTokenId = saved.PayPalPaymentTokenId;
            savedCardDescription = saved.Describe();
        }
        else if (command.Card is null)
        {
            throw new PaymentStateException("Provide card details or a saved payment method id.");
        }

        var payment = order.Payment;
        if (payment is null)
        {
            payment = new Payment(order.Id, buyerId, order.Total(), _currency);
            order.AttachPayment(payment);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        var gatewayRequest = new GatewayAuthorizeRequest
        {
            Amount = payment.Amount,
            Currency = payment.Currency,
            ReferenceId = $"eshop-{InstanceId}-order-{order.Id}",
            // A fresh invoice id per authorize attempt: a failed attempt may still have
            // registered the invoice at PayPal, and duplicate invoice ids are rejected.
            InvoiceId = $"eshop-{InstanceId}-order-{order.Id}-a{payment.FailedAuthorizeAttempts}",
            Card = command.Card,
            VaultPaymentTokenId = vaultTokenId
        };
        // Same key while no failure is recorded: a retry after a lost response replays the
        // original gateway request instead of authorizing twice. A decline bumps the counter.
        var idempotencyKey = $"eshop-{InstanceId}-pay-{payment.Id}-v{payment.FailedAuthorizeAttempts}";

        try
        {
            var authorization = await _paymentGateway.AuthorizeAsync(gatewayRequest, idempotencyKey, cancellationToken);
            var cardDescription = savedCardDescription ?? Describe(authorization.CardBrand, authorization.CardLast4);
            payment.MarkAuthorized(authorization.GatewayOrderId ?? string.Empty, authorization.AuthorizationId,
                authorization.Status, authorization.ExpiresAt, cardDescription);
            order.MarkPaymentAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id} paid: PayPal authorization {authorization.AuthorizationId} for {payment.Amount} {payment.Currency}.");
            return payment;
        }
        catch (PaymentGatewayException)
        {
            payment.MarkAuthorizationFailed();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }
    }

    public async Task<Payment> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderWithPaymentAsync(orderId, cancellationToken);

        // Idempotent replay: fulfilling an already-fulfilled order returns its capture.
        if (order.Status == OrderStatus.Fulfilled && order.Payment?.Status == PaymentStatus.Captured)
        {
            return order.Payment;
        }
        if (order.Status != OrderStatus.PaymentAuthorized || order.Payment is null ||
            order.Payment.Status != PaymentStatus.Authorized || order.Payment.AuthorizationId is null)
        {
            throw new PaymentStateException($"Order {orderId} is {order.Status} and cannot be fulfilled.");
        }

        var payment = order.Payment;
        var authorizationId = await EnsureCapturableAuthorizationAsync(order, payment, cancellationToken);

        try
        {
            var capture = await CaptureAsync(payment, authorizationId, orderId, cancellationToken);
            payment.MarkCaptured(capture.CaptureId, capture.Amount, capture.Fee, capture.NetAmount,
                capture.CapturedAt ?? DateTimeOffset.UtcNow);
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id} fulfilled: PayPal capture {capture.CaptureId}, net {capture.NetAmount} {capture.Currency}.");
            return payment;
        }
        catch (PaymentGatewayException ex) when (IsStaleAuthorization(ex))
        {
            // The authorization went stale between the check and the capture: renew once and retry.
            _logger.LogWarning($"Authorization {authorizationId} for order {order.Id} failed as stale ({ex.Issue}); renewing.");
            var renewed = await RenewAuthorizationAsync(order, payment, cancellationToken);
            var capture = await CaptureAsync(payment, renewed, orderId, cancellationToken);
            payment.MarkCaptured(capture.CaptureId, capture.Amount, capture.Fee, capture.NetAmount,
                capture.CapturedAt ?? DateTimeOffset.UtcNow);
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return payment;
        }
    }

    public async Task<Payment?> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderWithPaymentAsync(orderId, cancellationToken);

        // Idempotent replay.
        if (order.Status == OrderStatus.Cancelled)
        {
            return order.Payment;
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentStateException($"Order {orderId} is already fulfilled; refund it instead of cancelling.");
        }

        var payment = order.Payment;
        if (order.Status == OrderStatus.PaymentAuthorized && payment?.AuthorizationId is not null)
        {
            try
            {
                await _paymentGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    $"eshop-{InstanceId}-void-{payment.Id}-{payment.AuthorizationId}", cancellationToken);
            }
            catch (PaymentGatewayException ex) when (IsAlreadyVoided(ex))
            {
                // The hold was already released at PayPal; cancelling stays idempotent.
            }
            payment.MarkVoided("VOIDED");
            _logger.LogInformation($"Order {order.Id} cancelled: PayPal authorization {payment.AuthorizationId} voided.");
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<PaymentRefund> RefundAsync(int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOrderWithPaymentAsync(orderId, cancellationToken);
        var payment = order.Payment;
        if (order.Status != OrderStatus.Fulfilled || payment is null || payment.CaptureId is null)
        {
            throw new PaymentStateException($"Order {orderId} is {order.Status}; only fulfilled orders can be refunded.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null && existing.Status != RefundStatus.Failed)
        {
            // Same key replayed: return the original refund, never refund twice.
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        var refund = existing ?? payment.AddRefund(idempotencyKey, refundAmount, noteToPayer);
        if (existing is not null)
        {
            // A previous attempt under this key failed at the gateway; retry it.
            if (refundAmount != existing.Amount)
            {
                throw new PaymentStateException(
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of {existing.Amount} {existing.Currency}.");
            }
        }
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var gatewayKey = $"eshop-{InstanceId}-refund-{payment.Id}-{ShortHash(idempotencyKey)}";
        try
        {
            var result = await _paymentGateway.RefundCaptureAsync(payment.CaptureId, refund.Amount, payment.Currency,
                $"eshop-{InstanceId}-order-{order.Id}-refund-{refund.Id}", noteToPayer, gatewayKey, cancellationToken);
            refund.MarkCompleted(result.RefundId, payment);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id}: refund {result.RefundId} of {refund.Amount} {refund.Currency} completed.");
            return refund;
        }
        catch (PaymentGatewayException)
        {
            refund.MarkFailed();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<Order>> ListOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var spec = new OrdersWithPaymentByBuyerSpecification(buyerId);
        return await _orderRepository.ListAsync(spec, cancellationToken);
    }

    private async Task<Order> GetOwnedOrderWithPaymentAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderWithPaymentAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            // Do not leak that the order exists.
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Order> GetOrderWithPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var spec = new OrderWithPaymentSpecification(orderId);
        var order = await _orderRepository.FirstOrDefaultAsync(spec, cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    /// <summary>
    /// Returns the authorization id to capture. Renews the authorization when it has
    /// gone stale (past its expiry or no longer in a capturable state).
    /// </summary>
    private async Task<string> EnsureCapturableAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        payment.RenewAuthorization(authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var freshEnough = authorization.ExpiresAt is null || authorization.ExpiresAt > DateTimeOffset.UtcNow;
        if (authorization.Status == "CREATED" && freshEnough)
        {
            return authorization.AuthorizationId;
        }
        if (authorization.Status == "PENDING")
        {
            throw new PaymentStateException(
                $"Order {order.Id}: PayPal authorization {authorization.AuthorizationId} is still pending review; fulfil again once it clears.");
        }

        return await RenewAuthorizationAsync(order, payment, cancellationToken);
    }

    private async Task<string> RenewAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
                $"eshop-{InstanceId}-reauth-{payment.Id}-{payment.AuthorizationId}", cancellationToken);
            payment.RenewAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation($"Order {order.Id}: authorization renewed as {renewed.AuthorizationId}.");
            return renewed.AuthorizationId;
        }
        catch (PaymentGatewayException ex)
        {
            throw new AuthorizationRenewalException(
                $"Order {order.Id}: the PayPal authorization went stale and could not be renewed ({ex.Issue ?? ex.Message}). " +
                "PayPal only renews authorizations between day 4 and day 29 after the original hold. " +
                "Cancel this order and ask the shopper to place and pay for a new one.");
        }
    }

    private Task<PaymentGatewayCapture> CaptureAsync(Payment payment, string authorizationId, int orderId, CancellationToken cancellationToken)
    {
        return _paymentGateway.CaptureAsync(authorizationId, payment.Amount, payment.Currency,
            $"eshop-{InstanceId}-order-{orderId}-capture", $"eshop-{InstanceId}-capture-{payment.Id}-{authorizationId}", cancellationToken);
    }

    private static bool IsStaleAuthorization(PaymentGatewayException ex) =>
        ex.Issue is "AUTHORIZATION_EXPIRED" or "MAX_CAPTURE_ATTEMPTS" or "INVALID_RESOURCE_ID";

    private static bool IsAlreadyVoided(PaymentGatewayException ex) =>
        ex.Issue is "PREVIOUSLY_VOIDED" or "AUTHORIZATION_VOIDED" or "CANNOT_BE_VOIDED";

    private static string? Describe(string? brand, string? last4) =>
        brand is null && last4 is null ? null : $"{brand ?? "Card"} x-{last4 ?? "????"}";

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}
