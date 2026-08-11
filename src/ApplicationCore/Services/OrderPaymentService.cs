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
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Orchestrates the money flow over the existing order model, delegating every PayPal interaction to
/// <see cref="IPayPalPaymentGateway"/>. Idempotency in effect comes from two layers: the payment's own
/// state machine (a repeated request finds the work already done and returns) and stable PayPal-Request-Id
/// keys derived from the order id (so even a concurrent retry reuses the same PayPal action).
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IUriComposer _uriComposer;
    private readonly IPayPalPaymentGateway _gateway;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly string _currency;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<SavedCard> savedCardRepository,
        IUriComposer uriComposer,
        IPayPalPaymentGateway gateway,
        IOptions<PayPalSettings> settings,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _itemRepository = itemRepository;
        _savedCardRepository = savedCardRepository;
        _uriComposer = uriComposer;
        _gateway = gateway;
        _logger = logger;
        _currency = string.IsNullOrWhiteSpace(settings.Value.Currency) ? "USD" : settings.Value.Currency!.Trim();
    }

    public async Task<int> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLineRequest> lines, Address shipToAddress, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));
        if (lines == null || lines.Count == 0)
        {
            throw new InvalidPaymentOperationException("An order must contain at least one line item.");
        }

        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new InvalidPaymentOperationException($"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
            }
        }

        var ids = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), ct);

        var items = new List<OrderItem>();
        foreach (var line in lines)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == line.CatalogItemId)
                ?? throw new InvalidPaymentOperationException($"Catalog item {line.CatalogItemId} was not found.");

            var picture = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrEmpty(picture)) picture = "eCatalog-item-default.png";

            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture);
            items.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, shipToAddress, items);
        await _orderRepository.AddAsync(order, ct);

        // Amounts come from catalog prices; currency from configuration. Order starts awaiting payment.
        var payment = new Payment(order.Id, buyerId, order.Total(), _currency);
        await _paymentRepository.AddAsync(payment, ct);

        _logger.LogInformation($"Placed order {order.Id} for {buyerId}: total {order.Total():0.00} {_currency}.");
        return order.Id;
    }

    public async Task<OrderPaymentSummary> PayAsync(string buyerId, int orderId, PaymentInstruction instruction, CancellationToken ct = default)
    {
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        switch (payment.Status)
        {
            case PaymentStatus.Authorized:
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return await BuildSummaryAsync(payment, ct); // already paid — a double-click never authorizes twice
            case PaymentStatus.Cancelled:
                throw new InvalidPaymentOperationException($"Order {orderId} was cancelled and can no longer be paid.");
        }

        if (instruction == null || (instruction.Card == null && !instruction.SavedCardId.HasValue))
        {
            throw new InvalidPaymentOperationException("Provide either card details or a saved card id to pay.");
        }
        if (instruction.Card != null && instruction.SavedCardId.HasValue)
        {
            throw new InvalidPaymentOperationException("Provide either card details or a saved card id, not both.");
        }

        var idempotencyKey = $"order-{orderId}-pay";
        var reference = orderId.ToString();

        AuthorizationResult auth;
        if (instruction.SavedCardId.HasValue)
        {
            var card = await _savedCardRepository.FirstOrDefaultAsync(new SavedCardByIdForBuyerSpec(buyerId, instruction.SavedCardId.Value), ct)
                ?? throw new PaymentNotFoundException($"Saved card {instruction.SavedCardId.Value} was not found.");
            auth = await _gateway.AuthorizeWithVaultAsync(payment.Amount, payment.Currency, card.VaultId, reference, idempotencyKey, ct);
        }
        else
        {
            auth = await _gateway.AuthorizeWithCardAsync(payment.Amount, payment.Currency, instruction.Card!, reference, idempotencyKey, ct);
        }

        payment.MarkAuthorized(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpiresAt);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Authorized order {orderId}: hold {auth.AuthorizationId} ({auth.Status}).");
        return await BuildSummaryAsync(payment, ct);
    }

    public async Task<OrderPaymentSummary> FulfilAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        switch (payment.Status)
        {
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                return await BuildSummaryAsync(payment, ct); // already fulfilled/captured — idempotent
            case PaymentStatus.AwaitingPayment:
                throw new InvalidPaymentOperationException($"Order {orderId} has not been paid and cannot be fulfilled.");
            case PaymentStatus.Cancelled:
                throw new InvalidPaymentOperationException($"Order {orderId} was cancelled and cannot be fulfilled.");
        }

        // Proactively renew a hold that has already gone past its honour window.
        if (IsAuthorizationStale(payment))
        {
            await RenewAuthorizationAsync(payment, orderId, ct);
        }

        var captureKey = $"order-{orderId}-capture";
        CaptureResult capture;
        try
        {
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, captureKey, ct);
        }
        catch (PaymentGatewayException ex) when (IsExpiredAuthorization(ex))
        {
            // Reactive path: the hold went stale before capture. Renew rather than failing fulfilment,
            // then capture the renewed authorization.
            _logger.LogWarning($"Authorization for order {orderId} was stale at capture; renewing.");
            await RenewAuthorizationAsync(payment, orderId, ct);
            capture = await _gateway.CaptureAsync(payment.AuthorizationId!, captureKey, ct);
        }

        payment.MarkCaptured(capture.CaptureId, capture.Status, capture.Gross, capture.Fee, capture.Net);
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Fulfilled order {orderId}: captured {capture.Gross:0.00} {capture.Currency}, fee {capture.Fee:0.00}, net {capture.Net:0.00}.");
        return await BuildSummaryAsync(payment, ct);
    }

    public async Task<OrderPaymentSummary> CancelAsync(int orderId, CancellationToken ct = default)
    {
        var payment = await LoadPaymentAsync(orderId, ct);

        switch (payment.Status)
        {
            case PaymentStatus.Cancelled:
                return await BuildSummaryAsync(payment, ct); // idempotent
            case PaymentStatus.Captured:
            case PaymentStatus.PartiallyRefunded:
            case PaymentStatus.Refunded:
                throw new InvalidPaymentOperationException($"Order {orderId} has been fulfilled; use a refund instead of cancelling.");
        }

        if (payment.Status == PaymentStatus.Authorized && payment.AuthorizationId != null)
        {
            try
            {
                await _gateway.VoidAsync(payment.AuthorizationId, $"order-{orderId}-void", ct);
            }
            catch (PaymentGatewayException ex) when (IsAlreadyReleased(ex))
            {
                // The hold was already released at PayPal — cancellation is still the correct outcome.
                _logger.LogWarning($"Authorization for order {orderId} was already released at PayPal.");
            }
        }

        payment.MarkCancelled();
        await _paymentRepository.UpdateAsync(payment, ct);
        _logger.LogInformation($"Cancelled order {orderId}; any held funds released.");
        return await BuildSummaryAsync(payment, ct);
    }

    public async Task<RefundOutcome> RefundAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, CancellationToken ct = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var payment = await LoadOwnedPaymentAsync(buyerId, orderId, ct);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded) || payment.CaptureId == null)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has not been captured; there is nothing to refund.");
        }

        // Idempotency: repeating a request under the same key returns the original refund, not a second one.
        var existing = payment.FindRefundByIdempotencyKey(idempotencyKey);
        if (existing != null)
        {
            return ToOutcome(existing);
        }

        if (amount.HasValue && amount.Value <= 0)
        {
            throw new InvalidPaymentOperationException("Refund amount must be greater than zero.");
        }

        var refundAmount = amount ?? payment.RefundableRemaining;
        if (refundAmount <= 0)
        {
            throw new InvalidPaymentOperationException($"Order {orderId} has already been fully refunded.");
        }
        if (refundAmount > payment.RefundableRemaining)
        {
            throw new InvalidPaymentOperationException(
                $"Refund of {refundAmount:0.00} exceeds the remaining refundable amount of {payment.RefundableRemaining:0.00} for order {orderId}.");
        }

        var result = await _gateway.RefundAsync(payment.CaptureId, refundAmount, payment.Currency, idempotencyKey, ct);

        var refund = new PaymentRefund(idempotencyKey, result.RefundId, refundAmount, payment.Currency, result.Status);
        payment.AddRefund(refund);
        await _paymentRepository.UpdateAsync(payment, ct);

        _logger.LogInformation($"Refunded {refundAmount:0.00} {payment.Currency} on order {orderId} (refund {result.RefundId}, {result.Status}).");
        return ToOutcome(refund);
    }

    public async Task<IReadOnlyList<OrderPaymentSummary>> GetMyOrdersAsync(string buyerId, CancellationToken ct = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), ct);
        var payments = await _paymentRepository.ListAsync(new PaymentsByBuyerSpec(buyerId), ct);
        var byOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order =>
            {
                byOrderId.TryGetValue(order.Id, out var payment);
                return BuildSummary(order, payment);
            })
            .ToList();
    }

    // --- helpers ---

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken ct) =>
        await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

    private async Task<Payment> LoadOwnedPaymentAsync(string buyerId, int orderId, CancellationToken ct)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), ct);
        // A shopper must never see or act on another's order — do not reveal that it exists.
        if (payment == null || payment.BuyerId != buyerId)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }
        return payment;
    }

    private async Task RenewAuthorizationAsync(Payment payment, int orderId, CancellationToken ct)
    {
        try
        {
            var state = await _gateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency, $"order-{orderId}-reauth", ct);
            payment.RenewAuthorization(state.AuthorizationId, state.Status, state.ExpiresAt);
            await _paymentRepository.UpdateAsync(payment, ct);
            _logger.LogInformation($"Renewed authorization for order {orderId}: new hold {state.AuthorizationId} ({state.Status}).");
        }
        catch (PaymentGatewayException ex)
        {
            // Cannot be renewed — say so in terms an operator can act on.
            throw new InvalidPaymentOperationException(
                $"The authorization for order {orderId} has expired and can no longer be renewed ({ex.Message}). " +
                "The held funds are gone; ask the shopper to place and pay for a new order.");
        }
    }

    private static bool IsAuthorizationStale(Payment payment) =>
        payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow;

    private static bool IsExpiredAuthorization(PaymentGatewayException ex) =>
        ex.HasIssue("AUTHORIZATION_EXPIRED") || ex.HasIssue("INVALID_AUTHORIZATION_ID");

    private static bool IsAlreadyReleased(PaymentGatewayException ex) =>
        ex.HasIssue("AUTH_ALREADY_VOIDED") || ex.HasIssue("AUTHORIZATION_ALREADY_VOIDED");

    private RefundOutcome ToOutcome(PaymentRefund refund) => new()
    {
        PayPalRefundId = refund.RefundId,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = refund.Currency
    };

    private async Task<OrderPaymentSummary> BuildSummaryAsync(Payment payment, CancellationToken ct)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(payment.OrderId), ct);
        if (order == null)
        {
            // Order and payment are created together; this should not happen. Fall back to a payment-only view.
            return new OrderPaymentSummary
            {
                OrderId = payment.OrderId,
                Total = payment.Amount,
                Currency = payment.Currency,
                PaymentStatus = payment.Status.ToString(),
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                CapturedGross = payment.CapturedGross,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                TotalRefunded = payment.TotalRefunded,
                RefundableRemaining = payment.CaptureId != null ? payment.RefundableRemaining : 0m,
                Refunds = payment.Refunds.Select(ToOutcome).ToList()
            };
        }
        return BuildSummary(order, payment);
    }

    private OrderPaymentSummary BuildSummary(Order order, Payment? payment)
    {
        var lines = order.OrderItems
            .Select(i => new OrderLineSummary
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            })
            .ToList();

        var summary = new OrderPaymentSummary
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Currency = payment?.Currency ?? _currency,
            PaymentStatus = payment?.Status.ToString() ?? "NoPayment",
            Lines = lines
        };

        if (payment != null)
        {
            summary.PayPalOrderId = payment.PayPalOrderId;
            summary.AuthorizationId = payment.AuthorizationId;
            summary.CaptureId = payment.CaptureId;
            summary.CapturedGross = payment.CapturedGross;
            summary.PayPalFee = payment.PayPalFee;
            summary.NetAmount = payment.NetAmount;
            summary.TotalRefunded = payment.TotalRefunded;
            summary.RefundableRemaining = payment.CaptureId != null ? payment.RefundableRemaining : 0m;
            summary.Refunds = payment.Refunds.Select(ToOutcome).ToList();
        }

        return summary;
    }
}
