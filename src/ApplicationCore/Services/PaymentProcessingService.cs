using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Integrations.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Integrations.Reconciliation;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives the money through the PayPal lifecycle:
/// place (awaiting payment) -> pay (authorize/hold) -> fulfil (capture/take) -> refund (give back),
/// or cancel -> void (release the hold).
///
/// Idempotency: every call first consults the locally-stored payment state (so a double-click
/// returns the original authorization/capture/refund instead of moving money twice) and each
/// mutating PayPal call carries a PayPal-Request-Id derived from the order and its
/// authorization generation.
/// </summary>
public class PaymentProcessingService : IPaymentProcessingService
{
    // Serializes operations per aggregate so concurrent double-clicks cannot race past the
    // state check before the first request has persisted its PayPal ids.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();

    private const int ReportingMaxDaysPerRequest = 29; // Transaction Search supports at most 31-day windows.
    private const int ReportingPageSize = 100;
    private const int ReportingMaxPagesPerWindow = 500;

    private readonly IRepository<Order> _orderRepository;
    private readonly IReadRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Buyer> _buyerRepository;
    private readonly IPayPalProvider _payPal;
    private readonly PayPalSettings _settings;
    private readonly IAppLogger<PaymentProcessingService> _logger;
    private readonly IUriComposer _uriComposer;

    public PaymentProcessingService(
        IRepository<Order> orderRepository,
        IReadRepository<CatalogItem> catalogItemRepository,
        IRepository<Buyer> buyerRepository,
        IPayPalProvider payPal,
        PayPalSettings settings,
        IAppLogger<PaymentProcessingService> logger,
        IUriComposer uriComposer)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _buyerRepository = buyerRepository;
        _payPal = payPal;
        _settings = settings;
        _logger = logger;
        _uriComposer = uriComposer;
    }

    private string Currency => _settings.ResolvedCurrency;

    // ---------------------------------------------------------------- placing orders

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines, Address shipTo, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        if (lines is null || lines.Count == 0)
        {
            throw new DomainValidationException("At least one catalog item and quantity is required.");
        }
        Guard.Against.Null(shipTo, nameof(shipTo));

        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
            {
                throw new DomainValidationException($"Quantity for catalog item {line.CatalogItemId} must be positive.");
            }

            var catalogItem = catalogItems.FirstOrDefault(ci => ci.Id == line.CatalogItemId)
                ?? throw new ResourceNotFoundException($"Catalog item {line.CatalogItemId} was not found.");

            // Amounts come from catalog prices, snapshotted onto the order item.
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, _uriComposer.ComposePicUri(catalogItem.PictureUri)),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(buyerId, shipTo, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for buyer {BuyerId}, total {Total} {Currency}.", order.Id, buyerId, order.Total(), Currency);
        return order;
    }

    // ---------------------------------------------------------------- pay (authorize / hold)

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, CardDetails? card, string? paymentMethodId, CancellationToken cancellationToken = default)
    {
        if (card is null && string.IsNullOrEmpty(paymentMethodId))
        {
            throw new DomainValidationException("Provide either card details for a one-off payment or a saved paymentMethodId.");
        }
        if (card is not null && !string.IsNullOrEmpty(paymentMethodId))
        {
            throw new DomainValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        return await LockedOrderAsync(orderId, async () =>
        {
            var order = await LoadOrderForBuyerAsync(buyerId, orderId, cancellationToken);

            // Idempotent: a click that finds a live authorization returns it unchanged -
            // the shopper is never held twice.
            if (order.Status == OrderStatus.Authorized && order.Payment is { } existing && existing.IsAuthorizationUsable)
            {
                _logger.LogInformation("Order {OrderId} already authorized (authorization {AuthorizationId}); pay request treated as idempotent replay.", orderId, existing.AuthorizationId);
                return order;
            }

            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new InvalidOrderStateException("This order has already been paid for and fulfilled.");
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException("This order is cancelled and can no longer be paid.");
            }
            if (order.Status == OrderStatus.Authorized)
            {
                throw new InvalidOrderStateException("The previous authorization for this order has expired. Cancel the order and place a new one.");
            }

            string? vaultId = null;
            if (!string.IsNullOrEmpty(paymentMethodId))
            {
                vaultId = await ResolveSavedCardVaultIdAsync(buyerId, paymentMethodId, cancellationToken);
            }
            else if (card is not null)
            {
                card = SanitizeCard(card);
            }

            var total = Decimal.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
            var generation = order.Payment?.AuthorizationGeneration ?? 1;
            var requestId = RequestId($"pay-{orderId}-g{generation}");
            var invoiceId = OrderInvoice(orderId, generation);

            var auth = await _payPal.AuthorizeAsync(
                total, Currency, card, vaultId,
                invoiceId: invoiceId,
                customId: orderId.ToString(CultureInfo.InvariantCulture),
                requestId: requestId,
                storeCardInVault: false);

            var payment = new Payment(total, Currency, vaultId);
            payment.RecordAuthorization(auth.PayPalOrderId, auth.AuthorizationId, auth.Status, auth.ExpirationTime, invoiceId);

            order.Authorize(payment);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation("Order {OrderId} authorized for {Amount} {Currency} (PayPal authorization {AuthorizationId}, status {AuthorizationStatus}).",
                orderId, total, Currency, auth.AuthorizationId, auth.Status);
            return order;
        });
    }

    // ---------------------------------------------------------------- fulfil (capture / take)

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await LockedOrderAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Fulfilled)
            {
                // Idempotent replay: already captured once; never take the money twice.
                _logger.LogInformation("Order {OrderId} already fulfilled; fulfil request treated as idempotent replay.", orderId);
                return order;
            }
            if (order.Status == OrderStatus.Cancelled)
            {
                throw new InvalidOrderStateException("This order is cancelled; the held funds were released, nothing can be captured.");
            }
            if (order.Status != OrderStatus.Authorized || order.Payment is null)
            {
                throw new InvalidOrderStateException("The order must be paid (authorized) before it can be fulfilled.");
            }

            var payment = order.Payment;
            var authorizationId = await EnsureLiveAuthorizationAsync(order, payment, cancellationToken);

            var capture = await _payPal.CaptureAuthorizationAsync(
                authorizationId, payment.AuthorizedAmount, payment.Currency,
                RequestId($"capture-{orderId}-g{payment.AuthorizationGeneration}"));

            payment.RecordCapture(capture.CaptureId, capture.Status, capture.CapturedAmount, capture.FeeAmount, capture.NetAmount);
            order.MarkFulfilled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation("Order {OrderId} fulfilled: captured {CapturedAmount} {Currency} (fee {FeeAmount}, net {NetAmount}, capture {CaptureId}).",
                orderId, capture.CapturedAmount, capture.Currency, capture.FeeAmount, capture.NetAmount, capture.CaptureId);
            return order;
        });
    }

    /// <summary>
    /// Returns a capturable authorization id. If the stored hold has gone stale it is renewed
    /// (PayPal reauthorize, falling back to a fresh authorization on the shopper's saved card).
    /// Throws <see cref="InvalidOrderStateException"/> with an operator-actionable message when
    /// the authorization can no longer be renewed.
    /// </summary>
    private async Task<string> EnsureLiveAuthorizationAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        if (payment.IsAuthorizationUsable && payment.AuthorizationId is not null)
        {
            // Confirm against PayPal - locally we may still believe the hold is live.
            var live = await _payPal.GetAuthorizationStatusAsync(payment.AuthorizationId);
            if (live is not null && IsCapturableStatus(live.Status))
            {
                payment.RefreshAuthorization(payment.AuthorizationId, live.Status, live.ExpirationTime);
                return payment.AuthorizationId;
            }
        }

        _logger.LogWarning("Authorization for order {OrderId} is stale (status {Status}); attempting renewal.", order.Id, payment.AuthorizationStatus);

        var renewalErrors = new List<string>();

        // 1. PayPal's reauthorize endpoint can revive an authorization whose honor period lapsed.
        if (payment.AuthorizationId is not null)
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(
                    payment.AuthorizationId, payment.AuthorizedAmount, payment.Currency,
                    RequestId($"reauth-{order.Id}-g{payment.AuthorizationGeneration + 1}"));

                payment.IncrementAuthorizationGeneration();
                payment.RefreshAuthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime);
                _logger.LogInformation("Authorization for order {OrderId} renewed via reauthorize (generation {Generation}).", order.Id, payment.AuthorizationGeneration);
                return renewed.AuthorizationId;
            }
            catch (PaymentDeclinedException ex)
            {
                renewalErrors.Add($"reauthorize: {ex.Message}");
            }
        }

        // 2. A saved card lets us place a fresh hold server-side and capture it.
        if (payment.CardVaultId is not null)
        {
            try
            {
                var fresh = await _payPal.AuthorizeAsync(
                    payment.AuthorizedAmount, payment.Currency, card: null, vaultId: payment.CardVaultId,
                    invoiceId: OrderInvoice(order.Id, payment.AuthorizationGeneration + 1),
                    customId: order.Id.ToString(CultureInfo.InvariantCulture),
                    requestId: RequestId($"renew-{order.Id}-g{payment.AuthorizationGeneration + 1}"));

                payment.IncrementAuthorizationGeneration();
                payment.RecordAuthorization(fresh.PayPalOrderId, fresh.AuthorizationId, fresh.Status, fresh.ExpirationTime,
                    OrderInvoice(order.Id, payment.AuthorizationGeneration));
                _logger.LogInformation("Authorization for order {OrderId} renewed from the shopper's saved card (generation {Generation}).", order.Id, payment.AuthorizationGeneration);
                return fresh.AuthorizationId;
            }
            catch (PaymentDeclinedException ex)
            {
                renewalErrors.Add($"saved-card renewal: {ex.Message}");
            }
        }

        var reason = payment.CardVaultId is null
            ? "the order was paid with a one-off card whose details are deliberately not kept (PCI). Renewal is only possible for orders paid with a saved card."
            : "the saved card could no longer be charged (it may have been removed by the shopper).";

        throw new InvalidOrderStateException(
            $"The payment authorization for order {order.Id} has expired or was released ({payment.AuthorizationStatus}), and PayPal could not renew it. " +
            $"Reason it cannot be renewed: {reason} " +
            "Action for operators: cancel this order and ask the customer to place it again, paying with a saved card, then fulfil.");
    }

    private static bool IsCapturableStatus(string status) =>
        status is "CREATED" or "PENDING" or "PARTIALLY_CAPTURED" or "CAPTURED" or "COMPLETED";

    // ---------------------------------------------------------------- cancel (void / release)

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        return await LockedOrderAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("Order {OrderId} already cancelled; cancel request treated as idempotent replay.", orderId);
                return order;
            }
            if (order.Status == OrderStatus.Fulfilled)
            {
                throw new InvalidOrderStateException("This order was already fulfilled and captured; use the refunds endpoint to give money back.");
            }

            if (order.Status == OrderStatus.Authorized && order.Payment?.AuthorizationId is not null)
            {
                var releasedStatus = "VOIDED";
                var live = await _payPal.GetAuthorizationStatusAsync(order.Payment.AuthorizationId);
                if (live is not null && IsCapturableStatus(live.Status))
                {
                    var voided = await _payPal.VoidAuthorizationAsync(
                        order.Payment.AuthorizationId, RequestId($"void-{orderId}-g{order.Payment.AuthorizationGeneration}"));
                    releasedStatus = voided.Status;
                    _logger.LogInformation("Order {OrderId} cancelled: PayPal authorization {AuthorizationId} voided, funds released.", orderId, order.Payment.AuthorizationId);
                }
                else
                {
                    _logger.LogInformation("Order {OrderId} cancelled: authorization {AuthorizationId} was already stale/expired ({Status}); nothing to release.",
                        orderId, order.Payment.AuthorizationId, live?.Status ?? "NOT_FOUND");
                }

                order.Payment.MarkAuthorizationReleased(releasedStatus);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return order;
        });
    }

    // ---------------------------------------------------------------- refund (after fulfilment)

    public async Task<Refund> RefundOrderAsync(string buyerId, int orderId, decimal? amount, string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        return await LockedAsync<Refund>($"order-{orderId}", async () =>
        {
            var order = await LoadOrderForBuyerAsync(buyerId, orderId, cancellationToken);

            if (order.Payment is not { } payment)
            {
                throw new InvalidOrderStateException("This order has no payment to refund.");
            }

            // Caller-key idempotency: the same key never refunds twice.
            if (payment.FindRefundByKey(idempotencyKey) is { } replay)
            {
                _logger.LogInformation("Refund for order {OrderId} under idempotency key {Key} already exists ({RefundId}); returning it without refunding again.",
                    orderId, idempotencyKey, replay.RefundId);
                return replay;
            }

            if (payment.Status != PaymentStatus.Captured &&
                payment.Status != PaymentStatus.PartiallyRefunded &&
                payment.Status != PaymentStatus.Refunded)
            {
                throw new InvalidOrderStateException("Refunds are only possible after the payment has been captured (fulfilment).");
            }

            var refundable = Decimal.Round(payment.RefundableAmount, 2);
            if (refundable <= 0m)
            {
                throw new InvalidOrderStateException($"The captured amount ({payment.CapturedAmount:0.00}) has already been refunded in full.");
            }

            var refundAmount = amount ?? refundable;
            refundAmount = Decimal.Round(refundAmount, 2, MidpointRounding.AwayFromZero);
            if (refundAmount <= 0m)
            {
                throw new DomainValidationException("The refund amount must be greater than zero.");
            }
            if (refundAmount > refundable)
            {
                throw new InvalidOrderStateException(
                    $"Only {refundable:0.00} {payment.Currency} remains refundable on this capture (captured {payment.CapturedAmount:0.00}, already refunded {payment.TotalRefunded:0.00}); the request of {refundAmount:0.00} exceeds it.");
            }

            var result = await _payPal.RefundCaptureAsync(payment.CaptureId!, refundAmount, payment.Currency, RefundRequestId(payment.CaptureId!, idempotencyKey), noteToPayer);
            var completedTime = result.Status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) ? DateTimeOffset.UtcNow : (DateTimeOffset?)null;
            payment.AddRefund(result.RefundId, result.Amount, result.Status, completedTime, idempotencyKey);

            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} (refund {RefundId}, status {Status}).",
                orderId, result.Amount, payment.Currency, result.RefundId, result.Status);
            return payment.Refunds.Last(r => r.RefundId == result.RefundId);
        });
    }

    // ---------------------------------------------------------------- my orders

    public async Task<IReadOnlyList<Order>> GetBuyerOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        return await _orderRepository.ListAsync(new BuyerOrdersWithPaymentSpecification(buyerId), cancellationToken);
    }

    // ---------------------------------------------------------------- saved cards

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        card = SanitizeCard(card);

        return await LockedAsync($"card-{buyerId}", async () =>
        {
            var (buyer, isNew) = await LoadOrCreateBuyerAsync(buyerId, cancellationToken);

            var vault = await _payPal.VaultCardAsync(card, customerId: buyer.Id.ToString(CultureInfo.InvariantCulture), requestId: RequestId($"vault-{buyer.Id}-{Guid.NewGuid():N}"));
            var paymentMethod = buyer.AddPaymentMethod(alias, vault.VaultId, vault.Brand, vault.Last4, vault.Expiry);

            if (isNew)
            {
                await _buyerRepository.AddAsync(buyer, cancellationToken);
            }
            else
            {
                await _buyerRepository.UpdateAsync(buyer, cancellationToken);
            }

            _logger.LogInformation("Saved card ({Brand} ending {Last4}) for buyer {BuyerId}.", vault.Brand, vault.Last4, buyerId);
            return paymentMethod;
        });
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetBuyerCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        return buyer is null ? new List<PaymentMethod>() : buyer.PaymentMethods.ToList();
    }

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        await LockedAsync($"card-{buyerId}", async () =>
        {
            var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken)
                ?? throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found for this shopper.");

            var paymentMethod = buyer.FindPaymentMethod(paymentMethodId)
                ?? throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found for this shopper.");

            // Remove it at PayPal first: afterwards it can no longer be used to pay at all.
            if (!string.IsNullOrEmpty(paymentMethod.VaultId))
            {
                await _payPal.DeleteVaultCardAsync(paymentMethod.VaultId!);
            }

            buyer.RemovePaymentMethod(paymentMethodId);
            await _buyerRepository.UpdateAsync(buyer, cancellationToken);

            _logger.LogInformation("Payment method {PaymentMethodId} removed for buyer {BuyerId}.", paymentMethodId, buyerId);
        });
    }

    // ---------------------------------------------------------------- reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to <= from)
        {
            throw new DomainValidationException("'to' must be after 'from'.");
        }

        // 1. PayPal's own record for the entire range (the reporting API caps a single request
        //    at ~31 days, so walk the range in windows; within each window, walk every page).
        var payPalRows = new List<PayPalTransactionRecord>();
        var windowStart = from;
        while (windowStart < to)
        {
            var windowEnd = windowStart.AddDays(ReportingMaxDaysPerRequest) < to
                ? windowStart.AddDays(ReportingMaxDaysPerRequest)
                : to;

            for (var page = 1; page <= ReportingMaxPagesPerWindow; page++)
            {
                var result = await _payPal.ListTransactionsAsync(windowStart, windowEnd, page, ReportingPageSize);
                payPalRows.AddRange(result.Transactions);

                if (result.Transactions.Count == 0 || page >= result.TotalPages || result.TotalPages == 0)
                {
                    break;
                }
            }

            windowStart = windowEnd;
        }

        // 2. eShop orders (with payments) created in the range.
        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentInDateRangeSpecification(from, to), cancellationToken);

        // 3. Line them up both ways.
        var byId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (order.Payment is not { } payment)
            {
                continue;
            }
            if (payment.PayPalOrderId is not null) byId[payment.PayPalOrderId] = order.Id;
            if (payment.AuthorizationId is not null) byId[payment.AuthorizationId] = order.Id;
            if (payment.CaptureId is not null) byId[payment.CaptureId] = order.Id;
            foreach (var refund in payment.Refunds)
            {
                byId[refund.RefundId] = order.Id;
            }
            if (payment.InvoiceId is not null) byId[payment.InvoiceId] = order.Id;
        }

        var transactionRows = payPalRows
            .Where(t => !string.IsNullOrEmpty(t.TransactionId))
            .Select(t => new ReconciliationTransactionRow
            {
                TransactionId = t.TransactionId,
                PayPalReferenceId = t.PayPalReferenceId,
                PayPalReferenceIdType = t.PayPalReferenceIdType,
                TransactionEventCode = t.TransactionEventCode,
                TransactionStatus = t.TransactionStatus,
                Amount = t.Amount,
                FeeAmount = t.FeeAmount,
                Currency = t.Currency,
                InitiationDate = t.InitiationDate,
                PayerEmail = t.PayerEmail,
                InvoiceId = t.InvoiceId,
                EshopOrderId = MatchOrderId(t, byId)
            })
            .ToList();

        var matchedOrderIds = transactionRows.Where(r => r.EshopOrderId.HasValue).Select(r => r.EshopOrderId!.Value).ToHashSet();

        var eshopRows = orders.Where(o => o.Payment is not null).Select(o =>
        {
            var payment = o.Payment!;
            var found = matchedOrderIds.Contains(o.Id)
                        || (payment.PayPalOrderId is not null && payPalRows.Any(t => t.TransactionId == payment.PayPalOrderId || t.PayPalReferenceId == payment.PayPalOrderId))
                        || (payment.AuthorizationId is not null && payPalRows.Any(t => t.TransactionId == payment.AuthorizationId || t.PayPalReferenceId == payment.AuthorizationId))
                        || (payment.CaptureId is not null && payPalRows.Any(t => t.TransactionId == payment.CaptureId || t.PayPalReferenceId == payment.CaptureId))
                        || payment.Refunds.Any(r => payPalRows.Any(t => t.TransactionId == r.RefundId || t.PayPalReferenceId == r.RefundId));

            return new ReconciliationEshopRow
            {
                EshopOrderId = o.Id,
                BuyerId = o.BuyerId,
                OrderDate = o.OrderDate,
                OrderStatus = o.Status.ToString(),
                PaymentStatus = payment.Status.ToString(),
                OrderTotal = Decimal.Round(o.Total(), 2),
                Currency = payment.Currency,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                RefundIds = payment.Refunds.Select(r => r.RefundId).ToList(),
                FoundInPayPalReport = found
            };
        }).ToList();

        var inPayPalNotEshop = transactionRows.Where(r => r.UnmatchedInEshop).Select(r => r.TransactionId).Distinct().ToList();
        var inEshopNotPayPal = eshopRows.Where(r => !r.FoundInPayPalReport).Select(r => r.EshopOrderId).ToList();

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            Transactions = transactionRows,
            EshopPayments = eshopRows,
            Summary = new ReconciliationSummary
            {
                PayPalTransactionCount = transactionRows.Count,
                EshopPaymentCount = eshopRows.Count,
                MatchedCount = transactionRows.Count(r => !r.UnmatchedInEshop),
                InPayPalNotInEshop = inPayPalNotEshop,
                InEshopNotInPayPal = inEshopNotPayPal
            }
        };

        _logger.LogInformation("Reconciliation {From:o}..{To:o}: {PayPal} PayPal transactions, {Eshop} eShop payments, {PayPalOnly} unmatched in eShop, {EshopOnly} unmatched in PayPal.",
            from, to, report.Summary.PayPalTransactionCount, report.Summary.EshopPaymentCount, inPayPalNotEshop.Count, inEshopNotPayPal.Count);
        return report;
    }

    private static int? MatchOrderId(PayPalTransactionRecord transaction, Dictionary<string, int> byId)
    {
        // Exact id match only: transaction ids, PayPal reference ids (the PayPal order id appears
        // here for card payments, reference type ODR) and the unique invoice id stored per payment.
        if (transaction.TransactionId is not null && byId.TryGetValue(transaction.TransactionId, out var a)) return a;
        if (transaction.PayPalReferenceId is not null && byId.TryGetValue(transaction.PayPalReferenceId, out var b)) return b;
        if (transaction.InvoiceId is not null && byId.TryGetValue(transaction.InvoiceId, out var c)) return c;
        return null;
    }

    // ---------------------------------------------------------------- shared helpers

    /// <summary>
    /// Builds a PayPal-Request-Id. Kept under 127 chars and unique per attempt; duplicate
    /// submission is prevented by the per-order state machine + in-process lock.
    /// </summary>
    /// <summary>
    /// Builds a PayPal-Request-Id for one logical operation. Deterministic within a process run
    /// (so a retried/double-clicked request replays PayPal's cached result instead of moving
    /// money twice) yet unique per run (so a fresh run never collides with ids this shared
    /// sandbox account saw before).
    /// </summary>
    private static readonly string ProcessNonce = Guid.NewGuid().ToString("N")[..12];

    private static string RequestId(string suffix) =>
        RequestIdCore($"{suffix}-{ProcessNonce}");

    private static string RequestIdCore(string value) =>
        value.Length <= 127 ? value : value[..127];

    /// <summary>
    /// Globally unique invoice id for one authorization attempt. This (shared) PayPal account
    /// requires unique invoice ids per transaction, so it carries the process nonce and the
    /// authorization generation. It is stored on the payment for reconciliation matching.
    /// </summary>
    private static string OrderInvoice(int orderId, int generation) =>
        $"eshop-{ProcessNonce}-order-{orderId}-g{generation}";

    /// <summary>
    /// Deterministic PayPal-Request-Id for a refund derived from the capture id + the caller's
    /// idempotency key. Repeating the same refund (same capture+key) is idempotent at PayPal,
    /// while two different captures refunding under the same caller key never collide.
    /// </summary>
    private static string RefundRequestId(string captureId, string idempotencyKey)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{captureId}:{idempotencyKey}"));
        var hex = Convert.ToHexString(hash).ToLowerInvariant();
        return RequestIdCore($"refund-{hex[..40]}");
    }

    private async Task<(Buyer Buyer, bool IsNew)> LoadOrCreateBuyerAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        if (buyer is not null)
        {
            return (buyer, false);
        }

        return (new Buyer(buyerId), true);
    }

    private async Task<string> ResolveSavedCardVaultIdAsync(string buyerId, string paymentMethodIdParam, CancellationToken cancellationToken)
    {
        if (!int.TryParse(paymentMethodIdParam, out var paymentMethodId))
        {
            throw new DomainValidationException("paymentMethodId must be a number identifying one of your saved cards.");
        }

        var buyer = await _buyerRepository.FirstOrDefaultAsync(new BuyerWithPaymentMethodsSpecification(buyerId), cancellationToken);
        var paymentMethod = buyer?.FindPaymentMethod(paymentMethodId);
        if (paymentMethod is null || string.IsNullOrEmpty(paymentMethod.VaultId))
        {
            throw new ResourceNotFoundException($"Payment method {paymentMethodId} was not found among this shopper's saved cards.");
        }

        return paymentMethod.VaultId!;
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken)
            ?? throw new ResourceNotFoundException($"Order {orderId} was not found.");
        return order;
    }

    private async Task<Order> LoadOrderForBuyerAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        // One shopper must never see or act on another's orders.
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }
        return order;
    }

    private CardDetails SanitizeCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Any(c => !char.IsDigit(c)) || card.Number.Length is < 13 or > 19)
        {
            throw new DomainValidationException("The card number is not valid.");
        }

        // Accept MM/YY and YYYY-MM; PayPal's card APIs want YYYY-MM.
        var expiry = NormalizeExpiry(card.Expiry);

        if (card.Cvv is not null && (card.Cvv.Length is < 3 or > 4 || card.Cvv.Any(c => !char.IsDigit(c))))
        {
            throw new DomainValidationException("The card security code is not valid.");
        }

        return card with { Number = card.Number.Trim(), Expiry = expiry };
    }

    private static string NormalizeExpiry(string? expiry)
    {
        var trimmed = (expiry ?? string.Empty).Trim();

        if (DateTime.TryParseExact(trimmed, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ym))
        {
            return $"{ym.Year:0000}-{ym.Month:00}";
        }

        if (DateTime.TryParseExact(trimmed, "MM/yy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var my))
        {
            return $"{my.Year:0000}-{my.Month:00}";
        }

        throw new DomainValidationException("Card expiry must be in MM/YY or YYYY-MM format (for example 09/29 or 2029-09).");
    }

    private static async Task LockedAsync(string key, Func<Task> action) =>
        await LockedAsync<bool>(key, async () =>
        {
            await action();
            return true;
        });

    private static async Task<T> LockedAsync<T>(string key, Func<Task<T>> action)
    {
        var semaphore = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(60)))
        {
            throw new InvalidOrderStateException("Another payment operation on this resource is still running; try again shortly.");
        }

        try
        {
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<Order> LockedOrderAsync(int orderId, Func<Task<Order>> action) =>
        await LockedAsync($"order-{orderId}", action);
}
