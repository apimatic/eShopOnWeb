using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PaymentGateway;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Drives the money for an order: it holds the total when the shopper pays, takes it when an operator
/// fulfils, releases it when an order is cancelled before fulfilment, and gives it back on a return.
/// Saved cards exist only as processor tokens here, so card details never reach this application's
/// database.
/// </summary>
public class PaymentProcessingService : IPaymentProcessingService
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<PaymentProcessingService> _logger;
    private readonly TimeProvider _clock;

    /// <summary>
    /// Striped locks, so two requests that arrive together for the same order (a double-click on pay, two
    /// operators on fulfil) cannot both pass the state check and move the money twice. The stripes are
    /// fixed in number so nothing grows over time; two different orders sharing a stripe merely means one
    /// waits for the other, which is harmless for operations this short. A deployment with more than one
    /// process would pair this with the unique order/payment constraint and the processor's own request
    /// ids, which are also sent.
    /// </summary>
    private const int ORDER_LOCK_STRIPES = 64;
    private static readonly SemaphoreSlim[] OrderLocks = CreateOrderLocks();

    private static SemaphoreSlim[] CreateOrderLocks()
    {
        var locks = new SemaphoreSlim[ORDER_LOCK_STRIPES];
        for (var stripe = 0; stripe < locks.Length; stripe++)
        {
            locks[stripe] = new SemaphoreSlim(1, 1);
        }

        return locks;
    }

    public PaymentProcessingService(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IPaymentGateway gateway,
        IUriComposer uriComposer,
        IAppLogger<PaymentProcessingService> logger,
        TimeProvider clock)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _catalogItemRepository = catalogItemRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _logger = logger;
        _clock = clock;
    }

    public string Currency => _gateway.Currency;

    public async Task<Order> PlaceOrderAsync(string buyerId, IReadOnlyList<PlaceOrderLine> lines,
        Address shipToAddress, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(lines, nameof(lines));
        Guard.Against.NotAllowed(lines.Count == 0, "An order needs at least one catalog item.");
        Guard.Against.Null(shipToAddress, nameof(shipToAddress));

        var catalogItemIds = lines.Select(line => line.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds),
            cancellationToken);
        var itemsById = catalogItems.ToDictionary(item => item.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in lines)
        {
            Guard.Against.NotAllowed(line.Quantity <= 0,
                $"The quantity for catalog item {line.CatalogItemId} must be at least one.");

            if (!itemsById.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new ResourceNotFoundException($"Catalog item {line.CatalogItemId} is not in the catalog.");
            }

            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        // The existing order/order-item model is reused; the order simply starts life awaiting payment.
        var order = new Order(buyerId, shipToAddress, orderItems);
        var placed = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} placed for {BuyerId} totalling {Total}.", placed.Id, buyerId,
            placed.Total());
        return placed;
    }

    public Task<PaymentOperationResult> PayAsync(string buyerId, int orderId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken = default)
        => InOrderLockAsync(orderId,
            () => PayCoreAsync(buyerId, orderId, card, paymentMethodId, cancellationToken), cancellationToken);

    private async Task<PaymentOperationResult> PayCoreAsync(string buyerId, int orderId, CardDetails? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        Guard.Against.NotAllowed(card is not null && paymentMethodId.HasValue,
            "Pay with either a card or one of your saved cards, not both.");
        Guard.Against.NotAllowed(card is null && paymentMethodId is null,
            "A payment needs either card details or a saved card.");

        var now = _clock.GetUtcNow();
        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

        Guard.Against.NotAllowed(order.Status == OrderStatus.Cancelled,
            "This order was cancelled and can no longer be paid for.");
        Guard.Against.NotAllowed(order.Status == OrderStatus.Fulfilled,
            "This order has already been fulfilled.");

        var payment = await FindPaymentAsync(orderId, cancellationToken);
        if (payment is not null && payment.Status is not (PaymentStatus.AwaitingPayment or PaymentStatus.Declined))
        {
            // A double-click, or a shopper paying again after a slow response, must not hold the money
            // a second time: the payment that already exists is what is reported.
            _logger.LogInformation("Order {OrderId} already carries payment {PaymentId}; nothing was re-held.",
                orderId, payment.Id);
            return new PaymentOperationResult
            {
                Order = order,
                Payment = payment,
                AlreadyRecorded = true,
                Note = $"This order was already paid for on {payment.Updated:yyyy-MM-dd HH:mm} UTC."
            };
        }

        SavedCardReference? savedCard = null;
        if (paymentMethodId.HasValue)
        {
            savedCard = await GetOwnedSavedCardReferenceAsync(buyerId, paymentMethodId.Value, cancellationToken);
        }
        else
        {
            ValidateCard(card!);
        }

        var amount = Math.Round(order.Total(), 2, MidpointRounding.AwayFromZero);
        Guard.Against.NotAllowed(amount <= 0m, "An order that comes to zero has nothing to pay for.");

        if (payment is null)
        {
            payment = await _paymentRepository.AddAsync(
                new OrderPayment(order.Id, buyerId, _gateway.Currency, amount), cancellationToken);
        }
        else
        {
            payment.PriceAgain(amount, _gateway.Currency, now);
        }

        var attempt = payment.BeginAuthorization(now);
        var request = new AuthorizePaymentRequest
        {
            Amount = amount,
            Currency = payment.Currency,
            InvoiceId = PaymentReference.InvoiceId(payment.Id, payment.Reference, attempt),
            CustomId = PaymentReference.CustomId(payment.Id, payment.Reference),
            Description = $"eShop order {order.Id}",
            RequestId = PaymentReference.HoldRequestId(payment.Id, payment.Reference, attempt),
            Card = card,
            SavedCard = savedCard
        };

        PaymentAuthorization authorization;
        try
        {
            authorization = await _gateway.AuthorizeAsync(request, cancellationToken);
        }
        catch (CardDeclinedException)
        {
            payment.MarkDeclined(now);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }
        catch (PaymentProcessorException exception) when (exception.HasIssue("ORDER_ALREADY_AUTHORIZED"))
        {
            throw new PaymentProcessorException(
                $"The payment processor already holds money against order {order.Id} that this application cannot " +
                "identify, so nothing was taken. Check the processor's record for this order (GET /api/reconciliation) " +
                "before paying it again.", exception.ErrorName, exception.HttpStatus, exception.Issues, exception.DebugId);
        }

        payment.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId, authorization.Status,
            authorization.ExpirationTime, now);
        payment.SetCardSource(paymentMethodId, savedCard?.VaultId, savedCard?.PayPalCustomerId);
        order.MarkAuthorized(now);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} authorized for {Amount} {Currency} against hold {AuthorizationId}.",
            order.Id, amount, payment.Currency, authorization.AuthorizationId);

        return new PaymentOperationResult
        {
            Order = order,
            Payment = payment,
            Note = authorization.ExpirationTime is null
                ? "The order total is on hold and will be taken when the order is fulfilled."
                : $"The order total is on hold until {authorization.ExpirationTime:yyyy-MM-dd HH:mm} UTC and will be " +
                  "taken when the order is fulfilled."
        };
    }

    public Task<PaymentOperationResult> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
        => InOrderLockAsync(orderId, () => FulfilCoreAsync(orderId, cancellationToken), cancellationToken);

    private async Task<PaymentOperationResult> FulfilCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await FindPaymentAsync(orderId, cancellationToken)
            ?? throw new ActionNotAllowedException($"Order {orderId} has not been paid for, so it cannot be fulfilled.");

        if (order.Status == OrderStatus.Fulfilled)
        {
            return new PaymentOperationResult
            {
                Order = order,
                Payment = payment,
                AlreadyRecorded = true,
                Note = "This order has already been fulfilled and the money has been taken."
            };
        }

        Guard.Against.NotAllowed(order.Status == OrderStatus.Cancelled,
            "This order was cancelled and the held money was released, so it cannot be fulfilled.");
        Guard.Against.NotAllowed(payment.Status != PaymentStatus.Authorized,
            $"No money is on hold for this order (the payment reads as {payment.Status}), so there is nothing to take.");

        // A hold that has gone stale is renewed rather than failing the fulfilment outright.
        var renewed = false;
        var hold = await _gateway.GetAuthorizationAsync(payment.AuthorizationId ?? string.Empty, cancellationToken);
        if (hold is null || hold.IsStale)
        {
            renewed = await RenewHoldAsync(order, payment, cancellationToken);
        }

        CapturedPayment captured;
        try
        {
            captured = await CaptureHoldAsync(payment, cancellationToken);
        }
        catch (PaymentProcessorException exception) when (exception.HasIssue("AUTHORIZATION_EXPIRED")
            || exception.HasIssue("AUTHORIZATION_NOT_FOUND")
            || exception.HasIssue("AUTHORIZATION_VOIDED")
            || exception.HasIssue("MULTIPLE_AUTHORIZATIONS_NOT_SUPPORTED"))
        {
            // The hold lapsed between the check and the capture; renew it and take the money once.
            renewed |= await RenewHoldAsync(order, payment, cancellationToken);
            captured = await CaptureHoldAsync(payment, cancellationToken);
        }

        payment.MarkCaptured(captured.CaptureId, captured.Status, captured.GrossAmount, captured.FeeAmount,
            captured.NetAmount, PaymentReference.CaptureRequestId(payment.Id, payment.Reference, payment.RenewalCount), now);
        order.MarkFulfilled(now);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} fulfilled: captured {Gross} {Currency}, fee {Fee}, net {Net}.",
            order.Id, captured.GrossAmount, captured.Currency, captured.FeeAmount, captured.NetAmount);

        return new PaymentOperationResult
        {
            Order = order,
            Payment = payment,
            RenewedHold = renewed,
            Note = (renewed ? "The hold had gone stale and was renewed. " : string.Empty) +
                   $"Taken {captured.GrossAmount:0.00} {captured.Currency}; PayPal fee {captured.FeeAmount:0.00}; " +
                   $"net to the shop {captured.NetAmount:0.00}."
        };
    }

    public Task<PaymentOperationResult> CancelAsync(int orderId, CancellationToken cancellationToken = default)
        => InOrderLockAsync(orderId, () => CancelCoreAsync(orderId, cancellationToken), cancellationToken);

    private async Task<PaymentOperationResult> CancelCoreAsync(int orderId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await FindPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return new PaymentOperationResult
            {
                Order = order,
                Payment = payment,
                AlreadyRecorded = true,
                Note = "This order has already been cancelled."
            };
        }

        Guard.Against.NotAllowed(order.Status == OrderStatus.Fulfilled,
            "This order has already been fulfilled and the money has been taken; refund it instead of cancelling.");

        string note;
        if (payment is not null && payment.Status == PaymentStatus.Authorized
            && !string.IsNullOrEmpty(payment.AuthorizationId))
        {
            await _gateway.VoidAsync(payment.AuthorizationId!, PaymentReference.VoidRequestId(payment.Id, payment.Reference),
                cancellationToken);
            payment.MarkVoided(now);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            note = "The money that was on hold has been released back to the shopper; no money ever moved.";
        }
        else if (payment is not null)
        {
            payment.MarkCancelledWithoutHold(now);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            note = "The order was cancelled and no money had been taken.";
        }
        else
        {
            note = "The order was cancelled before it was ever paid for.";
        }

        order.MarkCancelled(now);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Order {OrderId} cancelled.", order.Id);
        return new PaymentOperationResult { Order = order, Payment = payment, Note = note };
    }

    public Task<RefundOperationResult> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken = default)
        => InOrderLockAsync(orderId,
            () => RefundCoreAsync(buyerId, orderId, amount, idempotencyKey, noteToPayer, cancellationToken), cancellationToken);

    private async Task<RefundOperationResult> RefundCoreAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, string? noteToPayer, CancellationToken cancellationToken)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        Guard.Against.NotAllowed(idempotencyKey.Length > 64
            || !idempotencyKey.All(IsSafeKeyCharacter),
            "The refund idempotency key must be 64 characters or fewer of letters, digits, dot, colon, dash or underscore.");

        var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await FindPaymentAsync(orderId, cancellationToken)
            ?? throw new ActionNotAllowedException($"Order {orderId} has no payment that could be refunded.");

        // Repeating a request under the same key replays the refund that was already made, while a
        // different key remains a legitimate second partial return.
        var alreadyRecorded = payment.FindRefund(idempotencyKey);
        if (alreadyRecorded is not null)
        {
            return new RefundOperationResult
            {
                Order = order,
                Payment = payment,
                Refund = alreadyRecorded,
                AlreadyRecorded = true
            };
        }

        Guard.Against.NotAllowed(string.IsNullOrEmpty(payment.CaptureId),
            "Nothing has been taken for this order yet, so there is nothing to refund. Cancel it instead.");

        var refundAmount = Math.Round(amount ?? payment.RefundableAmount, 2, MidpointRounding.AwayFromZero);
        var refund = payment.AddRefund(idempotencyKey, refundAmount, _clock.GetUtcNow());

        // Recorded before the processor is asked, so a request that dies half-way still shows that the
        // money is spoken for and cannot be offered back out twice.
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        try
        {
            var result = await _gateway.RefundAsync(payment.CaptureId!, refund.Amount,
                PaymentReference.RefundRequestId(payment.Id, payment.Reference, idempotencyKey), noteToPayer, cancellationToken);
            payment.CompleteRefund(refund, result.RefundId, result.FeeReturned, result.NetAmount, _clock.GetUtcNow());
        }
        catch (PaymentProcessorException exception) when (exception.HasIssue("REFUND_AMOUNT_EXCEEDED"))
        {
            payment.FailRefund(refund, _clock.GetUtcNow());
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new ActionNotAllowedException(
                $"Only {payment.RefundableAmount:0.00} {payment.Currency} of this order can still be refunded.");
        }
        catch (PaymentProcessorException)
        {
            payment.FailRefund(refund, _clock.GetUtcNow());
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw;
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation("Order {OrderId} refunded {Amount} {Currency} under key {IdempotencyKey} (refund {RefundId}).",
            order.Id, refund.Amount, refund.Currency, idempotencyKey, refund.PayPalRefundId);

        return new RefundOperationResult { Order = order, Payment = payment, Refund = refund };
    }

    public async Task<IReadOnlyList<OrderSummary>> GetOrdersForBuyerAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId),
            cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsForBuyerSpecification(buyerId),
            cancellationToken);

        var latestPaymentByOrder = new Dictionary<int, OrderPayment>();
        foreach (var payment in payments)
        {
            latestPaymentByOrder.TryAdd(payment.OrderId, payment);
        }

        return orders
            .OrderByDescending(order => order.Id)
            .Select(order => new OrderSummary
            {
                Order = order,
                Payment = latestPaymentByOrder.GetValueOrDefault(order.Id)
            })
            .ToList();
    }

    public async Task<PaymentMethod> SaveCardAsync(string buyerId, CardDetails card, string? alias,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(card, nameof(card));
        Guard.Against.NotAllowed(alias is { Length: > 64 }, "A card nickname must be 64 characters or fewer.");

        ValidateCard(card);

        var token = await _gateway.SaveCardAsync(card, PaymentReference.ShopperVaultKey(buyerId), cancellationToken);

        var paymentMethod = new PaymentMethod(buyerId, token.VaultId, token.PayPalCustomerId, alias, token.Last4,
            token.Brand, token.Expiry, token.CardHolderName, token.BillingCountry);
        var saved = await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);

        _logger.LogInformation("Shopper {BuyerId} saved {Description} as payment method {PaymentMethodId}.",
            buyerId, saved.Description, saved.Id);
        return saved;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetSavedCardsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        var cards = await _paymentMethodRepository.ListAsync(new SavedCardsForBuyerSpecification(buyerId),
            cancellationToken);
        return cards.ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var card = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || card.BuyerId != buyerId || string.IsNullOrEmpty(card.CardId))
        {
            throw new ResourceNotFoundException($"Saved card {paymentMethodId} is not available to this shopper.");
        }

        // Forgotten at the processor first, so the token cannot outlive the record that lets it be used.
        await _gateway.DeleteSavedCardAsync(card.CardId!, card.PayPalCustomerId ?? string.Empty, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(card, cancellationToken);

        _logger.LogInformation("Shopper {BuyerId} removed saved card {PaymentMethodId}.", buyerId, paymentMethodId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NotAllowed(to <= from, "'to' must be later than 'from'.");

        var transactions = await _gateway.ListTransactionsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsActiveInRangeSpecification(from, to),
            cancellationToken);

        // PayPal's record is matched to our payments both by the ids we carried on it and by the
        // processor ids each payment knows about.
        var byPayPalId = new Dictionary<string, OrderPayment>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            foreach (var id in payment.ProcessorIds())
            {
                byPayPalId.TryAdd(id, payment);
            }
        }

        var lines = new List<ReconciliationLine>();
        var linesByPayment = new Dictionary<int, List<ProcessorTransactionLine>>();

        foreach (var transaction in transactions)
        {
            var payment = Match(byPayPalId, payments, transaction);
            if (payment is null)
            {
                lines.Add(new ReconciliationLine { Transaction = transaction, KnownToEshop = false });
                continue;
            }

            if (!linesByPayment.TryGetValue(payment.Id, out var forThisPayment))
            {
                forThisPayment = new List<ProcessorTransactionLine>();
                linesByPayment[payment.Id] = forThisPayment;
            }

            forThisPayment.Add(transaction);
            lines.Add(new ReconciliationLine
            {
                Transaction = transaction,
                KnownToEshop = true,
                EshopPaymentId = payment.Id,
                EshopOrderId = payment.OrderId
            });
        }

        var paymentRows = new List<ReconciliationPayment>();
        foreach (var payment in payments.OrderBy(payment => payment.Id))
        {
            var seen = linesByPayment.TryGetValue(payment.Id, out var ourLines);
            var issues = new List<string>();

            if (!seen)
            {
                issues.Add("Nothing on PayPal's record for this range matches this payment. PayPal's transaction " +
                           "reporting lags live activity by up to a few hours, so this settles on its own unless " +
                           "it persists.");
            }
            else
            {
                if (payment.CaptureId is not null)
                {
                    var reported = ourLines.Where(line => line.TransactionId == payment.CaptureId)
                        .Sum(line => line.Amount);
                    if (reported > 0m && payment.CapturedAmount.HasValue && reported != payment.CapturedAmount.Value)
                    {
                        issues.Add($"PayPal's record differs for this payment: {reported:0.00} {payment.Currency} was " +
                                   $"reported as taken while this application recorded " +
                                   $"{payment.CapturedAmount:0.00} {payment.Currency}.");
                    }
                }

                var refundIds = payment.Refunds
                    .Where(refund => !string.IsNullOrEmpty(refund.PayPalRefundId))
                    .Select(refund => refund.PayPalRefundId!)
                    .ToHashSet(StringComparer.Ordinal);
                if (refundIds.Count > 0)
                {
                    var refunded = ourLines.Where(line => refundIds.Contains(line.TransactionId))
                        .Sum(line => Math.Abs(line.Amount));
                    if (refunded > 0m && refunded != payment.RefundedAmount)
                    {
                        issues.Add($"PayPal reported {refunded:0.00} {payment.Currency} returned against this payment " +
                                   $"but this application recorded {payment.RefundedAmount:0.00} {payment.Currency}.");
                    }
                }
            }

            if (payment.CapturedAmount.HasValue && payment.CapturedAmount.Value != payment.Amount)
            {
                issues.Add($"The amount taken ({payment.CapturedAmount:0.00} {payment.Currency}) differs from the " +
                           $"amount that was held ({payment.Amount:0.00} {payment.Currency}).");
            }

            paymentRows.Add(new ReconciliationPayment
            {
                PaymentId = payment.Id,
                OrderId = payment.OrderId,
                PaymentStatus = payment.Status.ToString(),
                AuthorizedAmount = payment.Amount,
                CapturedAmount = payment.CapturedAmount ?? 0m,
                FeeAmount = payment.FeeAmount ?? 0m,
                NetAmount = payment.NetAmount ?? 0m,
                RefundedAmount = payment.RefundedAmount,
                RefundableAmount = payment.RefundableAmount,
                PayPalOrderId = payment.PayPalOrderId,
                AuthorizationId = payment.AuthorizationId,
                CaptureId = payment.CaptureId,
                RefundIds = payment.Refunds
                    .Where(refund => !string.IsNullOrEmpty(refund.PayPalRefundId))
                    .Select(refund => refund.PayPalRefundId!)
                    .ToList(),
                SeenInPayPalRecord = seen,
                Issues = issues
            });
        }

        var onlyInPayPal = lines.Count(line => !line.KnownToEshop);
        return new ReconciliationReport
        {
            From = from,
            To = to,
            Generated = _clock.GetUtcNow(),
            Currency = _gateway.Currency,
            PayPalTransactions = lines,
            EshopPayments = paymentRows,
            Summary = new ReconciliationSummary
            {
                PayPalTransactionCount = lines.Count,
                EshopPaymentCount = paymentRows.Count,
                MatchedCount = lines.Count - onlyInPayPal,
                OnlyInPayPalCount = onlyInPayPal,
                OnlyInEshopCount = paymentRows.Count(row => !row.SeenInPayPalRecord),
                PayPalGrossAmount = transactions.Where(transaction => transaction.Amount > 0m)
                    .Sum(transaction => transaction.Amount),
                PayPalFeesAmount = transactions.Sum(transaction => transaction.FeeAmount ?? 0m),
                EshopCapturedAmount = paymentRows.Sum(row => row.CapturedAmount),
                EshopRefundedAmount = paymentRows.Sum(row => row.RefundedAmount)
            }
        };
    }

    private static OrderPayment? Match(IReadOnlyDictionary<string, OrderPayment> byPayPalId,
        IReadOnlyList<OrderPayment> payments, ProcessorTransactionLine transaction)
    {
        if (byPayPalId.TryGetValue(transaction.TransactionId, out var byTransactionId))
        {
            return byTransactionId;
        }

        if (!string.IsNullOrEmpty(transaction.ReferenceId)
            && byPayPalId.TryGetValue(transaction.ReferenceId, out var byReferenceId))
        {
            return byReferenceId;
        }

        foreach (var payment in payments)
        {
            if (payment.Recognizes(transaction.InvoiceId) || payment.Recognizes(transaction.CustomField))
            {
                return payment;
            }
        }

        return null;
    }

    private async Task<CapturedPayment> CaptureHoldAsync(OrderPayment payment, CancellationToken cancellationToken)
        => await _gateway.CaptureAsync(payment.AuthorizationId!, payment.Amount,
            PaymentReference.CaptureRequestId(payment.Id, payment.Reference, payment.RenewalCount), cancellationToken);

    /// <summary>
    /// Renews a hold that has gone stale. PayPal's own reauthorize is tried first; if that is refused,
    /// a fresh hold is taken on the card the shopper saved. When neither can work the caller is told in
    /// terms an operator can act on rather than left with a bare failure.
    /// </summary>
    private async Task<bool> RenewHoldAsync(Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var attempts = new List<string>();

        if (!string.IsNullOrEmpty(payment.AuthorizationId))
        {
            var previousAuthorizationId = payment.AuthorizationId;
            try
            {
                var renewed = await _gateway.ReauthorizeAsync(previousAuthorizationId, payment.Amount,
                    PaymentReference.RenewalRequestId(payment.Id, payment.Reference, payment.RenewalCount + 1), cancellationToken);

                payment.MarkRenewed(previousAuthorizationId, payment.PayPalOrderId ?? string.Empty,
                    renewed.AuthorizationId, renewed.Status, renewed.ExpirationTime, _clock.GetUtcNow());
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                _logger.LogWarning("Hold {PreviousAuthorizationId} on order {OrderId} was renewed as {AuthorizationId}.",
                    previousAuthorizationId, order.Id, renewed.AuthorizationId);
                return true;
            }
            catch (PaymentProcessorException exception)
            {
                attempts.Add($"reauthorizing the hold: {Describe(exception)}");
            }
            catch (CardDeclinedException exception)
            {
                attempts.Add($"reauthorizing the hold: {exception.Message}");
            }
        }

        if (!string.IsNullOrEmpty(payment.CardVaultId))
        {
            var previousAuthorizationId = payment.AuthorizationId ?? string.Empty;
            try
            {
                var attempt = payment.BeginAuthorization(_clock.GetUtcNow());
                var authorization = await _gateway.AuthorizeAsync(new AuthorizePaymentRequest
                {
                    Amount = payment.Amount,
                    Currency = payment.Currency,
                    InvoiceId = PaymentReference.InvoiceId(payment.Id, payment.Reference, attempt),
                    CustomId = PaymentReference.CustomId(payment.Id, payment.Reference),
                    Description = $"eShop order {order.Id} (renewed hold)",
                    RequestId = PaymentReference.HoldRequestId(payment.Id, payment.Reference, attempt),
                    SavedCard = new SavedCardReference
                    {
                        VaultId = payment.CardVaultId!,
                        PayPalCustomerId = payment.PayPalCustomerId ?? string.Empty
                    }
                }, cancellationToken);

                payment.MarkRenewed(previousAuthorizationId, authorization.PayPalOrderId, authorization.AuthorizationId,
                    authorization.Status, authorization.ExpirationTime, _clock.GetUtcNow());
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                _logger.LogWarning("Order {OrderId} was held again on the shopper's saved card as {AuthorizationId}.",
                    order.Id, authorization.AuthorizationId);
                return true;
            }
            catch (PaymentProcessorException exception)
            {
                attempts.Add($"re-holding on the saved card: {Describe(exception)}");
            }
            catch (CardDeclinedException exception)
            {
                attempts.Add($"re-holding on the saved card: {exception.Message}");
            }
        }

        throw new PaymentRenewalFailedException(
            $"The money held for order {order.Id} has lapsed and cannot be renewed, so nothing has been taken and " +
            "the order must not be shipped. Ask the shopper to pay for the order again " +
            $"(POST /api/orders/{order.Id}/pay), then fulfil it again." +
            (attempts.Count == 0 ? string.Empty : $" Attempts: {string.Join("; ", attempts)}"));
    }

    private async Task<SavedCardReference> GetOwnedSavedCardReferenceAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var card = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card is null || card.BuyerId != buyerId || string.IsNullOrEmpty(card.CardId))
        {
            throw new ResourceNotFoundException($"Saved card {paymentMethodId} is not available to this shopper.");
        }

        return new SavedCardReference
        {
            VaultId = card.CardId,
            PayPalCustomerId = card.PayPalCustomerId ?? string.Empty
        };
    }

    /// <summary>
    /// Serialises money movement for one order, so two requests that arrive at the same moment cannot
    /// both get past the state check and move the money twice.
    /// </summary>
    private static async Task<T> InOrderLockAsync<T>(int orderId, Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks[Math.Abs(orderId % ORDER_LOCK_STRIPES)];
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new ResourceNotFoundException($"Order {orderId} does not exist.");
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order.BuyerId != buyerId)
        {
            // Reported as missing rather than forbidden, so one shopper can never tell whether another
            // shopper's order exists.
            throw new ResourceNotFoundException($"Order {orderId} does not exist.");
        }

        return order;
    }

    private async Task<OrderPayment?> FindPaymentAsync(int orderId, CancellationToken cancellationToken)
        => await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);

    private static string Describe(PaymentProcessorException exception)
        => string.Join(",", exception.Issues.Append(exception.ErrorName ?? "PROCESSOR_ERROR").Distinct());

    private static bool IsSafeKeyCharacter(char character)
        => char.IsLetterOrDigit(character) || character is '.' or ':' or '_' or '-';

    /// <summary>
    /// Checks a card the caller typed in. These are request-shape problems, so they come back as
    /// invalid request rather than as something the server could not do.
    /// </summary>
    private void ValidateCard(CardDetails card)
    {
        Guard.Against.Null(card, nameof(card));

        var number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        InvalidIf(number.Length is < 12 or > 19, "The card number is not valid.");
        InvalidIf(string.IsNullOrWhiteSpace(card.CardHolderName), "The name on the card is required.");

        var expiry = (card.Expiry ?? string.Empty).Trim();
        var year = 0;
        var month = 0;
        var parsed = expiry.Length == 7 && expiry[4] == '-'
            && int.TryParse(expiry.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out year)
            && int.TryParse(expiry.AsSpan(5, 2), NumberStyles.None, CultureInfo.InvariantCulture, out month)
            && month is >= 1 and <= 12;

        InvalidIf(!parsed, "The card expiry must be a future month in YYYY-MM form.");
        if (parsed)
        {
            var today = _clock.GetUtcNow().DateTime;
            InvalidIf(year < today.Year || (year == today.Year && month < today.Month),
                "The card has already expired; use a card with a future expiry date.");
        }

        var securityCode = (card.SecurityCode ?? string.Empty).Trim();
        InvalidIf(securityCode.Length is < 3 or > 4 || !securityCode.All(char.IsDigit),
            "The card security code is not valid.");
    }

    private static void InvalidIf(bool condition, string message)
    {
        if (condition)
        {
            throw new ArgumentException(message);
        }
    }
}
