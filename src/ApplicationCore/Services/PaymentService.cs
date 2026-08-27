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
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    // PayPal refuses a reused PayPal-Request-Id when the payload differs, so request ids are
    // scoped to this process instance: deterministic per payment row within a run (protecting
    // against double submissions) without colliding across restarts of an in-memory store.
    private static readonly string InstanceId = Guid.NewGuid().ToString("N")[..8];

    // PayPal rejects PayPal-Request-Id values that are too long or carry non-ASCII characters,
    // so an arbitrary caller-supplied idempotency key is hashed into a safe, stable header value.
    private static string ToPayPalRequestId(string idempotencyKey)
    {
        if (idempotencyKey.Length <= 50 && idempotencyKey.All(c => c is >= (char)33 and <= (char)126))
        {
            return idempotencyKey;
        }

        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(idempotencyKey)));
        return $"eshop-key-{hash[..32].ToLowerInvariant()}";
    }

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedCardRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IPayPalGateway _payPalGateway;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedCardRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IPayPalGateway payPalGateway,
        PayPalSettings settings,
        IAppLogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedCardRepository = savedCardRepository;
        _catalogItemRepository = catalogItemRepository;
        _payPalGateway = payPalGateway;
        _settings = settings;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderItemRequest> items,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (items is null || items.Count == 0)
        {
            throw new PaymentConflictException("An order needs at least one item.");
        }
        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentConflictException("Item quantities must be positive.");
        }

        var catalogItemsSpec = new CatalogItemsSpecification(items.Select(i => i.CatalogItemId).ToArray());
        var catalogItems = await _catalogItemRepository.ListAsync(catalogItemsSpec, cancellationToken);

        var missingIds = items.Select(i => i.CatalogItemId).Except(catalogItems.Select(c => c.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentConflictException($"Unknown catalog item id(s): {string.Join(", ", missingIds)}.");
        }

        var orderItems = items.Select(item =>
        {
            var catalogItem = catalogItems.First(c => c.Id == item.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, order.Total(), _settings.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for buyer {BuyerId}; awaiting payment of {Total} {Currency}",
            order.Id, buyerId, order.Total(), _settings.Currency);
        return order;
    }

    public async Task<Payment> PayOrderAsync(int orderId, string buyerId, PayPalCardDetails? card,
        int? savedPaymentMethodId, CancellationToken cancellationToken = default)
    {
        var order = await GetOwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is cancelled and cannot be paid.");
        }

        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        // Idempotency: an order that already holds an authorization (or was captured) is not charged again.
        if (payment is not null &&
            (payment.Status == PaymentStatus.Authorized || payment.Status == PaymentStatus.Captured))
        {
            return payment;
        }
        if (order.Status == OrderStatus.Fulfilled || order.Status == OrderStatus.AwaitingFulfilment)
        {
            throw new PaymentConflictException($"Order {orderId} has already been paid.");
        }

        PayPalPaymentSource paymentSource;
        if (savedPaymentMethodId.HasValue)
        {
            var savedCard = await _savedCardRepository.GetByIdAsync(savedPaymentMethodId.Value, cancellationToken);
            if (savedCard is null || savedCard.BuyerId != buyerId)
            {
                throw new SavedPaymentMethodNotFoundException(savedPaymentMethodId.Value);
            }
            paymentSource = PayPalPaymentSource.ForVaultedCard(savedCard.VaultTokenId);
        }
        else if (card is not null)
        {
            paymentSource = PayPalPaymentSource.ForCard(card);
        }
        else
        {
            throw new PaymentConflictException("Provide either card details or a saved paymentMethodId.");
        }

        // A fresh Payment row per attempt keeps PayPal-Request-Id values deterministic per attempt,
        // so a retried double submission of the same attempt is deduplicated by PayPal itself.
        payment = new Payment(order.Id, buyerId, order.Total(), _settings.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        var referenceId = $"eshop-order-{order.Id}";
        var payPalOrder = await _payPalGateway.CreateOrderAsync(payment.OrderTotal, payment.Currency,
            referenceId, paymentSource, $"eshop-{InstanceId}-payment-{payment.Id}-create", cancellationToken);

        // When card details are supplied at creation, PayPal authorizes immediately and the
        // authorization is already on the create response; otherwise authorize explicitly.
        var authorization = payPalOrder.Authorization
            ?? await _payPalGateway.AuthorizeOrderAsync(payPalOrder.Id, paymentSource,
                $"eshop-{InstanceId}-payment-{payment.Id}-authorize", cancellationToken);

        if (authorization.Amount != payment.OrderTotal)
        {
            _logger.LogWarning("Order {OrderId}: PayPal authorized {Authorized} but order total is {Total}",
                order.Id, authorization.Amount, payment.OrderTotal);
        }

        payment.MarkAuthorized(payPalOrder.Id, authorization.AuthorizationId, authorization.AuthorizationStatus,
            authorization.Amount, authorization.ExpirationTime);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkAwaitingFulfilment();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorized via PayPal authorization {AuthorizationId}",
            order.Id, authorization.AuthorizationId);
        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        // Idempotency: fulfilling an already-fulfilled order returns the existing capture.
        if (order.Status == OrderStatus.Fulfilled && payment?.Status == PaymentStatus.Captured)
        {
            return payment;
        }
        if (order.Status != OrderStatus.AwaitingFulfilment || payment is null || payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentConflictException(
                $"Order {orderId} is not awaiting fulfilment (order: {order.Status}, payment: {payment?.Status.ToString() ?? "none"}).");
        }

        var authorizationId = payment.AuthorizationId
            ?? throw new PaymentConflictException($"Order {orderId} has no PayPal authorization to capture.");

        var authorization = await _payPalGateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (!IsCapturable(authorization.Status))
        {
            authorization = await RenewAuthorizationAsync(orderId, payment, authorization, cancellationToken);
        }

        var capture = await _payPalGateway.CaptureAuthorizationAsync(authorization.Id,
            payment.OrderTotal, payment.Currency, $"eshop-{InstanceId}-payment-{payment.Id}-capture", cancellationToken);

        payment.MarkCaptured(capture.Id, capture.Status, capture.Amount, capture.PayPalFee, capture.NetAmount);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} fulfilled; captured {Amount} {Currency} (capture {CaptureId})",
            order.Id, capture.Amount, payment.Currency, capture.Id);
        return payment;
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }
        if (order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fulfilled and cannot be cancelled; issue a refund instead.");
        }

        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);

        // Idempotency: cancelling twice is a no-op once the hold is released.
        if (order.Status == OrderStatus.Cancelled)
        {
            return payment;
        }

        if (payment?.Status == PaymentStatus.Authorized && payment.AuthorizationId is not null)
        {
            try
            {
                await _payPalGateway.VoidAuthorizationAsync(payment.AuthorizationId,
                    $"eshop-{InstanceId}-payment-{payment.Id}-void", cancellationToken);
            }
            catch (PayPalGatewayException ex) when (ex.HasIssue("AUTHORIZATION_ALREADY_VOIDED"))
            {
                // Already released at PayPal; treat as released locally too.
            }
            payment.MarkVoided();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.Cancel();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} cancelled; held funds released", orderId);
        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(int orderId, decimal? amount, string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new OrderNotFoundException(orderId);
        }

        var payment = await GetLatestPaymentAsync(orderId, cancellationToken);
        if (payment?.Status != PaymentStatus.Captured || payment.CaptureId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        // Idempotency: a repeated request under the same key returns the original refund.
        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
        if (existing is not null)
        {
            return existing;
        }

        var refundAmount = amount ?? payment.RefundableAmount;
        if (refundAmount <= 0)
        {
            throw new PaymentConflictException($"Order {orderId} has already been refunded in full.");
        }
        if (refundAmount > payment.RefundableAmount)
        {
            throw new PaymentConflictException(
                $"Refund of {refundAmount:F2} {payment.Currency} exceeds the refundable remainder " +
                $"of {payment.RefundableAmount:F2} {payment.Currency} (captured {payment.CapturedAmount:F2}, " +
                $"already refunded {payment.TotalRefunded:F2}).");
        }

        var refund = await _payPalGateway.RefundCaptureAsync(payment.CaptureId, refundAmount, payment.Currency,
            ToPayPalRequestId(idempotencyKey), cancellationToken);

        var record = payment.AddRefund(refund.Id, idempotencyKey, refund.Amount, refund.Status);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} (refund {RefundId})",
            orderId, refund.Amount, payment.Currency, refund.Id);
        return record;
    }

    public async Task<IReadOnlyList<OrderWithPayment>> ListMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToArray()), cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderWithPayment(o,
                payments.Where(p => p.OrderId == o.Id).OrderByDescending(p => p.Id).FirstOrDefault()))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, PayPalCardDetails card, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        // PayPal's vault API rejects PayPal-Request-Id values longer than ~50 chars with an
        // unhelpful UNKNOWN_VALIDATION error, so keep these short.
        var setupToken = await _payPalGateway.CreateSetupTokenAsync(card, $"eshop-vs-{Guid.NewGuid():N}", cancellationToken);
        var paymentToken = await _payPalGateway.CreatePaymentTokenAsync(setupToken.Id, $"eshop-vt-{Guid.NewGuid():N}", cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, paymentToken.Id,
            paymentToken.CustomerId ?? setupToken.CustomerId,
            paymentToken.Brand, paymentToken.LastDigits,
            paymentToken.ExpiryMonth, paymentToken.ExpiryYear, paymentToken.CardholderName);
        await _savedCardRepository.AddAsync(saved, cancellationToken);

        _logger.LogInformation("Buyer {BuyerId} saved a card ending in {LastFour}", buyerId, paymentToken.LastDigits ?? "????");
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> ListSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(int paymentMethodId, string buyerId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (savedCard is null || savedCard.BuyerId != buyerId)
        {
            throw new SavedPaymentMethodNotFoundException(paymentMethodId);
        }

        try
        {
            await _payPalGateway.DeletePaymentTokenAsync(savedCard.VaultTokenId, cancellationToken);
        }
        catch (PayPalGatewayException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Already gone from PayPal's vault; remove the local record regardless.
        }

        await _savedCardRepository.DeleteAsync(savedCard, cancellationToken);
        _logger.LogInformation("Buyer {BuyerId} deleted saved payment method {PaymentMethodId}", buyerId, paymentMethodId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentConflictException("The 'to' timestamp must not be earlier than the 'from' timestamp.");
        }

        var transactions = await _payPalGateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(cancellationToken);

        var knownIds = payments
            .SelectMany(p => new[]
                {
                    p.PayPalOrderId,
                    p.AuthorizationId,
                    p.CaptureId
                }
                .Concat(p.Refunds.Select(r => r.PayPalRefundId)))
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = transactions.Select(t =>
        {
            var matchedPayment = payments.FirstOrDefault(p =>
                string.Equals(p.PayPalOrderId, t.TransactionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.AuthorizationId, t.TransactionId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.CaptureId, t.TransactionId, StringComparison.OrdinalIgnoreCase) ||
                p.Refunds.Any(r => string.Equals(r.PayPalRefundId, t.TransactionId, StringComparison.OrdinalIgnoreCase)));

            return new ReconciliationEntry(
                t.TransactionId, t.EventCode, t.Status, t.Amount, t.Currency, t.FeeAmount, t.InitiationDate,
                matchedPayment?.OrderId,
                matchedPayment is null ? "PayPalOnly" : "Matched");
        }).ToList();

        var seenIds = transactions.Select(t => t.TransactionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var missing = payments
            .Where(p => p.Status is PaymentStatus.Authorized or PaymentStatus.Captured or PaymentStatus.Voided)
            .Where(p => new[] { p.PayPalOrderId, p.AuthorizationId, p.CaptureId }
                .Where(id => !string.IsNullOrEmpty(id))
                .Any(id => !seenIds.Contains(id!)))
            .Select(p => new ReconciliationUnmatchedPayment(
                p.OrderId, p.Id, p.Status.ToString(), p.PayPalOrderId, p.AuthorizationId, p.CaptureId))
            .ToList();

        return new ReconciliationReport(from, to, entries, missing);
    }

    private async Task<Order> GetOwnedOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
        {
            throw new OrderNotFoundException(orderId);
        }
        return order;
    }

    private async Task<Payment?> GetLatestPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private static bool IsCapturable(string status) =>
        string.Equals(status, "CREATED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "PENDING", StringComparison.OrdinalIgnoreCase);

    private async Task<PayPalAuthorizationDetails> RenewAuthorizationAsync(int orderId, Payment payment,
        PayPalAuthorizationDetails staleAuthorization, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Order {OrderId}: authorization {AuthorizationId} is {Status}; attempting renewal",
            orderId, staleAuthorization.Id, staleAuthorization.Status);

        try
        {
            var renewed = await _payPalGateway.ReauthorizeAsync(staleAuthorization.Id,
                payment.OrderTotal, payment.Currency, $"eshop-{InstanceId}-payment-{payment.Id}-reauthorize", cancellationToken);

            payment.MarkReauthorized(renewed.Id, renewed.Status, renewed.ExpirationTime);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return renewed;
        }
        catch (PayPalGatewayException ex)
        {
            throw new PaymentConflictException(
                $"Order {orderId}: the PayPal authorization {staleAuthorization.Id} is {staleAuthorization.Status} " +
                $"and could not be renewed ({ex.ErrorName ?? "PayPal error"}: {ex.Message}). " +
                "Cancel the order and ask the shopper to pay again.");
        }
    }
}
