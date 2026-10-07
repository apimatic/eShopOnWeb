using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Places orders and moves money for them. Every provider write follows one order:
/// claim (a row the store refuses to insert twice) → provider call → record the result.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    public const int MaxQuantityPerLine = 1000;
    public const int MaxClientRequestKeyLength = 64;

    private readonly IOrderPaymentStore _store;
    private readonly IPaymentGateway _gateway;
    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogRepository;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _time;
    private readonly PaymentProcessingOptions _options;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IOrderPaymentStore store, IPaymentGateway gateway, IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogRepository, IUriComposer uriComposer, TimeProvider time,
        PaymentProcessingOptions options, IAppLogger<OrderPaymentService> logger)
    {
        _store = store;
        _gateway = gateway;
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _uriComposer = uriComposer;
        _time = time;
        _options = options;
        _logger = logger;
    }

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<PlaceOrderLine>? lines,
        Address? shipToAddress, CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return Invalid("An order needs at least one item.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxQuantityPerLine))
            return Invalid($"Each quantity must be between 1 and {MaxQuantityPerLine}.");

        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (quantities.Values.Any(q => q > MaxQuantityPerLine))
            return Invalid($"Each quantity must be between 1 and {MaxQuantityPerLine}.");

        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);
        var unknownIds = quantities.Keys.Except(catalogItems.Select(c => c.Id)).OrderBy(id => id).ToList();
        if (unknownIds.Count > 0)
            return Invalid($"Unknown catalog item id(s): {string.Join(", ", unknownIds)}.");

        var items = catalogItems
            .Select(c => new OrderItem(
                new CatalogItemOrdered(c.Id, c.Name, _uriComposer.ComposePicUri(c.PictureUri)),
                c.Price,
                quantities[c.Id]))
            .ToList();
        var order = new Order(buyerId, shipToAddress ?? new Address("", "", "", "", ""), items);

        var currency = _gateway.Currency;
        if (!MinorUnits.TryToMinorUnits(order.Total(), currency, out var totalMinor) || totalMinor <= 0)
            return Invalid($"The order total {order.Total()} cannot be charged in {currency}.");

        order = await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} placed by API for {Total} {Currency}, awaiting payment.", order.Id, order.Total(), currency);
        return new PlaceOrderResult(PlaceOrderStatus.Created, order.Id, order.Total(), currency, null);

        static PlaceOrderResult Invalid(string message) => new(PlaceOrderStatus.Invalid, null, null, null, message);
    }

    public async Task<PayOrderResult> PayAsync(string buyerId, int orderId, EncryptedCard card, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(card.EncryptedCardNumber) || string.IsNullOrWhiteSpace(card.EncryptedExpiryMonth)
            || string.IsNullOrWhiteSpace(card.EncryptedExpiryYear) || string.IsNullOrWhiteSpace(card.EncryptedSecurityCode)
            || string.IsNullOrWhiteSpace(card.HolderName))
        {
            return new PayOrderResult(PayOrderStatus.Invalid, orderId,
                "The encrypted card number, expiry month, expiry year, security code and the holder's name are all required.");
        }

        var order = await _store.GetOrderAsync(orderId, cancellationToken);
        if (order is null || order.BuyerId != buyerId)
            return new PayOrderResult(PayOrderStatus.NotFound, orderId, $"Order {orderId} was not found.");

        if (order.AuthorisedPayment is { } paid)
            return AlreadyPaid(order, paid);

        var now = _time.GetUtcNow();
        var latest = order.LatestPaymentAttempt;
        PaymentAttempt? settles = null;
        switch (latest?.Status)
        {
            case PaymentAttemptStatus.InFlight when !latest.IsStaleClaim(now, _options.StaleClaimAfter):
                return InProgress(order);
            case PaymentAttemptStatus.InFlight:
            case PaymentAttemptStatus.Unknown:
                // The earlier attempt may have charged the card. Re-send it with the same idempotency key so the
                // provider answers with that original result instead of charging a second time.
                settles = latest;
                break;
            case PaymentAttemptStatus.Pending:
            case PaymentAttemptStatus.PartiallyAuthorised:
                return new PayOrderResult(PayOrderStatus.Pending, orderId,
                    "A payment for this order is already being processed by the payment provider. No new payment was started.",
                    order.PaymentStatus, latest.PspReference, latest.Amount, latest.Currency);
        }

        long amountInMinorUnits;
        string currency;
        if (settles is not null)
        {
            amountInMinorUnits = settles.AmountInMinorUnits;
            currency = settles.Currency;
        }
        else
        {
            currency = _gateway.Currency;
            if (!MinorUnits.TryToMinorUnits(order.Total(), currency, out amountInMinorUnits) || amountInMinorUnits <= 0)
                return new PayOrderResult(PayOrderStatus.NotPayable, orderId,
                    $"The order total {order.Total()} cannot be charged in {currency}.");
        }

        // 1. Claim. A concurrent pay call computes the same attempt number; the store refuses its insert.
        var attempt = order.StartPaymentAttempt(amountInMinorUnits, currency, now, settles);
        if (!await _store.TrySaveClaimAsync(order, cancellationToken))
            return InProgress(order);

        // 2. Provider call, bounded by one budget for the whole request.
        var request = new PaymentAuthorisationRequest(order.Id, attempt.Reference, attempt.IdempotencyKey,
            attempt.AmountInMinorUnits, attempt.Currency, card);
        var responses = new List<CapturedProviderResponse>();
        var result = await AuthoriseSettlingUnknownAsync(request, responses);

        // 3. Record.
        var status = ToAttemptStatus(result, attempt);
        order.RecordPaymentOutcome(attempt, status, result.PspReference, result.ResultCode, result.RefusalReason,
            result.RefusalReasonCode, result.ErrorCode, result.ErrorMessage, responses, _time.GetUtcNow());
        await _store.SaveAsync(order, CancellationToken.None);

        _logger.LogInformation("Payment attempt {AttemptNumber} for order {OrderId} ({Reference}): {Status}, pspReference {PspReference}, resultCode {ResultCode}, errorCode {ErrorCode}.",
            attempt.AttemptNumber, order.Id, attempt.Reference, status, result.PspReference ?? "-", result.ResultCode ?? "-", result.ErrorCode ?? "-");

        return ToPayResult(order, attempt, status, result);
    }

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal amount, RefundReason? reason,
        string? clientRequestKey, CancellationToken cancellationToken)
    {
        if (clientRequestKey is not null && (clientRequestKey.Length == 0 || clientRequestKey.Length > MaxClientRequestKeyLength))
            return new RefundOrderResult(RefundOrderStatus.Invalid, orderId,
                $"The idempotency key must be 1 to {MaxClientRequestKeyLength} characters.");

        var order = await _store.GetOrderAsync(orderId, cancellationToken);
        if (order is null)
            return new RefundOrderResult(RefundOrderStatus.NotFound, orderId, $"Order {orderId} was not found.");

        var budget = new RequestBudget(_time, _options.RequestBudget);

        if (clientRequestKey is not null && order.Refunds.FirstOrDefault(r => r.ClientRequestKey == clientRequestKey) is { } existing)
        {
            if (existing.NeedsSettlement(_time.GetUtcNow(), _options.StaleClaimAfter) && budget.Remaining >= _options.MinimumCallWindow)
            {
                await SettleRefundAsync(order, existing, budget);
            }
            return ToRefundResult(order, existing, RefundOrderStatus.Replayed, "This refund was already requested with the same idempotency key.");
        }

        var payment = order.AuthorisedPayment;
        if (payment is null)
            return new RefundOrderResult(RefundOrderStatus.NotRefundable, orderId, $"Order {orderId} has no payment to refund.");

        // Settle refunds whose outcome is unknown first, so what can still be refunded is known exactly.
        foreach (var unsettled in order.Refunds.Where(r => r.NeedsSettlement(_time.GetUtcNow(), _options.StaleClaimAfter)).ToList())
        {
            if (budget.Remaining < _options.MinimumCallWindow) break;
            await SettleRefundAsync(order, unsettled, budget);
        }

        if (amount <= 0 || !MinorUnits.TryToMinorUnits(amount, payment.Currency, out var amountInMinorUnits) || amountInMinorUnits <= 0)
            return new RefundOrderResult(RefundOrderStatus.Invalid, orderId,
                $"The refund amount must be positive and have at most {MinorUnits.ExponentOf(payment.Currency)} decimal places for {payment.Currency}.");

        var refundable = order.RefundableMinorUnits;
        if (amountInMinorUnits > refundable)
        {
            var remaining = MinorUnits.FromMinorUnits(refundable, payment.Currency);
            return new RefundOrderResult(RefundOrderStatus.ExceedsRefundable, orderId,
                $"The order can be refunded at most {remaining} {payment.Currency} more (paid {payment.Amount} {payment.Currency}).",
                Currency: payment.Currency, RemainingRefundable: remaining);
        }

        if (budget.Remaining < _options.MinimumCallWindow)
            return new RefundOrderResult(RefundOrderStatus.ProviderDidNotRespond, orderId,
                "Adyen did not respond while earlier refunds were being confirmed. No new refund was started; try again.");

        // 1. Claim. A concurrent refund on this order computes the same sequence number; the store refuses its insert.
        var refund = order.StartRefund(amount, amountInMinorUnits, reason, clientRequestKey, _time.GetUtcNow());
        if (!await _store.TrySaveClaimAsync(order, cancellationToken))
            return new RefundOrderResult(RefundOrderStatus.InProgress, orderId,
                "Another refund on this order is in progress. Check the order and try again.");

        // 2. Provider call, 3. record.
        var status = await SettleRefundAsync(order, refund, budget);

        _logger.LogInformation("Refund {Sequence} on order {OrderId} ({Reference}) for {Amount} {Currency}: {Status}, pspReference {PspReference}, errorCode {ErrorCode}.",
            refund.Sequence, order.Id, refund.Reference, refund.Amount, refund.Currency, status, refund.PspReference ?? "-", refund.ErrorCode ?? "-");

        return status switch
        {
            RefundStatus.Received => ToRefundResult(order, refund, RefundOrderStatus.Accepted,
                "Adyen accepted the refund. The money is on its way back to the shopper."),
            RefundStatus.Unknown => ToRefundResult(order, refund, RefundOrderStatus.ProviderDidNotRespond,
                "Adyen did not respond. The refund is recorded as unknown, its amount stays reserved, and it is confirmed with Adyen on the next refund request for this order."),
            _ when refund.ErrorCode == ProviderUnavailableCode => ToRefundResult(order, refund, RefundOrderStatus.ProviderUnavailable,
                "Refunds are temporarily unavailable. Nothing was refunded."),
            _ => ToRefundResult(order, refund, RefundOrderStatus.Rejected,
                $"Adyen rejected the refund: {refund.ErrorMessage ?? "no reason given"}. Nothing was refunded.")
        };
    }

    public Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        _store.ListBuyerOrdersAsync(buyerId, cancellationToken);

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        _store.GetOrderAsync(orderId, cancellationToken);

    private const string ProviderUnavailableCode = "provider-unavailable";

    /// <summary>
    /// Sends the payment; when no response arrives and budget is left, re-sends it once with the SAME idempotency
    /// key so the provider settles the outcome (returns the original result if the first send was processed).
    /// </summary>
    private async Task<PaymentAuthorisationResult> AuthoriseSettlingUnknownAsync(PaymentAuthorisationRequest request,
        List<CapturedProviderResponse> responses)
    {
        var budget = new RequestBudget(_time, _options.RequestBudget);
        using var deadline = new CancellationTokenSource(_options.RequestBudget, _time);

        var result = await _gateway.AuthoriseAsync(request, deadline.Token);
        responses.AddRange(result.Responses);
        if (result.Outcome == ProviderCallOutcome.Unknown && result.NoResponse && budget.Remaining >= _options.MinimumCallWindow)
        {
            _logger.LogWarning("No response from the payment provider for {Reference}; re-sending with the same idempotency key to settle the outcome.", request.Reference);
            result = await _gateway.AuthoriseAsync(request, deadline.Token);
            responses.AddRange(result.Responses);
        }
        return result;
    }

    /// <summary>
    /// Sends (or re-sends, with the same idempotency key) a refund and records the outcome. Re-sending once more
    /// within the budget when no response arrived.
    /// </summary>
    private async Task<RefundStatus> SettleRefundAsync(Order order, OrderRefund refund, RequestBudget budget)
    {
        var request = new ProviderRefundRequest(order.Id, refund.PaymentPspReference, refund.Reference, refund.IdempotencyKey,
            refund.AmountInMinorUnits, refund.Currency, Enum.TryParse<RefundReason>(refund.Reason, out var r) ? r : null);
        var responses = new List<CapturedProviderResponse>();

        using var deadline = new CancellationTokenSource(budget.Remaining > TimeSpan.Zero ? budget.Remaining : TimeSpan.FromMilliseconds(1), _time);
        var result = await _gateway.RefundAsync(request, deadline.Token);
        responses.AddRange(result.Responses);
        if (result.Outcome == ProviderCallOutcome.Unknown && result.NoResponse && budget.Remaining >= _options.MinimumCallWindow)
        {
            _logger.LogWarning("No response from the payment provider for refund {Reference}; re-sending with the same idempotency key to settle the outcome.", refund.Reference);
            result = await _gateway.RefundAsync(request, deadline.Token);
            responses.AddRange(result.Responses);
        }

        var status = result.Outcome switch
        {
            ProviderCallOutcome.Accepted => RefundStatus.Received,
            ProviderCallOutcome.Rejected or ProviderCallOutcome.ProviderUnavailable => RefundStatus.Rejected,
            _ => RefundStatus.Unknown
        };
        var errorCode = result.Outcome == ProviderCallOutcome.ProviderUnavailable ? ProviderUnavailableCode : result.ErrorCode;
        order.RecordRefundOutcome(refund, status, result.PspReference, errorCode, result.ErrorMessage, responses, _time.GetUtcNow());
        await _store.SaveAsync(order, CancellationToken.None);
        return status;
    }

    private PaymentAttemptStatus ToAttemptStatus(PaymentAuthorisationResult result, PaymentAttempt attempt)
    {
        switch (result.Outcome)
        {
            case ProviderCallOutcome.Authorised:
                var amountMatches = result.AuthorisedAmountInMinorUnits is null
                    || (result.AuthorisedAmountInMinorUnits == attempt.AmountInMinorUnits
                        && string.Equals(result.AuthorisedCurrency ?? attempt.Currency, attempt.Currency, StringComparison.OrdinalIgnoreCase));
                if (amountMatches) return PaymentAttemptStatus.Authorised;
                _logger.LogWarning("Payment {Reference} was authorised for {Amount} {Currency} instead of {Expected} {ExpectedCurrency}; needs operator attention.",
                    attempt.Reference, result.AuthorisedAmountInMinorUnits!, result.AuthorisedCurrency ?? "-", attempt.AmountInMinorUnits, attempt.Currency);
                return PaymentAttemptStatus.PartiallyAuthorised;
            case ProviderCallOutcome.Refused: return PaymentAttemptStatus.Refused;
            case ProviderCallOutcome.Pending: return PaymentAttemptStatus.Pending;
            case ProviderCallOutcome.ActionRequired: return PaymentAttemptStatus.ActionRequired;
            case ProviderCallOutcome.PartiallyAuthorised: return PaymentAttemptStatus.PartiallyAuthorised;
            case ProviderCallOutcome.Rejected:
            case ProviderCallOutcome.ProviderUnavailable: return PaymentAttemptStatus.Rejected;
            default: return PaymentAttemptStatus.Unknown;
        }
    }

    private static PayOrderResult ToPayResult(Order order, PaymentAttempt attempt, PaymentAttemptStatus status, PaymentAuthorisationResult result)
    {
        var id = order.Id;
        return status switch
        {
            PaymentAttemptStatus.Authorised => new PayOrderResult(PayOrderStatus.Paid, id,
                "Payment authorised. The order is paid.", order.PaymentStatus, attempt.PspReference, attempt.Amount, attempt.Currency),
            PaymentAttemptStatus.Refused => new PayOrderResult(PayOrderStatus.Refused, id,
                result.RefusalReason is { Length: > 0 } reason
                    ? $"Your card was declined ({reason}). Check the card details, or pay with a different card. The order has not been paid."
                    : "Your card was declined. Check the card details, or pay with a different card. The order has not been paid.",
                order.PaymentStatus, attempt.PspReference, attempt.Amount, attempt.Currency, result.RefusalReason),
            PaymentAttemptStatus.ActionRequired => new PayOrderResult(PayOrderStatus.ActionRequired, id,
                "Your card issuer requires additional verification (such as 3D Secure) that this checkout does not support. Pay with a different card. The order has not been paid.",
                order.PaymentStatus, attempt.PspReference, attempt.Amount, attempt.Currency),
            PaymentAttemptStatus.Pending or PaymentAttemptStatus.PartiallyAuthorised => new PayOrderResult(PayOrderStatus.Pending, id,
                "The payment is being processed by the payment provider; its final result is not known yet. Do not pay again — check the order status later.",
                order.PaymentStatus, attempt.PspReference, attempt.Amount, attempt.Currency),
            PaymentAttemptStatus.Rejected when result.Outcome == ProviderCallOutcome.ProviderUnavailable => new PayOrderResult(
                PayOrderStatus.ProviderUnavailable, id, "Card payments are temporarily unavailable. Nothing was charged; try again later.",
                order.PaymentStatus, Amount: attempt.Amount, Currency: attempt.Currency),
            PaymentAttemptStatus.Rejected => new PayOrderResult(PayOrderStatus.Rejected, id,
                $"The card details could not be processed ({result.ErrorMessage ?? "invalid card data"}). Re-enter the card details and try again. Nothing was charged.",
                order.PaymentStatus, attempt.PspReference, attempt.Amount, attempt.Currency),
            _ => new PayOrderResult(PayOrderStatus.ProviderDidNotRespond, id,
                result.NoResponse
                    ? "Adyen did not respond in time, so it is not known yet whether the payment went through. Do not use another card: send the same pay request again and it will be confirmed without charging twice."
                    : "Adyen could not confirm the payment, so it is not known yet whether it went through. Send the same pay request again and it will be confirmed without charging twice.",
                order.PaymentStatus, Amount: attempt.Amount, Currency: attempt.Currency)
        };
    }

    private static PayOrderResult AlreadyPaid(Order order, PaymentAttempt paid) =>
        new(PayOrderStatus.AlreadyPaid, order.Id, "The order is already paid. Nothing was charged again.",
            order.PaymentStatus, paid.PspReference, paid.Amount, paid.Currency);

    private static PayOrderResult InProgress(Order order) =>
        new(PayOrderStatus.InProgress, order.Id,
            "A payment for this order is already in progress. Wait for it to finish, then check the order.", order.PaymentStatus);

    private static RefundOrderResult ToRefundResult(Order order, OrderRefund refund, RefundOrderStatus status, string message) =>
        new(status, order.Id, message, refund.RefundId, refund.Status, refund.Amount, refund.Currency,
            MinorUnits.FromMinorUnits(order.RefundableMinorUnits, refund.Currency), refund.PspReference);

    /// <summary>Tracks how much of one API request's provider-time budget is left.</summary>
    private sealed class RequestBudget
    {
        private readonly TimeProvider _time;
        private readonly long _started;
        private readonly TimeSpan _total;

        public RequestBudget(TimeProvider time, TimeSpan total)
        {
            _time = time;
            _total = total;
            _started = time.GetTimestamp();
        }

        public TimeSpan Remaining => _total - _time.GetElapsedTime(_started);
    }
}
