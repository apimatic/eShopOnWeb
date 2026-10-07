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
/// Places orders from catalog items, takes the order total by card, and gives money back.
/// Every provider call follows one order: claim the order (refusing a concurrent caller), persist the
/// attempt with its idempotency key, call the provider, record what it returned.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    /// <summary>
    /// Total time one API request may spend waiting on the payment provider, across every provider call it
    /// makes. Kept under the 30 second ceiling callers are promised.
    /// </summary>
    public static readonly TimeSpan ProviderTimeBudget = TimeSpan.FromSeconds(25);

    public const int MaxUnitsPerLine = 100;

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _itemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IPaymentOperationLock _operationLock;
    private readonly IUriComposer _uriComposer;
    private readonly TimeProvider _timeProvider;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> itemRepository,
        IPaymentGateway gateway,
        IPaymentOperationLock operationLock,
        IUriComposer uriComposer,
        TimeProvider timeProvider,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _itemRepository = itemRepository;
        _gateway = gateway;
        _operationLock = operationLock;
        _uriComposer = uriComposer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public string Currency => _gateway.Currency;

    public async Task<PlaceOrderResult> PlaceOrderAsync(string buyerId, IReadOnlyCollection<OrderLine> lines, Address? shipToAddress,
        CancellationToken cancellationToken)
    {
        if (lines is null || lines.Count == 0)
            return PlaceOrderResult.Invalid("An order needs at least one item.");
        if (lines.Any(l => l.Quantity < 1 || l.Quantity > MaxUnitsPerLine))
            return PlaceOrderResult.Invalid($"Each item quantity must be between 1 and {MaxUnitsPerLine}.");

        var quantities = lines
            .GroupBy(l => l.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        if (quantities.Values.Any(q => q > MaxUnitsPerLine))
            return PlaceOrderResult.Invalid($"Each item quantity must be between 1 and {MaxUnitsPerLine}.");

        var catalogItems = await _itemRepository.ListAsync(new CatalogItemsSpecification(quantities.Keys.ToArray()), cancellationToken);
        var missing = quantities.Keys.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
            return PlaceOrderResult.Invalid($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        // Prices always come from the catalog, never from the caller.
        var items = catalogItems.Select(catalogItem =>
        {
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var order = new Order(buyerId, shipToAddress ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty), items);
        if (!MinorUnits.TryToMinor(order.Total(), _gateway.Currency, out var totalMinor) || totalMinor <= 0)
            return PlaceOrderResult.Invalid($"The order total {order.Total()} cannot be charged in {_gateway.Currency}.");

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Order {OrderId} placed by buyer with total {TotalMinor} {Currency} awaiting payment.",
            order.Id, totalMinor, _gateway.Currency);
        return PlaceOrderResult.Created(order);
    }

    public async Task<PayOrderResult> PayAsync(int orderId, string buyerId, EncryptedCardDetails card, string returnUrl)
    {
        // Ownership check that tracks nothing, so the order is only loaded once the claim is held.
        if (!await _orderRepository.AnyAsync(new OrderOwnedByBuyerSpec(orderId, buyerId)))
            return new PayOrderResult(PayOrderOutcome.NotFound, null, null, "Order not found.");

        if (!await _operationLock.TryAcquireAsync(orderId, CancellationToken.None))
            return new PayOrderResult(PayOrderOutcome.Busy, null, null,
                "A payment for this order is already being processed. Wait a moment and check the order before trying again.");

        try
        {
            var order = (await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentsSpec(orderId)))!;

            if (order.AuthorisedPayment is { } existing)
                return new PayOrderResult(PayOrderOutcome.AlreadyPaid, order, existing, "This order has already been paid.");

            PaymentAttempt attempt;
            if (order.UnsettledPayment is { } unsettled)
            {
                // Same idempotency key as the original call: if the provider already processed it, it returns
                // that result instead of charging again.
                attempt = unsettled;
                _logger.LogWarning("Order {OrderId}: settling payment attempt {Reference} whose outcome is {Status}.",
                    order.Id, attempt.Reference, attempt.Status);
            }
            else
            {
                var currency = _gateway.Currency;
                if (!MinorUnits.TryToMinor(order.Total(), currency, out var amountMinor) || amountMinor <= 0)
                    return new PayOrderResult(PayOrderOutcome.NotPayable, order, null,
                        $"The order total cannot be charged in {currency}.");

                attempt = order.StartPaymentAttempt(Guid.NewGuid().ToString("N"), amountMinor, currency, _timeProvider.GetUtcNow());
                // The attempt and its key are stored before the provider is called, so a crash mid-call still
                // leaves a record that the next request settles.
                await _orderRepository.SaveChangesAsync();
            }

            using var budget = new CancellationTokenSource(ProviderTimeBudget, _timeProvider);
            var result = await _gateway.AuthoriseAsync(new PaymentAuthorisationRequest(
                attempt.Reference, $"ESHOP-{order.Id}", attempt.IdempotencyKey, attempt.AmountMinor, attempt.Currency, card, returnUrl),
                budget.Token);

            RecordPayment(order, attempt, result);
            await _orderRepository.SaveChangesAsync();

            return new PayOrderResult(ToPayOutcome(result), order, attempt, ShopperMessage(result));
        }
        finally
        {
            await _operationLock.ReleaseAsync(orderId);
        }
    }

    public async Task<RefundOrderResult> RefundAsync(int orderId, decimal? amount, string requestedBy)
    {
        if (!await _orderRepository.AnyAsync(new OrderWithPaymentsSpec(orderId)))
            return new RefundOrderResult(RefundOrderOutcome.NotFound, null, null, "Order not found.");

        if (!await _operationLock.TryAcquireAsync(orderId, CancellationToken.None))
            return new RefundOrderResult(RefundOrderOutcome.Busy, null, null,
                "Another payment operation on this order is in progress. Try again in a moment.");

        try
        {
            var order = (await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentsSpec(orderId)))!;
            var payment = order.AuthorisedPayment;
            if (payment is null)
                return new RefundOrderResult(RefundOrderOutcome.NotPaid, order, null, "The order has not been paid, so there is nothing to refund.");

            using var budget = new CancellationTokenSource(ProviderTimeBudget, _timeProvider);

            // Settle refunds whose outcome is unknown before sending a new one.
            foreach (var unsettled in order.Refunds.Where(r => r.IsUnsettled).ToList())
            {
                if (budget.IsCancellationRequested) break;
                _logger.LogWarning("Order {OrderId}: settling refund {Reference} whose outcome is {Status}.",
                    order.Id, unsettled.Reference, unsettled.Status);
                var settled = await _gateway.RefundAsync(ToGatewayRequest(unsettled), budget.Token);
                RecordRefund(order, unsettled, settled);
                await _orderRepository.SaveChangesAsync();
            }

            if (order.Refunds.FirstOrDefault(r => r.IsUnsettled) is { } stillUnsettled)
                return new RefundOrderResult(RefundOrderOutcome.PreviousRefundUnsettled, order, stillUnsettled,
                    "Adyen did not respond, so the outcome of an earlier refund on this order is still unconfirmed. No new refund was sent; retry shortly.");

            long amountMinor;
            if (amount is null)
            {
                amountMinor = order.RefundableAmountMinor;
            }
            else if (amount <= 0)
            {
                return new RefundOrderResult(RefundOrderOutcome.Invalid, order, null, "The refund amount must be greater than zero.");
            }
            else if (!MinorUnits.TryToMinor(amount.Value, payment.Currency, out amountMinor))
            {
                return new RefundOrderResult(RefundOrderOutcome.Invalid, order, null,
                    $"The refund amount {amount} is not a valid {payment.Currency} amount.");
            }

            if (order.RefundableAmountMinor <= 0)
                return new RefundOrderResult(RefundOrderOutcome.ExceedsRefundable, order, null, "The order has already been fully refunded.");
            if (amountMinor > order.RefundableAmountMinor)
                return new RefundOrderResult(RefundOrderOutcome.ExceedsRefundable, order, null,
                    $"The refund amount exceeds what can still be refunded ({MinorUnits.FromMinor(order.RefundableAmountMinor, payment.Currency)} {payment.Currency}).");

            if (budget.IsCancellationRequested)
                return new RefundOrderResult(RefundOrderOutcome.ProviderTimeout, order, null,
                    "Adyen did not respond in time while settling earlier refunds. No new refund was sent; retry shortly.");

            var refund = order.StartRefund(Guid.NewGuid().ToString("N"), amountMinor, requestedBy, _timeProvider.GetUtcNow());
            await _orderRepository.SaveChangesAsync();

            var result = await _gateway.RefundAsync(ToGatewayRequest(refund), budget.Token);
            RecordRefund(order, refund, result);
            await _orderRepository.SaveChangesAsync();

            return new RefundOrderResult(ToRefundOutcome(result), order, refund, RefundMessage(result));
        }
        finally
        {
            await _operationLock.ReleaseAsync(orderId);
        }
    }

    public async Task<IReadOnlyList<Order>> ListBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken) =>
        await _orderRepository.ListAsync(new CustomerOrdersWithPaymentsSpecification(buyerId), cancellationToken);

    public Task<Order?> GetOrderWithPaymentsAsync(int orderId, CancellationToken cancellationToken) =>
        _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentsSpec(orderId), cancellationToken);

    private void RecordPayment(Order order, PaymentAttempt attempt, PaymentAuthorisationResult result)
    {
        foreach (var exchange in result.Exchanges)
            order.AddProviderResponse(attempt, new ProviderResponseRecord(exchange.ReceivedAt, exchange.HttpStatusCode, exchange.Body));

        var status = result.Outcome switch
        {
            PaymentAuthorisationOutcome.Authorised => PaymentAttemptStatus.Authorised,
            PaymentAuthorisationOutcome.Refused => PaymentAttemptStatus.Refused,
            PaymentAuthorisationOutcome.ActionRequired => PaymentAttemptStatus.ActionRequired,
            PaymentAuthorisationOutcome.Pending => PaymentAttemptStatus.Pending,
            PaymentAuthorisationOutcome.Rejected or PaymentAuthorisationOutcome.ProviderError => PaymentAttemptStatus.Failed,
            _ => PaymentAttemptStatus.Unknown
        };

        if (status == PaymentAttemptStatus.Authorised && result.AuthorisedAmountMinor is { } authorised && authorised != attempt.AmountMinor)
        {
            _logger.LogError("Order {OrderId}: Adyen authorised {Authorised} but {Requested} was requested (psp {PspReference}). Manual review required.",
                order.Id, authorised, attempt.AmountMinor, result.PspReference ?? "-");
        }

        order.RecordPaymentOutcome(attempt, status, result.PspReference, result.ResultCode, result.RefusalReason,
            result.RefusalReasonCode, result.ErrorCode, result.ErrorMessage, result.AuthorisedAmountMinor, _timeProvider.GetUtcNow());

        _logger.LogInformation("Order {OrderId}: payment attempt {Reference} is {Status} (psp {PspReference}, resultCode {ResultCode}).",
            order.Id, attempt.Reference, status, result.PspReference ?? "-", result.ResultCode ?? "-");
    }

    private void RecordRefund(Order order, OrderRefund refund, RefundResult result)
    {
        foreach (var exchange in result.Exchanges)
            order.AddProviderResponse(refund, new ProviderResponseRecord(exchange.ReceivedAt, exchange.HttpStatusCode, exchange.Body));

        var status = result.Outcome switch
        {
            RefundOutcome.Received => RefundStatus.Received,
            RefundOutcome.Rejected or RefundOutcome.ProviderError => RefundStatus.Failed,
            _ => RefundStatus.Unknown
        };
        order.RecordRefundOutcome(refund, status, result.PspReference, result.ErrorCode, result.ErrorMessage, _timeProvider.GetUtcNow());

        _logger.LogInformation("Order {OrderId}: refund {Reference} of {Amount} is {Status} (psp {PspReference}).",
            order.Id, refund.Reference, refund.AmountMinor, status, result.PspReference ?? "-");
    }

    private static GatewayRefundRequest ToGatewayRequest(OrderRefund refund) =>
        new(refund.Reference, refund.IdempotencyKey, refund.PaymentPspReference, refund.AmountMinor, refund.Currency);

    private static PayOrderOutcome ToPayOutcome(PaymentAuthorisationResult result) => result.Outcome switch
    {
        PaymentAuthorisationOutcome.Authorised => PayOrderOutcome.Paid,
        PaymentAuthorisationOutcome.Refused => PayOrderOutcome.Refused,
        PaymentAuthorisationOutcome.ActionRequired => PayOrderOutcome.ActionRequired,
        PaymentAuthorisationOutcome.Pending => PayOrderOutcome.Pending,
        PaymentAuthorisationOutcome.Rejected => PayOrderOutcome.Rejected,
        PaymentAuthorisationOutcome.ProviderError => PayOrderOutcome.ProviderUnavailable,
        _ => result.TimedOut ? PayOrderOutcome.ProviderTimeout : PayOrderOutcome.ProviderUnknown
    };

    private static RefundOrderOutcome ToRefundOutcome(RefundResult result) => result.Outcome switch
    {
        RefundOutcome.Received => RefundOrderOutcome.Received,
        RefundOutcome.Rejected => RefundOrderOutcome.Rejected,
        RefundOutcome.ProviderError => RefundOrderOutcome.ProviderUnavailable,
        _ => result.TimedOut ? RefundOrderOutcome.ProviderTimeout : RefundOrderOutcome.ProviderUnknown
    };

    /// <summary>What the shopper is told, in terms they can act on.</summary>
    private static string ShopperMessage(PaymentAuthorisationResult result) => result.Outcome switch
    {
        PaymentAuthorisationOutcome.Authorised => "Payment successful.",
        PaymentAuthorisationOutcome.Refused when result.ResultCode is "Refused" =>
            $"Your card was declined{(string.IsNullOrWhiteSpace(result.RefusalReason) ? "" : $" ({result.RefusalReason})")}. " +
            "No money was taken. Check the card details you entered, or pay with a different card.",
        PaymentAuthorisationOutcome.Refused =>
            "The payment could not be completed. No money was taken. Try again, or pay with a different card.",
        PaymentAuthorisationOutcome.ActionRequired =>
            "Your card issuer requires an extra verification step (such as 3-D Secure) that this checkout cannot perform. " +
            "No money was taken. Please pay with a different card.",
        PaymentAuthorisationOutcome.Pending =>
            "Adyen has received the payment but has not confirmed it yet. Do not pay again; repeat this request later to check its status.",
        PaymentAuthorisationOutcome.Rejected =>
            $"The card details could not be processed{(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" ({result.ErrorMessage})")}. " +
            "No money was taken. Re-enter your card details and try again.",
        PaymentAuthorisationOutcome.ProviderError =>
            "Card payments are temporarily unavailable. No money was taken. Please try again later.",
        _ when result.TimedOut =>
            "Adyen did not respond in time, so the payment is not confirmed yet. Repeat this request to check it; you will not be charged twice.",
        _ => "Adyen could not confirm the payment. Repeat this request to check it; you will not be charged twice."
    };

    private static string RefundMessage(RefundResult result) => result.Outcome switch
    {
        RefundOutcome.Received => "Refund accepted by Adyen.",
        RefundOutcome.Rejected => $"Adyen rejected the refund{(string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $": {result.ErrorMessage}")}.",
        RefundOutcome.ProviderError => "Refunds are temporarily unavailable. No refund was made.",
        _ when result.TimedOut => "Adyen did not respond in time. The refund is recorded as unconfirmed and will be settled on the next refund request for this order.",
        _ => "Adyen could not confirm the refund. It is recorded as unconfirmed and will be settled on the next refund request for this order."
    };
}
