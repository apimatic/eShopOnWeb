using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places, pays and refunds orders. Every processor write follows the same order:
/// claim in our own store → processor call → record the result. The claim is what stops a
/// double-click (or two racing requests) from reaching the processor twice.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    /// <summary>
    /// Time allowed for all processor calls made while serving one request (leaves headroom under the
    /// 30-second ceiling a caller may wait).
    /// </summary>
    public static readonly TimeSpan DefaultProcessorBudget = TimeSpan.FromSeconds(25);

    private const int MaxQuantityPerLine = 1000;
    private const int MaxConcurrencyRetries = 5;

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IRepository<PaymentAttempt> _attemptRepository;
    private readonly IRepository<OrderRefund> _refundRepository;
    private readonly IPaymentStateStore _stateStore;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _processorBudget;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IRepository<PaymentAttempt> attemptRepository,
        IRepository<OrderRefund> refundRepository,
        IPaymentStateStore stateStore,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger,
        TimeProvider? clock = null,
        TimeSpan? processorBudget = null)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _attemptRepository = attemptRepository;
        _refundRepository = refundRepository;
        _stateStore = stateStore;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _processorBudget = processorBudget ?? DefaultProcessorBudget;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address? shipToAddress, CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
            return new(PlaceOrderOutcome.Invalid, "An order needs at least one item.");
        if (lines.Any(l => l.CatalogItemId <= 0))
            return new(PlaceOrderOutcome.Invalid, "Every item needs a valid catalogItemId.");
        if (lines.Any(l => l.Quantity <= 0 || l.Quantity > MaxQuantityPerLine))
            return new(PlaceOrderOutcome.Invalid, $"Every quantity must be between 1 and {MaxQuantityPerLine}.");

        var merged = lines.GroupBy(l => l.CatalogItemId)
            .Select(g => new PlaceOrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
        if (merged.Any(l => l.Quantity > MaxQuantityPerLine))
            return new(PlaceOrderOutcome.Invalid, $"Every quantity must be between 1 and {MaxQuantityPerLine}.");

        var ids = merged.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            return new(PlaceOrderOutcome.Invalid, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        // Prices always come from the catalog, never from the caller.
        var items = merged.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var currency = _gateway.Currency;
        var order = new Order(buyerId, shipToAddress ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), items, currency);
        if (!MinorUnits.TryConvert(order.Total(), currency, out var minorUnits) || minorUnits <= 0)
            return new(PlaceOrderOutcome.Invalid, $"The order total {order.Total()} cannot be charged in {currency}.");

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} placed by buyer with {ItemCount} line(s), total {Total} {Currency}.", order.Id, items.Count, order.Total(), currency);
        return new(PlaceOrderOutcome.Created, "Order placed; awaiting payment.", order);
    }

    public async Task<PayOrderResult> PayOrderAsync(PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(command.OrderId), cancellationToken);
        // Another buyer's order is reported exactly like a missing one.
        if (order is null || !string.Equals(order.BuyerId, command.BuyerId, StringComparison.Ordinal))
            return new(PayOrderOutcome.OrderNotFound, $"Order {command.OrderId} was not found.");

        if (order.IsPaid)
            return new(PayOrderOutcome.AlreadyPaid, "This order is already paid. The card was not charged again.", order);

        var attempts = await _attemptRepository.ListAsync(new PaymentAttemptsForOrderSpec(order.Id), cancellationToken);
        var latest = attempts.LastOrDefault();
        var now = _clock.GetUtcNow();

        if (latest is { Status: PaymentAttemptStatus.Authorised })
        {
            // The processor authorised an earlier attempt but recording it on the order did not complete.
            await RecordPaidAsync(order, latest);
            return new(PayOrderOutcome.AlreadyPaid, "This order is already paid. The card was not charged again.", order, latest);
        }

        PaymentAttempt attempt;
        if (latest is not null && latest.NeedsSettlement(now))
        {
            // The previous attempt's outcome is unknown: resend it with ITS idempotency key, never a new one,
            // so a charge that did land is returned instead of repeated.
            attempt = latest;
            attempt.MarkResending(now);
            await _attemptRepository.UpdateAsync(attempt, CancellationToken.None);
            _logger.LogWarning("Settling payment attempt {AttemptId} for order {OrderId} by resending with its original idempotency key.", attempt.Id, order.Id);
        }
        else if (latest is { Status: PaymentAttemptStatus.InFlight })
        {
            return new(PayOrderOutcome.PaymentInProgress, "A payment for this order is already being processed. Check the order status before trying again.", order, latest);
        }
        else
        {
            var currency = string.IsNullOrEmpty(order.Currency) ? _gateway.Currency : order.Currency;
            var amount = order.Total();
            if (!MinorUnits.TryConvert(amount, currency, out var minorUnits) || minorUnits <= 0)
                return new(PayOrderOutcome.CannotCharge, $"The order total {amount} cannot be charged in {currency}.", order);

            attempt = new PaymentAttempt(order.Id, attempts.Count + 1, minorUnits, amount, currency, now);
            if (!await _stateStore.TryClaimAsync(attempt, cancellationToken))
            {
                return new(PayOrderOutcome.PaymentInProgress, "A payment for this order is already being processed. Check the order status before trying again.", order);
            }

            await UpdateOrderAsync(order, o =>
            {
                if (o.IsPaid) return false;
                o.AssignCurrencyIfMissing(currency);
                o.MarkPaymentPending();
                return true;
            });
        }

        return await ChargeAndRecordAsync(order, attempt, command);
    }

    private async Task<PayOrderResult> ChargeAndRecordAsync(Order order, PaymentAttempt attempt, PayOrderCommand command)
    {
        ChargeResult result;
        using (var deadline = new CancellationTokenSource(_processorBudget, _clock))
        {
            result = await _gateway.ChargeAsync(new ChargeCommand(
                attempt.IdempotencyKey,
                attempt.MerchantReference,
                attempt.AmountMinorUnits,
                attempt.Currency,
                command.Card,
                command.ReturnUrl), deadline.Token);
        }

        // From here on the processor has answered (or not); recording must not be abandoned because the caller left.
        var now = _clock.GetUtcNow();
        switch (result.Status)
        {
            case ChargeStatus.Authorised:
                attempt.MarkAuthorised(result.PspReference!, result.ResultCode ?? "Authorised", now);
                await _attemptRepository.UpdateAsync(attempt, CancellationToken.None);
                await RecordPaidAsync(order, attempt);
                _logger.LogInformation("Order {OrderId} paid: attempt {AttemptId}, pspReference {PspReference}, {Amount} {Currency}.", order.Id, attempt.Id, attempt.PspReference!, attempt.Amount, attempt.Currency);
                return new(PayOrderOutcome.Paid, "Payment authorised. Thank you!", order, attempt);

            case ChargeStatus.Declined:
            case ChargeStatus.Invalid:
            case ChargeStatus.ProviderError:
                attempt.MarkRefused(result.PspReference, result.ResultCode, result.RefusalReason ?? result.ShopperMessage, result.RefusalReasonCode, now);
                await _attemptRepository.UpdateAsync(attempt, CancellationToken.None);
                await ReopenForPaymentAsync(order);
                _logger.LogWarning("Payment attempt {AttemptId} for order {OrderId} not completed: {Status} ({ResultCode}, {RefusalReasonCode}).", attempt.Id, order.Id, result.Status, result.ResultCode ?? "-", result.RefusalReasonCode ?? "-");
                return new(result.Status switch
                {
                    ChargeStatus.Declined => PayOrderOutcome.Declined,
                    ChargeStatus.Invalid => PayOrderOutcome.InvalidCard,
                    _ => PayOrderOutcome.ProviderUnavailable
                }, result.ShopperMessage, order, attempt);

            case ChargeStatus.Reversed:
            case ChargeStatus.ReversalUnknown:
                attempt.MarkReversed(result.PspReference, result.ResultCode, result.Status == ChargeStatus.Reversed, result.ShopperMessage, now);
                await _attemptRepository.UpdateAsync(attempt, CancellationToken.None);
                await ReopenForPaymentAsync(order);
                return new(PayOrderOutcome.Declined, result.ShopperMessage, order, attempt);

            default:
                attempt.MarkUnknown(now);
                await _attemptRepository.UpdateAsync(attempt, CancellationToken.None);
                _logger.LogWarning("Payment attempt {AttemptId} for order {OrderId} has an unknown outcome (timed out: {TimedOut}); it will be settled with the same idempotency key.", attempt.Id, order.Id, result.TimedOut);
                return new(result.TimedOut ? PayOrderOutcome.ProcessorTimeout : PayOrderOutcome.OutcomeUnknown, result.ShopperMessage, order, attempt);
        }
    }

    private Task RecordPaidAsync(Order order, PaymentAttempt attempt) =>
        UpdateOrderAsync(order, o =>
        {
            if (o.IsPaid) return false;
            o.MarkPaid(attempt.PspReference!, attempt.Amount, attempt.UpdatedDate);
            return true;
        });

    private Task ReopenForPaymentAsync(Order order) =>
        UpdateOrderAsync(order, o =>
        {
            if (o.IsPaid) return false;
            o.MarkAwaitingPayment();
            return true;
        });

    public async Task<RefundOrderResult> RefundOrderAsync(RefundOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(command.OrderId), cancellationToken);
        if (order is null)
            return new(RefundOrderOutcome.OrderNotFound, $"Order {command.OrderId} was not found.");
        if (!order.IsPaid || string.IsNullOrEmpty(order.PaymentPspReference))
            return new(RefundOrderOutcome.OrderNotPaid, "Only a paid order can be refunded.", order);

        using var deadline = new CancellationTokenSource(_processorBudget, _clock);
        var refunds = await _refundRepository.ListAsync(new RefundsForOrderSpec(order.Id), cancellationToken);

        // Settle refunds whose outcome is unknown first, with their own idempotency keys: they still hold a
        // reservation on the order, and the refundable balance must be known before a new refund is judged.
        foreach (var unsettled in refunds.Where(r => r.NeedsSettlement(_clock.GetUtcNow())).ToList())
        {
            _logger.LogWarning("Settling refund {RefundId} on order {OrderId} by resending with its original idempotency key.", unsettled.Id, order.Id);
            await SubmitRefundAsync(order, unsettled, deadline.Token);
        }

        var refundId = OrderRefund.NewId(order.Id, command.IdempotencyKey);
        var replay = refunds.FirstOrDefault(r => r.Id == refundId);
        if (replay is not null)
            return Replay(order, replay, command);

        if (deadline.IsCancellationRequested)
            return new(RefundOrderOutcome.ProcessorTimeout, "Adyen did not respond while earlier refunds on this order were being confirmed. No new refund was submitted; try again shortly.", order);

        var amount = command.Amount ?? order.RefundableAmount;
        if (order.RefundableAmount <= 0m)
            return new(RefundOrderOutcome.ExceedsRefundable, "Nothing is left to refund on this order.", order);
        if (amount <= 0m)
            return new(RefundOrderOutcome.InvalidAmount, "The refund amount must be greater than zero.", order);
        if (!MinorUnits.TryConvert(amount, order.Currency, out var minorUnits))
            return new(RefundOrderOutcome.InvalidAmount, $"The refund amount {amount} has more decimals than {order.Currency} allows.", order);
        if (amount > order.RefundableAmount)
            return new(RefundOrderOutcome.ExceedsRefundable, $"The refund of {amount} {order.Currency} exceeds the {order.RefundableAmount} {order.Currency} still refundable on this order.", order);

        var refund = new OrderRefund(refundId, order.Id, amount, minorUnits, order.Currency, command.RequestedBy, command.Reason, _clock.GetUtcNow());
        if (!await _stateStore.TryClaimAsync(refund, cancellationToken))
        {
            // A concurrent request with the same idempotency key won the claim.
            var winner = await _refundRepository.GetByIdAsync(refundId, cancellationToken);
            return winner is null
                ? new(RefundOrderOutcome.RefundInProgress, "A refund with this idempotency key is already being processed.", order)
                : Replay(order, winner, command);
        }

        // Reserve the amount on the order under its concurrency stamp; a concurrent refund that reserved first
        // forces a re-read and a re-check against the new refundable balance.
        var reserved = await UpdateOrderAsync(order, o =>
        {
            if (!o.IsPaid || amount > o.RefundableAmount) return false;
            o.ReserveRefund(amount);
            return true;
        });
        if (!reserved)
        {
            refund.MarkFailed("Exceeded the refundable balance after a concurrent refund.", _clock.GetUtcNow());
            await _refundRepository.UpdateAsync(refund, CancellationToken.None);
            return new(RefundOrderOutcome.ExceedsRefundable, $"The refund of {amount} {order.Currency} exceeds the {order.RefundableAmount} {order.Currency} still refundable on this order.", order, refund);
        }

        return await SubmitRefundAsync(order, refund, deadline.Token);
    }

    private RefundOrderResult Replay(Order order, OrderRefund existing, RefundOrderCommand command)
    {
        if (command.Amount is { } requested && requested != existing.Amount)
            return new(RefundOrderOutcome.IdempotencyKeyReused, $"This idempotency key was already used for a refund of {existing.Amount} {existing.Currency}.", order, existing);

        return existing.Status switch
        {
            RefundStatus.Received => new(RefundOrderOutcome.AlreadySubmitted, "This refund was already submitted; it was not repeated.", order, existing),
            RefundStatus.Failed => new(RefundOrderOutcome.Rejected, existing.FailureReason ?? "This refund was rejected.", order, existing),
            RefundStatus.Unknown => new(RefundOrderOutcome.OutcomeUnknown, "Adyen has not yet confirmed this refund. Repeat the request to confirm it; it will not be refunded twice.", order, existing),
            _ => new(RefundOrderOutcome.RefundInProgress, "This refund is already being processed.", order, existing)
        };
    }

    private async Task<RefundOrderResult> SubmitRefundAsync(Order order, OrderRefund refund, CancellationToken deadline)
    {
        if (refund.Status != RefundStatus.InFlight || refund.NeedsSettlement(_clock.GetUtcNow()))
        {
            refund.MarkResending(_clock.GetUtcNow());
            await _refundRepository.UpdateAsync(refund, CancellationToken.None);
        }

        var result = await _gateway.RefundAsync(new RefundCommand(
            order.PaymentPspReference!,
            refund.IdempotencyKey,
            refund.Id,
            refund.AmountMinorUnits,
            refund.Currency), deadline);

        var now = _clock.GetUtcNow();
        switch (result.Status)
        {
            case RefundGatewayStatus.Received:
                refund.MarkReceived(result.PspReference!, now);
                await _refundRepository.UpdateAsync(refund, CancellationToken.None);
                _logger.LogInformation("Refund {RefundId} of {Amount} {Currency} on order {OrderId} accepted: pspReference {PspReference}.", refund.Id, refund.Amount, refund.Currency, order.Id, refund.PspReference!);
                return new(RefundOrderOutcome.Submitted, "Refund submitted to Adyen.", order, refund);

            case RefundGatewayStatus.Rejected:
            case RefundGatewayStatus.ProviderError:
                refund.MarkFailed(result.Message, now);
                await _refundRepository.UpdateAsync(refund, CancellationToken.None);
                await UpdateOrderAsync(order, o =>
                {
                    o.ReleaseRefund(refund.Amount);
                    return true;
                });
                _logger.LogWarning("Refund {RefundId} on order {OrderId} not accepted: {Status}.", refund.Id, order.Id, result.Status);
                return new(result.Status == RefundGatewayStatus.Rejected ? RefundOrderOutcome.Rejected : RefundOrderOutcome.ProviderUnavailable, result.Message, order, refund);

            default:
                // Keep the reservation: releasing it could let a later refund push the total past what was paid.
                refund.MarkUnknown(now);
                await _refundRepository.UpdateAsync(refund, CancellationToken.None);
                _logger.LogWarning("Refund {RefundId} on order {OrderId} has an unknown outcome (timed out: {TimedOut}); it will be settled with the same idempotency key.", refund.Id, order.Id, result.TimedOut);
                return new(result.TimedOut ? RefundOrderOutcome.ProcessorTimeout : RefundOrderOutcome.OutcomeUnknown, result.Message, order, refund);
        }
    }

    public async Task<IReadOnlyList<OrderPaymentView>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0) return Array.Empty<OrderPaymentView>();

        var ids = orders.Select(o => o.Id).ToArray();
        var attempts = await _attemptRepository.ListAsync(new PaymentAttemptsForOrdersSpec(ids), cancellationToken);
        var refunds = await _refundRepository.ListAsync(new RefundsForOrdersSpec(ids), cancellationToken);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => new OrderPaymentView(
                o,
                attempts.Where(a => a.OrderId == o.Id).ToList(),
                refunds.Where(r => r.OrderId == o.Id).ToList()))
            .ToList();
    }

    /// <summary>
    /// Applies <paramref name="change"/> and saves under the order's concurrency stamp, re-reading and
    /// re-applying when another request changed the order first. Returns false when <paramref name="change"/>
    /// declines to apply to the current state.
    /// </summary>
    private async Task<bool> UpdateOrderAsync(Order order, Func<Order, bool> change)
    {
        for (var attempt = 0; attempt < MaxConcurrencyRetries; attempt++)
        {
            if (!change(order)) return false;
            if (await _stateStore.TrySaveOrderAsync(order, CancellationToken.None)) return true;
        }

        throw new InvalidOperationException($"Order {order.Id} is being changed concurrently; giving up after {MaxConcurrencyRetries} attempts.");
    }
}
