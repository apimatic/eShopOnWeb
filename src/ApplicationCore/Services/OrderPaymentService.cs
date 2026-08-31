using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;
using NotFoundException = Microsoft.eShopWeb.ApplicationCore.Exceptions.NotFoundException;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly Address DefaultShipTo = new Address("Not provided", "Not provided", "Not provided", "Not provided", "00000");

    // PayPal scopes PayPal-Request-Id and invoice_id to the whole merchant account, so
    // every id carries a per-instance component: retries within this run stay
    // deterministic (idempotent), while a restarted app (with a fresh in-memory store
    // and recycled order ids) can never collide with keys from a previous run.
    private static readonly string RunId = Guid.NewGuid().ToString("N")[..8];

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IOptions<PayPalSettings> settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        if (lines.Count == 0)
        {
            throw new PaymentStateException("An order must contain at least one item.");
        }
        if (lines.Any(l => l.Quantity <= 0))
        {
            throw new PaymentStateException("Every order line must have a quantity of at least 1.");
        }

        var catalogItemsSpecification = new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray());
        var catalogItems = await _itemRepository.ListAsync(catalogItemsSpecification, cancellationToken);

        var missing = lines.Select(l => l.CatalogItemId).Distinct().Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new NotFoundException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = lines.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? DefaultShipTo, items);
        await _orderRepository.AddAsync(order, cancellationToken);
        return order;
    }

    public async Task<Payment> PayOrderAsync(string buyerId, int orderId, CardDetails? card, int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if ((card == null) == (savedPaymentMethodId == null))
        {
            throw new PaymentStateException("Provide either card details or a saved paymentMethodId, not both.");
        }

        var order = await GetOwnOrderAsync(buyerId, orderId, cancellationToken);
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentStateException($"Order {orderId} has been cancelled and cannot be paid.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        // Idempotency: a double-pay never authorizes twice.
        if (payment != null && (payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Captured))
        {
            return payment;
        }

        string? vaultTokenId = null;
        GatewayCard? gatewayCard = null;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId), cancellationToken);
            if (savedCard == null)
            {
                throw new NotFoundException($"Saved payment method {savedPaymentMethodId} was not found.");
            }
            vaultTokenId = savedCard.VaultTokenId;
        }
        else
        {
            gatewayCard = ToGatewayCard(card!);
        }

        payment ??= new Payment(order.Id, buyerId, order.Total(), _settings.Currency);

        var attempt = payment.AuthorizationAttempts + 1;
        var idempotencyKey = $"eshop-{RunId}-order-{order.Id}-authorize-{attempt}";
        var invoiceId = $"eshop-{RunId}-order-{order.Id}-{attempt}";
        var amount = MoneyOf(payment.Amount);

        GatewayAuthorization authorization;
        try
        {
            authorization = await _gateway.AuthorizeCardPaymentAsync(
                order.Id.ToString(CultureInfo.InvariantCulture), invoiceId, amount, gatewayCard, vaultTokenId, idempotencyKey, cancellationToken);
        }
        catch (Exception ex) when (ex is not PaymentDeclinedException)
        {
            _logger.LogWarning($"Authorization for order {order.Id} failed: {ex.Message}");
            payment.MarkAuthorizationFailed(ex.Message);
            await SavePaymentAsync(payment, cancellationToken);
            throw new PaymentDeclinedException($"PayPal could not authorize the payment for order {order.Id}: {ex.Message}");
        }

        if (authorization.Status is "DENIED")
        {
            payment.MarkAuthorizationFailed($"PayPal denied the authorization (status {authorization.Status}).");
            await SavePaymentAsync(payment, cancellationToken);
            throw new PaymentDeclinedException($"PayPal declined the card payment for order {order.Id} (status {authorization.Status}).");
        }

        payment.MarkAuthorized(authorization.PayPalOrderId!, authorization.AuthorizationId, authorization.Status, authorization.ExpiresAt);
        order.MarkPaymentAuthorized();

        await SavePaymentAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken)
            ?? throw new PaymentStateException($"Order {orderId} has not been paid yet; there is no authorization to capture.");

        // Idempotency: fulfilling an already-captured order returns the recorded capture.
        if (payment.Status == PaymentStatus.Captured)
        {
            return payment;
        }
        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentStateException($"Order {orderId} cannot be fulfilled while its payment is in state {payment.Status}.");
        }

        await RenewAuthorizationIfStaleAsync(payment, cancellationToken);

        var amount = MoneyOf(payment.Amount);
        var invoiceId = $"eshop-{RunId}-order-{order.Id}-capture-{payment.AuthorizationAttempts}";
        var idempotencyKey = $"eshop-{RunId}-order-{order.Id}-capture-{payment.AuthorizationAttempts}";

        GatewayCapture capture;
        try
        {
            capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, amount, invoiceId, idempotencyKey, cancellationToken);
        }
        catch (Exception ex)
        {
            // The authorization may have gone stale between the check and the capture:
            // renew once and retry before giving up.
            _logger.LogWarning($"Capture for order {order.Id} failed ({ex.Message}); attempting one renewal before failing.");
            var renewed = await TryRenewAuthorizationAsync(payment, cancellationToken);
            if (!renewed)
            {
                throw NotRenewable(payment);
            }
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(payment.AuthorizationId!, amount, invoiceId, idempotencyKey, cancellationToken);
            }
            catch (Exception retryEx)
            {
                throw new PaymentDeclinedException($"PayPal could not capture the payment for order {order.Id}: {retryEx.Message}");
            }
        }

        if (capture.Status is "DECLINED" or "FAILED")
        {
            throw new PaymentDeclinedException($"PayPal declined the capture for order {order.Id} (status {capture.Status}).");
        }

        payment.MarkCaptured(
            capture.CaptureId,
            capture.Status,
            ParseMoney(capture.Amount),
            capture.PayPalFee != null ? ParseMoney(capture.PayPalFee) : null,
            capture.NetAmount != null ? ParseMoney(capture.NetAmount) : null);

        if (capture.Status == "COMPLETED")
        {
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return payment;
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
            ?? throw new NotFoundException($"Order {orderId} was not found.");
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);

        if (order.Status == OrderStatus.Fulfilled || payment?.Status == PaymentStatus.Captured)
        {
            throw new PaymentStateException($"Order {orderId} has already been fulfilled; issue a refund instead of cancelling.");
        }
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (payment?.Status == PaymentStatus.Authorized)
        {
            await _gateway.VoidAuthorizationAsync(payment.AuthorizationId!, $"eshop-{RunId}-order-{order.Id}-void-{payment.AuthorizationAttempts}", cancellationToken);
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await GetOwnOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken)
            ?? throw new PaymentStateException($"Order {orderId} has no payment to refund.");

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (payment.Status != PaymentStatus.Captured || payment.CaptureId == null)
        {
            throw new PaymentStateException($"Order {orderId} can only be refunded after it has been fulfilled and its payment captured.");
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0 || refundAmount > payment.RefundableAmount)
        {
            throw new PaymentStateException(
                $"Refund of {refundAmount} {payment.Currency} exceeds the refundable amount of {payment.RefundableAmount} {payment.Currency} for order {orderId}.");
        }

        // PayPal scopes PayPal-Request-Id to the whole merchant account, so namespace the
        // caller's key per capture: distinct keys stay legitimate distinct refunds, while a
        // repeated key maps onto the same PayPal request and cannot refund twice.
        var gatewayKey = $"eshop-{RunId}-order-{orderId}-capture-{payment.CaptureId}-refund-{idempotencyKey}";
        var refund = await _gateway.RefundCaptureAsync(payment.CaptureId, MoneyOf(refundAmount), gatewayKey, noteToPayer, cancellationToken);

        var recorded = payment.RegisterRefund(idempotencyKey, refund.RefundId, refundAmount, payment.Currency, refund.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return recorded;
    }

    private async Task<Order> GetOwnOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        // Existence of another shopper's order is not revealed.
        if (order == null || order.BuyerId != buyerId)
        {
            throw new NotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private async Task RenewAuthorizationIfStaleAsync(Payment payment, CancellationToken cancellationToken)
    {
        var status = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);

        if (status.Status is "VOIDED")
        {
            throw new PaymentStateException($"The authorization for order {payment.OrderId} was voided; the order must be paid again before it can be fulfilled.");
        }
        if (status.Status is "DENIED")
        {
            throw NotRenewable(payment);
        }

        var stale = (status.ExpiresAt.HasValue && status.ExpiresAt.Value <= DateTimeOffset.UtcNow)
            || status.Status is not ("CREATED" or "PENDING" or "CAPTURED" or "PARTIALLY_CAPTURED");

        if (stale && !await TryRenewAuthorizationAsync(payment, cancellationToken))
        {
            throw NotRenewable(payment);
        }
        if (stale)
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    private async Task<bool> TryRenewAuthorizationAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            var renewed = await _gateway.ReauthorizeAsync(
                payment.AuthorizationId!,
                MoneyOf(payment.Amount),
                $"eshop-{RunId}-order-{payment.OrderId}-reauthorize-{payment.AuthorizationAttempts}",
                cancellationToken);
            payment.UpdateAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
            return renewed.Status is "CREATED" or "PENDING";
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Reauthorization for order {payment.OrderId} failed: {ex.Message}");
            return false;
        }
    }

    private static AuthorizationNotRenewableException NotRenewable(Payment payment) =>
        new AuthorizationNotRenewableException(
            $"The PayPal authorization {payment.AuthorizationId} for order {payment.OrderId} has gone stale and can no longer be renewed " +
            $"(PayPal allows renewal only within 29 days of the original authorization). " +
            $"Do not fulfil order {payment.OrderId} against this hold; ask the shopper to pay again and then fulfil.");

    private async Task SavePaymentAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (payment.Id == 0)
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    private GatewayMoney MoneyOf(decimal amount) =>
        new GatewayMoney(_settings.Currency, amount.ToString("0.00", CultureInfo.InvariantCulture));

    private static decimal ParseMoney(GatewayMoney money) =>
        decimal.Parse(money.Value, CultureInfo.InvariantCulture);

    private static GatewayCard ToGatewayCard(CardDetails card) =>
        new GatewayCard(
            card.Number,
            card.Expiry,
            card.SecurityCode,
            card.CardholderName,
            card.BillingAddress == null
                ? null
                : new GatewayAddress(
                    card.BillingAddress.Line1,
                    card.BillingAddress.Line2,
                    card.BillingAddress.City,
                    card.BillingAddress.State,
                    card.BillingAddress.PostalCode,
                    card.BillingAddress.CountryCode));
}
