using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and takes / gives back money for them.
/// Every provider write follows claim → provider call → record the result: the claim row is inserted first, so a
/// concurrent duplicate is refused by the store before it can reach the provider.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private const int MaxOrderLines = 100;
    private const int MaxQuantityPerLine = 1000;
    private const int MaxEncryptedFieldLength = 15000;
    private const int MaxHolderNameLength = 256;
    private const int MaxClientRequestIdLength = 64;

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IPaymentStore _paymentStore;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly PaymentOptions _options;
    private readonly TimeProvider _clock;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository,
        IPaymentStore paymentStore,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        PaymentOptions options,
        TimeProvider clock,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentStore = paymentStore;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public string Currency => _gateway.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address shipToAddress,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            throw new PaymentValidationException("An order needs at least one item.");
        if (lines.Count > MaxOrderLines)
            throw new PaymentValidationException($"An order can have at most {MaxOrderLines} lines.");
        if (lines.Any(l => l.CatalogItemId <= 0))
            throw new PaymentValidationException("Every item needs a valid catalogItemId.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxQuantityPerLine))
            throw new PaymentValidationException($"Every item quantity must be between 1 and {MaxQuantityPerLine}.");

        var merged = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
        if (merged.Any(l => l.Quantity > MaxQuantityPerLine))
            throw new PaymentValidationException($"Every item quantity must be between 1 and {MaxQuantityPerLine}.");

        var ids = merged.Select(l => l.CatalogItemId).ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = merged.Select(line =>
        {
            var catalogItem = catalogItems.First(c => c.Id == line.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var order = new Order(buyerId, shipToAddress, orderItems);

        // Refuse an order that could never be paid to the cent in the configured currency.
        if (CurrencyMinorUnits.ToMinor(order.Total(), Currency) <= 0)
            throw new PaymentValidationException("The order total must be greater than zero.");

        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} placed by {BuyerId}: total {Total} {Currency}, awaiting payment.",
            order.Id, buyerId, order.Total(), Currency);
        return order;
    }

    public async Task<PayOrderResult> PayAsync(int orderId, string buyerId, CardDetails card, CancellationToken cancellationToken)
    {
        ValidateCard(card);

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentsSpecification(orderId), cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            throw new OrderNotFoundException(orderId);

        var captured = order.CapturedPayment();
        if (captured is not null)
        {
            _logger.LogInformation("Order {OrderId} is already paid by attempt {PaymentId}; not charging again.", order.Id, captured.Id);
            return new PayOrderResult(PayOrderOutcome.AlreadyPaid, order, captured);
        }

        var now = _clock.GetUtcNow();
        OrderPayment attempt;
        var inFlight = order.InFlightPayment();
        if (inFlight is not null)
        {
            if (inFlight.Status == PaymentAttemptStatus.Processing && now - inFlight.UpdatedAt < _options.StaleClaimAfter)
                throw new PaymentConflictException("A payment for this order is already being processed. Check the order status before paying again.");

            // An earlier attempt may or may not have charged the card. Re-send it under its own idempotency key:
            // the provider either reports what it already did or processes it once now — never a second charge.
            _logger.LogWarning("Order {OrderId}: settling payment attempt {PaymentId} ({Status}) by re-sending it with its idempotency key.",
                order.Id, inFlight.Id, inFlight.Status);
            attempt = inFlight;
        }
        else
        {
            var total = order.Total();
            var amountMinor = CurrencyMinorUnits.ToMinor(total, Currency);
            attempt = new OrderPayment(order.Id, order.Payments.Count + 1, Currency, amountMinor, total, now);
            await _paymentStore.AddPaymentClaimAsync(attempt, cancellationToken);
        }

        using var budget = new CancellationTokenSource(_options.ProviderTimeBudget, _clock);
        return await ChargeAsync(order, attempt, card, budget.Token);
    }

    private async Task<PayOrderResult> ChargeAsync(Order order, OrderPayment attempt, CardDetails card, CancellationToken deadline)
    {
        _logger.LogInformation("Order {OrderId}: charging {AmountMinor} (minor units) {Currency} with attempt {PaymentId}, reference {Reference}.",
            order.Id, attempt.AmountMinor, attempt.Currency, attempt.Id, attempt.MerchantReference);

        CardPaymentResult result;
        try
        {
            result = await _gateway.ChargeCardAsync(
                new CardPaymentRequest(order.Id, attempt.IdempotencyKey, attempt.MerchantReference, attempt.Currency, attempt.AmountMinor, card),
                deadline);
        }
        catch (PaymentGatewayException ex)
        {
            var failedAt = _clock.GetUtcNow();
            if (ex.Failure == PaymentGatewayFailure.OutcomeUnknown)
            {
                // The provider may have charged the card. Keep the claim as Unknown: it blocks new attempts and the
                // next pay request re-sends it with the same idempotency key to settle it.
                attempt.MarkUnknown(
                    "We could not confirm this payment with the payment provider. Paying again is safe: you will not be charged twice.",
                    failedAt);
            }
            else
            {
                attempt.MarkFailed(null, null, ex.Message, failedAt);
            }
            // Record the outcome even if the caller has gone away.
            await _paymentStore.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Order {OrderId}: payment attempt {PaymentId} ended as {Status} ({Failure}, provider status {ProviderStatus}, provider code {ProviderCode}, provider reference {ProviderReference}).",
                order.Id, attempt.Id, attempt.Status, ex.Failure, ex.ProviderStatusCode?.ToString() ?? "none",
                ex.ProviderErrorCode ?? "none", ex.ProviderReference ?? "none");
            throw;
        }

        var now = _clock.GetUtcNow();
        PayOrderOutcome outcome;
        switch (result.Outcome)
        {
            case CardPaymentOutcome.Authorised:
                attempt.MarkAuthorised(result.PspReference, result.ResultCode, now);
                outcome = PayOrderOutcome.Paid;
                if (result.ChargedAmountMinor is not null &&
                    (result.ChargedAmountMinor != attempt.AmountMinor ||
                     !string.Equals(result.ChargedCurrency, attempt.Currency, StringComparison.OrdinalIgnoreCase)))
                {
                    _logger.LogError(null, "Order {OrderId}: provider reports {ChargedAmount} {ChargedCurrency} for attempt {PaymentId} but {AmountMinor} {Currency} was requested (psp {PspReference}).",
                        order.Id, result.ChargedAmountMinor, result.ChargedCurrency ?? "?", attempt.Id, attempt.AmountMinor, attempt.Currency,
                        result.PspReference ?? "none");
                }
                break;
            case CardPaymentOutcome.Refused:
                attempt.MarkRefused(result.PspReference, result.ResultCode, result.RefusalReason, result.RefusalReasonCode,
                    DescribeRefusal(result.RefusalReason), now);
                outcome = PayOrderOutcome.Declined;
                break;
            case CardPaymentOutcome.Error:
                attempt.MarkRefused(result.PspReference, result.ResultCode, result.RefusalReason, result.RefusalReasonCode,
                    $"The payment could not be processed{FormatReason(result.RefusalReason)}. Check the card details and try again, or use a different card.",
                    now);
                outcome = PayOrderOutcome.Declined;
                break;
            case CardPaymentOutcome.Cancelled:
                attempt.MarkRefused(result.PspReference, result.ResultCode, result.RefusalReason, result.RefusalReasonCode,
                    "The payment was cancelled before it completed. You were not charged; you can try again.", now);
                outcome = PayOrderOutcome.Declined;
                break;
            case CardPaymentOutcome.Pending:
                attempt.MarkPending(result.PspReference, result.ResultCode, now);
                outcome = PayOrderOutcome.Pending;
                break;
            case CardPaymentOutcome.ActionRequired:
                attempt.MarkFailed(result.PspReference, result.ResultCode,
                    "Your card issuer requires an extra verification step (such as 3-D Secure) that this checkout does not support. You were not charged; please use a different card.",
                    now);
                outcome = PayOrderOutcome.Declined;
                break;
            default:
                _logger.LogError(null, "Order {OrderId}: unexpected result {ResultCode} for attempt {PaymentId} (psp {PspReference}); recorded as failed.",
                    order.Id, result.ResultCode, attempt.Id, result.PspReference ?? "none");
                attempt.MarkFailed(result.PspReference, result.ResultCode,
                    "The payment could not be completed. Please try again or use a different card.", now);
                outcome = PayOrderOutcome.Declined;
                break;
        }

        await _paymentStore.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Order {OrderId}: payment attempt {PaymentId} result {ResultCode} -> {Status} (psp {PspReference}).",
            order.Id, attempt.Id, result.ResultCode, attempt.Status, attempt.PspReference ?? "none");
        return new PayOrderResult(outcome, order, attempt);
    }

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string? reason, string requestedBy,
        string? clientRequestId, CancellationToken cancellationToken)
    {
        var normalizedReason = RefundReasons.Normalize(reason);
        if (amount is not null && amount <= 0)
            throw new PaymentValidationException("The refund amount must be greater than zero.");
        if (clientRequestId is not null && (clientRequestId.Length == 0 || clientRequestId.Length > MaxClientRequestIdLength))
            throw new PaymentValidationException($"The Idempotency-Key must be 1 to {MaxClientRequestIdLength} characters.");

        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentsSpecification(orderId), cancellationToken);
        if (order is null)
            throw new OrderNotFoundException(orderId);

        if (clientRequestId is not null)
        {
            var earlier = order.Refunds.FirstOrDefault(r => r.ClientRequestId == clientRequestId);
            if (earlier is not null)
            {
                if (amount is not null && CurrencyMinorUnits.ToMinor(amount.Value, earlier.Currency) != earlier.AmountMinor)
                    throw new PaymentConflictException("This Idempotency-Key was already used for a refund of a different amount.");
                return new RefundOrderResult(order, earlier, Replayed: true);
            }
        }

        var payment = order.CapturedPayment()
            ?? throw new PaymentConflictException($"Order {orderId} has not been paid, so there is nothing to refund.");

        using var budget = new CancellationTokenSource(_options.ProviderTimeBudget, _clock);
        await SettleUnresolvedRefundsAsync(order, budget.Token);

        var refundable = order.RefundableMinor();
        if (refundable <= 0)
            throw new PaymentConflictException($"Order {orderId} has already been refunded in full.");

        var amountMinor = amount is null ? refundable : CurrencyMinorUnits.ToMinor(amount.Value, payment.Currency);
        if (amountMinor > refundable)
        {
            throw new PaymentConflictException(
                $"A refund of {CurrencyMinorUnits.FromMinor(amountMinor, payment.Currency)} {payment.Currency} would exceed the " +
                $"{CurrencyMinorUnits.FromMinor(refundable, payment.Currency)} {payment.Currency} that can still be refunded on order {orderId}.");
        }

        var refund = new OrderRefund(order.Id, order.Refunds.Count + 1, payment, amountMinor,
            CurrencyMinorUnits.FromMinor(amountMinor, payment.Currency), normalizedReason, requestedBy, clientRequestId, _clock.GetUtcNow());
        await _paymentStore.AddRefundClaimAsync(refund, cancellationToken);

        await SendRefundAsync(order, refund, budget.Token);
        return new RefundOrderResult(order, refund, Replayed: false);
    }

    private async Task SettleUnresolvedRefundsAsync(Order order, CancellationToken deadline)
    {
        var now = _clock.GetUtcNow();
        foreach (var refund in order.Refunds.Where(r => r.IsUnsettled).OrderBy(r => r.Sequence).ToList())
        {
            if (refund.Status == RefundStatus.Processing && now - refund.UpdatedAt < _options.StaleClaimAfter)
                throw new PaymentConflictException("Another refund on this order is being processed. Try again in a moment.");

            // Re-send under the refund's own idempotency key: the provider reports what it already did, or does it once now.
            _logger.LogWarning("Order {OrderId}: settling refund {RefundId} ({Status}) by re-sending it with its idempotency key.",
                order.Id, refund.Id, refund.Status);
            await SendRefundAsync(order, refund, deadline);
        }
    }

    private async Task SendRefundAsync(Order order, OrderRefund refund, CancellationToken deadline)
    {
        _logger.LogInformation("Order {OrderId}: refunding {AmountMinor} (minor units) {Currency} of payment {PspReference} with refund {RefundId}, reference {Reference}.",
            order.Id, refund.AmountMinor, refund.Currency, refund.PaymentPspReference, refund.Id, refund.MerchantReference);
        try
        {
            var result = await _gateway.RefundAsync(
                new ProviderRefundRequest(refund.PaymentPspReference, refund.IdempotencyKey, refund.MerchantReference, refund.Currency,
                    refund.AmountMinor, refund.Reason),
                deadline);
            refund.MarkReceived(result.PspReference, _clock.GetUtcNow());
        }
        catch (PaymentGatewayException ex)
        {
            if (ex.Failure == PaymentGatewayFailure.OutcomeUnknown)
            {
                // The provider may have accepted it: keep counting it against the payment and settle it on the next refund request.
                refund.MarkUnknown("The refund could not be confirmed with the payment provider; it is settled on the next refund request for this order.",
                    _clock.GetUtcNow());
            }
            else
            {
                refund.MarkRejected(ex.Message, _clock.GetUtcNow());
            }
            await _paymentStore.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning("Order {OrderId}: refund {RefundId} ended as {Status} ({Failure}, provider status {ProviderStatus}, provider code {ProviderCode}, provider reference {ProviderReference}).",
                order.Id, refund.Id, refund.Status, ex.Failure, ex.ProviderStatusCode?.ToString() ?? "none",
                ex.ProviderErrorCode ?? "none", ex.ProviderReference ?? "none");
            throw;
        }

        await _paymentStore.SaveChangesAsync(CancellationToken.None);
        _logger.LogInformation("Order {OrderId}: refund {RefundId} accepted by the provider (psp {PspReference}).",
            order.Id, refund.Id, refund.PspReference ?? "none");
    }

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpecification(buyerId), cancellationToken);
    }

    private static void ValidateCard(CardDetails? card)
    {
        if (card is null)
            throw new PaymentValidationException("Card details are required.");

        RequireField(card.EncryptedCardNumber, "encryptedCardNumber", MaxEncryptedFieldLength);
        RequireField(card.EncryptedExpiryMonth, "encryptedExpiryMonth", MaxEncryptedFieldLength);
        RequireField(card.EncryptedExpiryYear, "encryptedExpiryYear", MaxEncryptedFieldLength);
        RequireField(card.EncryptedSecurityCode, "encryptedSecurityCode", MaxEncryptedFieldLength);
        RequireField(card.HolderName, "holderName", MaxHolderNameLength);
    }

    private static void RequireField(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new PaymentValidationException($"{name} is required.");
        if (value.Length > maxLength)
            throw new PaymentValidationException($"{name} must be at most {maxLength} characters.");
    }

    private static string DescribeRefusal(string? refusalReason) =>
        $"Your card was declined{FormatReason(refusalReason)}. You were not charged. Check the card number, expiry date and security code, " +
        "or use a different card.";

    private static string FormatReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? string.Empty : $" (reason: {reason})";
}
