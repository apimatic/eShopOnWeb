using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Finix;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<Payment> _payments;
    private readonly IRepository<Refund> _refunds;
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IRepository<PaymentCustomer> _customers;
    private readonly IFinixGateway _finix;
    private readonly IAppLogger<PaymentService> _logger;
    private readonly OrderOperationLock _orderOperationLock;

    public PaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<Payment> payments,
        IRepository<Refund> refunds,
        IRepository<SavedCard> savedCards,
        IRepository<PaymentCustomer> customers,
        IFinixGateway finix,
        IAppLogger<PaymentService> logger,
        OrderOperationLock orderOperationLock)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _payments = payments;
        _refunds = refunds;
        _savedCards = savedCards;
        _customers = customers;
        _finix = finix;
        _logger = logger;
        _orderOperationLock = orderOperationLock;
    }

    public async Task<(Order Order, PaymentView Payment)> CreateOrderAsync(
        string ownerId,
        IReadOnlyList<NewOrderItem> requestedItems,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (requestedItems is null || requestedItems.Count == 0)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "At least one order item is required.");
        }

        if (requestedItems.Any(i => i.Quantity <= 0))
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "Every order item quantity must be greater than zero.");
        }

        var catalogItemIds = requestedItems.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(
            new CatalogItemsSpecification(catalogItemIds),
            cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "One or more catalog items do not exist.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requestedItem in requestedItems)
        {
            var catalogItem = catalogItems.First(i => i.Id == requestedItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                catalogItem.PictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requestedItem.Quantity));
        }

        var order = new Order(ownerId, shippingAddress, orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The order total must be greater than zero.");
        }

        var payment = new Payment(order.Id, ownerId, amountCents, _finix.Currency);
        payment = await _payments.AddAsync(payment, cancellationToken);

        _logger.LogInformation("Created order {OrderId} for shopper with payment awaiting authorization.", order.Id);
        return (order, BuildView(order, payment, Array.Empty<Refund>()));
    }

    public async Task<PaymentView> PayOrderAsync(
        string ownerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        using var orderLock = await _orderOperationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOwnedOrderAsync(ownerId, orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Canceled || payment.FulfilmentStatus == FulfilmentStatus.Canceled)
        {
            throw new PaymentOperationException(PaymentOperationError.Conflict, "The order has been canceled and can no longer be paid.");
        }

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return await BuildViewAsync(order, payment, cancellationToken);
        }

        var hasCard = command.Card is not null;
        var hasSavedCard = command.PaymentMethodId.HasValue;
        if (hasCard == hasSavedCard)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Validation,
                "Supply exactly one payment source: either card details or paymentMethodId.");
        }

        var customer = await GetOrCreateCustomerIdentityAsync(ownerId, cancellationToken);
        payment.SetCustomerIdentity(customer.FinixIdentityId);

        string paymentInstrumentId;
        if (hasSavedCard)
        {
            var savedCard = await _savedCards.FirstOrDefaultAsync(
                new SavedCardByIdAndOwnerSpecification(command.PaymentMethodId!.Value, ownerId),
                cancellationToken);

            if (savedCard is null || !savedCard.Enabled)
            {
                throw new PaymentOperationException(PaymentOperationError.NotFound, "The saved payment method was not found.");
            }

            paymentInstrumentId = savedCard.FinixPaymentInstrumentId;
        }
        else
        {
            ValidateCard(command.Card!);
            var instrument = await _finix.CreatePaymentInstrumentAsync(
                customer.FinixIdentityId,
                NormalizeCard(command.Card!),
                cancellationToken);
            paymentInstrumentId = instrument.Id;
        }

        payment.SetPaymentInstrument(paymentInstrumentId);
        var attempt = payment.BeginAuthorizationAttempt();
        var idempotencyId = $"eshop-{payment.PublicReference}-authorize-{attempt}";

        FinixAuthorization authorization;
        try
        {
            authorization = await _finix.CreateAuthorizationAsync(
                payment.AmountCents,
                paymentInstrumentId,
                idempotencyId,
                cancellationToken);
        }
        catch (FinixApiException ex) when (IsDuplicateIdempotencyError(ex))
        {
            authorization = await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken)
                ?? throw new PaymentOperationException(
                    PaymentOperationError.Upstream,
                    "Finix reported a duplicate authorization request but the authorization could not be retrieved.");
        }
        catch (FinixApiException ex)
        {
            payment.MarkAuthorizationFailed(BuildFinixFailureMessage("authorize", ex));
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(
                PaymentOperationError.Upstream,
                BuildFinixFailureMessage("authorize", ex));
        }

        payment.MarkAuthorized(
            authorization.Id,
            authorization.State ?? "UNKNOWN",
            authorization.CreatedAt ?? DateTimeOffset.UtcNow);

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            var message = authorization.FailureMessage
                ?? $"Finix authorization {authorization.Id} ended in state {authorization.State ?? "UNKNOWN"}.";
            payment.MarkAuthorizationFailed(message, authorization.Id, authorization.State);
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentOperationError.Upstream, message);
        }

        await _payments.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Authorized order {OrderId} with Finix authorization {AuthorizationId}.", orderId, authorization.Id);
        return await BuildViewAsync(order, payment, cancellationToken);
    }

    public async Task<PaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var orderLock = await _orderOperationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.FulfilmentStatus == FulfilmentStatus.Fulfilled
            && payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return await BuildViewAsync(order, payment, cancellationToken);
        }

        if (payment.Status == PaymentStatus.Canceled || payment.FulfilmentStatus == FulfilmentStatus.Canceled)
        {
            throw new PaymentOperationException(PaymentOperationError.Conflict, "Canceled orders cannot be fulfilled.");
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentOperationException(
                PaymentOperationError.Conflict,
                "The order has no active card authorization. The shopper must pay successfully before the order can be fulfilled.");
        }

        FinixAuthorization captureResult;
        try
        {
            captureResult = await _finix.CaptureAuthorizationAsync(
                payment.AuthorizationId,
                payment.AmountCents,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            var recovered = await TryRecoverCaptureAsync(payment.AuthorizationId, cancellationToken);
            if (recovered is null)
            {
                var message = BuildCaptureFailureMessage(payment.AuthorizationId, ex);
                payment.MarkCaptureFailed(message);
                await _payments.UpdateAsync(payment, cancellationToken);
                throw new PaymentOperationException(PaymentOperationError.Conflict, message);
            }

            captureResult = recovered;
        }

        var transferId = captureResult.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            var refreshed = await TryRecoverCaptureAsync(payment.AuthorizationId, cancellationToken);
            transferId = refreshed?.Transfer;
        }

        if (string.IsNullOrWhiteSpace(transferId))
        {
            var message = $"Finix accepted the capture for authorization {payment.AuthorizationId} but did not return a capture transfer. " +
                          "Check the Finix dashboard before fulfilling again.";
            payment.MarkCaptureFailed(message);
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentOperationException(PaymentOperationError.Conflict, message);
        }

        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        var feeCents = await ResolveFeeCentsAsync(transfer, cancellationToken);

        // Fees and the final transfer state are produced asynchronously by Finix
        // after the capture is accepted. Re-read the transfer so the payment shows
        // the final state that Finix reports.
        var finalTransfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        if (finalTransfer.Amount > 0)
        {
            transfer = finalTransfer;
        }

        var capturedAmountCents = transfer.Amount > 0 ? transfer.Amount : payment.AmountCents;
        var netCents = capturedAmountCents - feeCents;

        payment.MarkCaptured(
            transfer.Id,
            transfer.State ?? "UNKNOWN",
            capturedAmountCents,
            feeCents,
            netCents,
            DateTimeOffset.UtcNow);

        await _payments.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation(
            "Captured order {OrderId} with Finix transfer {TransferId}; captured {CapturedCents} cents, fee {FeeCents} cents.",
            orderId,
            transfer.Id,
            capturedAmountCents,
            feeCents);

        return await BuildViewAsync(order, payment, cancellationToken);
    }

    public async Task<PaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var orderLock = await _orderOperationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderAsync(orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment.Status == PaymentStatus.Canceled)
        {
            return await BuildViewAsync(order, payment, cancellationToken);
        }

        if (payment.FulfilmentStatus == FulfilmentStatus.Fulfilled
            || payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Conflict,
                "The order has already been fulfilled. Cancel is not available; issue a refund instead.");
        }

        string? authorizationState = payment.AuthorizationState;
        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            try
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                authorizationState = voided.VoidState ?? voided.State ?? authorizationState;
            }
            catch (FinixApiException ex)
            {
                var message = $"Finix could not void authorization {payment.AuthorizationId}: {ex.Message}. " +
                              "The hold may already have expired or been released; retry the cancel or check Finix before proceeding.";
                payment.MarkCaptureFailed(message);
                await _payments.UpdateAsync(payment, cancellationToken);
                throw new PaymentOperationException(PaymentOperationError.Conflict, message);
            }
        }

        payment.MarkCanceled(DateTimeOffset.UtcNow, authorizationState);
        await _payments.UpdateAsync(payment, cancellationToken);
        _logger.LogInformation("Canceled order {OrderId} and released any card hold.", orderId);
        return await BuildViewAsync(order, payment, cancellationToken);
    }

    public async Task<(PaymentView Payment, RefundView Refund)> RefundOrderAsync(
        string ownerId,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "An idempotencyKey is required for refunds.");
        }

        using var orderLock = await _orderOperationLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOwnedOrderAsync(ownerId, orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        var existing = await _refunds.FirstOrDefaultAsync(
            new RefundByOrderAndIdempotencyKeySpecification(orderId, idempotencyKey),
            cancellationToken);
        if (existing is not null)
        {
            return (await BuildViewAsync(order, payment, cancellationToken), ToRefundView(existing));
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded)
            || string.IsNullOrWhiteSpace(payment.CaptureTransferId)
            || !payment.CapturedAmountCents.HasValue)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Conflict,
                "Only a fulfilled, captured payment can be refunded.");
        }

        var existingRefunds = await _refunds.ListAsync(
            new RefundsByPaymentIdSpecification(payment.Id),
            cancellationToken);
        await RefreshPendingRefundStatesAsync(existingRefunds, cancellationToken);
        var alreadyRefundedCents = EffectiveRefundedCents(existingRefunds);
        var remainingCents = payment.CapturedAmountCents.Value - alreadyRefundedCents;

        if (remainingCents <= 0)
        {
            throw new PaymentOperationException(PaymentOperationError.Conflict, "The captured payment has already been fully refunded.");
        }

        var requestedCents = amount.HasValue ? ToCents(amount.Value) : remainingCents;
        if (requestedCents <= 0)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The refund amount must be greater than zero.");
        }

        if (requestedCents > remainingCents)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Conflict,
                $"The refund amount exceeds the remaining refundable amount of {remainingCents / 100m:0.00} {payment.Currency}.");
        }

        var finixIdempotencyId = BuildRefundIdempotencyId(payment.PublicReference, idempotencyKey);
        FinixTransfer refundTransfer;
        try
        {
            refundTransfer = await _finix.CreateRefundAsync(
                payment.CaptureTransferId,
                requestedCents,
                finixIdempotencyId,
                cancellationToken);
        }
        catch (FinixApiException ex) when (IsDuplicateIdempotencyError(ex))
        {
            refundTransfer = await _finix.FindTransferByIdempotencyIdAsync(finixIdempotencyId, cancellationToken)
                ?? throw new PaymentOperationException(
                    PaymentOperationError.Upstream,
                    "Finix reported a duplicate refund request but the refund transfer could not be retrieved.");
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Upstream,
                BuildFinixFailureMessage("refund", ex));
        }

        var refund = new Refund(
            payment.Id,
            orderId,
            ownerId,
            idempotencyKey,
            requestedCents,
            refundTransfer.Id,
            refundTransfer.State ?? "UNKNOWN",
            refundTransfer.CreatedAt ?? DateTimeOffset.UtcNow);

        refund = await _refunds.AddAsync(refund, cancellationToken);
        payment.RecordRefund(alreadyRefundedCents + requestedCents);
        await _payments.UpdateAsync(payment, cancellationToken);

        _logger.LogInformation(
            "Refunded order {OrderId} with Finix transfer {TransferId} for {RefundCents} cents.",
            orderId,
            refundTransfer.Id,
            requestedCents);

        return (await BuildViewAsync(order, payment, cancellationToken), ToRefundView(refund));
    }

    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(ownerId), cancellationToken);
        var payments = await _payments.ListAsync(new PaymentsByOwnerSpecification(ownerId), cancellationToken);
        var refunds = await _refunds.ListAsync(new RefundsByOwnerSpecification(ownerId), cancellationToken);
        await RefreshPendingRefundStatesAsync(refunds, cancellationToken);

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var refundsByOrder = refunds.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Refund>)g.ToList());

        var views = new List<PaymentView>();
        foreach (var order in orders)
        {
            if (!paymentsByOrder.TryGetValue(order.Id, out var payment))
            {
                continue;
            }

            refundsByOrder.TryGetValue(order.Id, out var orderRefunds);
            views.Add(BuildView(order, payment, orderRefunds ?? Array.Empty<Refund>()));
        }

        return views.OrderByDescending(v => v.OrderDate).ToList();
    }

    public async Task<SavedCardView> SaveCardAsync(
        string ownerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        ValidateCard(card);
        var customer = await GetOrCreateCustomerIdentityAsync(ownerId, cancellationToken);
        var instrument = await _finix.CreatePaymentInstrumentAsync(
            customer.FinixIdentityId,
            NormalizeCard(card),
            cancellationToken);

        var savedCard = new SavedCard(
            ownerId,
            instrument.Id,
            instrument.Brand ?? "UNKNOWN",
            instrument.LastFour ?? LastFour(card.Number),
            instrument.ExpirationMonth ?? card.ExpirationMonth,
            instrument.ExpirationYear ?? card.ExpirationYear,
            instrument.CardType,
            DateTimeOffset.UtcNow);

        savedCard = await _savedCards.AddAsync(savedCard, cancellationToken);
        _logger.LogInformation("Saved Finix payment instrument {PaymentInstrumentId} for shopper.", instrument.Id);
        return ToSavedCardView(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetSavedCardsAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        var cards = await _savedCards.ListAsync(new SavedCardsByOwnerSpecification(ownerId), cancellationToken);
        return cards
            .Where(c => c.Enabled)
            .OrderByDescending(c => c.CreatedAt)
            .Select(ToSavedCardView)
            .ToList();
    }

    public async Task DeleteSavedCardAsync(string ownerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCards.FirstOrDefaultAsync(
            new SavedCardByIdAndOwnerSpecification(paymentMethodId, ownerId),
            cancellationToken);

        if (savedCard is null || !savedCard.Enabled)
        {
            throw new PaymentOperationException(PaymentOperationError.NotFound, "The saved payment method was not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404 || ex.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Finix payment instrument {PaymentInstrumentId} was already unavailable while deleting saved card.", savedCard.FinixPaymentInstrumentId);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(
                PaymentOperationError.Upstream,
                $"Finix could not disable payment instrument {savedCard.FinixPaymentInstrumentId}: {ex.Message}");
        }

        savedCard.Disable();
        await _savedCards.UpdateAsync(savedCard, cancellationToken);
        _logger.LogInformation("Deleted saved Finix payment instrument {PaymentInstrumentId} for shopper.", savedCard.FinixPaymentInstrumentId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The from date-time must be before the to date-time.");
        }

        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        var authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);

        var orders = await _orders.ListAsync(new OrdersInDateRangeSpecification(from, to), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = orderIds.Count == 0
            ? Array.Empty<Payment>()
            : (await _payments.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), cancellationToken)).ToArray();
        var refunds = orderIds.Count == 0
            ? Array.Empty<Refund>()
            : (await _refunds.ListAsync(new RefundsByOrderIdsSpecification(orderIds), cancellationToken)).ToArray();

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var refundsByOrder = refunds.GroupBy(r => r.OrderId).ToDictionary(g => g.Key, g => (IReadOnlyList<Refund>)g.ToList());

        var orderByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p.OrderId);

        var orderByTransfer = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var payment in payments.Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId)))
        {
            orderByTransfer[payment.CaptureTransferId!] = payment.OrderId;
        }
        foreach (var refund in refunds)
        {
            orderByTransfer[refund.FinixTransferId] = refund.OrderId;
        }

        var finixTransactions = new List<ReconciliationFinixTransaction>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transfer in transfers)
        {
            int? matchedOrderId = null;
            if (orderByTransfer.TryGetValue(transfer.Id, out var orderId))
            {
                matchedOrderId = orderId;
            }
            else if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer)
                     && orderByTransfer.TryGetValue(transfer.ParentTransfer, out var parentOrderId))
            {
                matchedOrderId = parentOrderId;
            }

            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
            }

            finixTransactions.Add(new ReconciliationFinixTransaction(
                "TRANSFER",
                transfer.Id,
                transfer.Type,
                transfer.State,
                transfer.Amount / 100m,
                transfer.Currency,
                transfer.ParentTransfer,
                transfer.CreatedAt,
                matchedOrderId));
        }

        foreach (var authorization in authorizations)
        {
            int? matchedOrderId = null;
            if (orderByAuthorization.TryGetValue(authorization.Id, out var orderId))
            {
                matchedOrderId = orderId;
            }

            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
            }

            finixTransactions.Add(new ReconciliationFinixTransaction(
                "AUTHORIZATION",
                authorization.Id,
                "AUTHORIZATION",
                authorization.State,
                authorization.Amount / 100m,
                authorization.Currency,
                null,
                authorization.CreatedAt,
                matchedOrderId));
        }

        var eshopOrders = new List<ReconciliationOrder>();
        foreach (var order in orders)
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            refundsByOrder.TryGetValue(order.Id, out var orderRefunds);

            eshopOrders.Add(new ReconciliationOrder(
                order.Id,
                order.OrderDate,
                order.Total(),
                payment?.Currency ?? _finix.Currency,
                payment?.Status.ToString() ?? "AwaitingPayment",
                payment?.FulfilmentStatus.ToString() ?? "Pending",
                payment?.AuthorizationId,
                payment?.CaptureTransferId,
                orderRefunds?.Select(r => r.FinixTransferId).ToList() ?? new List<string>()));
        }

        var unmatchedFinix = finixTransactions
            .Where(t => !t.MatchedOrderId.HasValue)
            .Select(t => t.Id)
            .Distinct()
            .ToList();

        var unmatchedOrders = eshopOrders
            .Where(o => !matchedOrderIds.Contains(o.OrderId))
            .Select(o => o.OrderId)
            .ToList();

        return new ReconciliationReport(from, to, finixTransactions, eshopOrders, unmatchedFinix, unmatchedOrders);
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        return order ?? throw new PaymentOperationException(PaymentOperationError.NotFound, $"Order {orderId} was not found.");
    }

    private async Task<Order> GetOwnedOrderAsync(string ownerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, ownerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(PaymentOperationError.NotFound, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Payment> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _payments.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
        return payment ?? throw new PaymentOperationException(PaymentOperationError.NotFound, $"Payment for order {orderId} was not found.");
    }

    private async Task<PaymentCustomer> GetOrCreateCustomerIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        var customer = await _customers.FirstOrDefaultAsync(new PaymentCustomerByOwnerSpecification(ownerId), cancellationToken);
        if (customer is not null)
        {
            return customer;
        }

        var identity = await _finix.CreateBuyerIdentityAsync(cancellationToken);
        customer = new PaymentCustomer(ownerId, identity.Id, DateTimeOffset.UtcNow);
        try
        {
            customer = await _customers.AddAsync(customer, cancellationToken);
        }
        catch
        {
            var existing = await _customers.FirstOrDefaultAsync(
                new PaymentCustomerByOwnerSpecification(ownerId),
                cancellationToken);
            if (existing is null)
            {
                throw;
            }

            customer = existing;
        }

        _logger.LogInformation("Created Finix buyer identity {FinixIdentityId} for shopper.", identity.Id);
        return customer;
    }

    private async Task<PaymentView> BuildViewAsync(Order order, Payment payment, CancellationToken cancellationToken)
    {
        var refunds = await _refunds.ListAsync(new RefundsByPaymentIdSpecification(payment.Id), cancellationToken);
        return BuildView(order, payment, refunds);
    }

    private static PaymentView BuildView(Order order, Payment payment, IReadOnlyList<Refund> refunds)
    {
        var refundedCents = EffectiveRefundedCents(refunds);
        var hasAuthorization = !string.IsNullOrWhiteSpace(payment.AuthorizationId);

        return new PaymentView(
            order.Id,
            order.OrderDate,
            order.Total(),
            payment.Currency,
            payment.Status.ToString(),
            payment.FulfilmentStatus.ToString(),
            payment.AuthorizationId,
            payment.AuthorizationState,
            payment.CaptureTransferId,
            payment.CaptureTransferState,
            hasAuthorization ? payment.AmountCents / 100m : null,
            payment.CapturedAmountCents.HasValue ? payment.CapturedAmountCents.Value / 100m : null,
            payment.FeeCents.HasValue ? payment.FeeCents.Value / 100m : null,
            payment.NetCents.HasValue ? payment.NetCents.Value / 100m : null,
            refundedCents / 100m,
            payment.FailureMessage,
            refunds
                .OrderBy(r => r.CreatedAt)
                .Select(ToRefundView)
                .ToList());
    }

    private static RefundView ToRefundView(Refund refund)
    {
        return new RefundView(
            refund.Id,
            refund.AmountCents / 100m,
            refund.State,
            refund.FinixTransferId,
            refund.CreatedAt);
    }

    private async Task RefreshPendingRefundStatesAsync(IReadOnlyList<Refund> refunds, CancellationToken cancellationToken)
    {
        foreach (var refund in refunds.Where(r =>
                     string.Equals(r.State, "PENDING", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(r.State, "UNKNOWN", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(refund.FinixTransferId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(transfer.State)
                    && !string.Equals(transfer.State, refund.State, StringComparison.OrdinalIgnoreCase))
                {
                    refund.UpdateState(transfer.State);
                    await _refunds.UpdateAsync(refund, cancellationToken);
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning(
                    "Could not refresh Finix refund transfer {TransferId}: {Message}",
                    refund.FinixTransferId,
                    ex.Message);
            }
        }
    }

    private static long EffectiveRefundedCents(IEnumerable<Refund> refunds)
    {
        return refunds
            .Where(r => !string.Equals(r.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                        && !string.Equals(r.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            .Sum(r => r.AmountCents);
    }

    private static SavedCardView ToSavedCardView(SavedCard card)
    {
        return new SavedCardView(
            card.Id,
            card.Brand,
            card.LastFour,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.CardType,
            card.CreatedAt);
    }

    private async Task<decimal> ResolveFeeCentsAsync(FinixTransfer transfer, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var fees = await _finix.GetFeesForTransferAsync(transfer.Id, cancellationToken);
            if (fees.Count > 0)
            {
                return fees.Sum(f => f.Amount);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return transfer.Fee;
    }

    private async Task<FinixAuthorization?> TryRecoverCaptureAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            var authorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
            return string.IsNullOrWhiteSpace(authorization.Transfer) ? null : authorization;
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private static string BuildCaptureFailureMessage(string authorizationId, FinixApiException exception)
    {
        var baseMessage = $"Finix could not capture authorization {authorizationId}: {exception.Message}";
        if (!baseMessage.EndsWith(".", StringComparison.Ordinal)
            && !baseMessage.EndsWith("!", StringComparison.Ordinal))
        {
            baseMessage += ".";
        }

        if (exception.Message.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("void", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("already", StringComparison.OrdinalIgnoreCase))
        {
            return baseMessage +
                   " The card hold is no longer capturable. Cancel this order and ask the shopper to authorize payment again.";
        }

        return baseMessage + " Check the authorization in Finix, then retry fulfilment or cancel and ask the shopper to pay again.";
    }

    private static string BuildFinixFailureMessage(string operation, FinixApiException exception)
    {
        var reference = string.IsNullOrWhiteSpace(exception.LogReference) ? string.Empty : $" (Finix logref {exception.LogReference})";
        return $"Finix could not {operation} the payment: {exception.Message}{reference}";
    }

    private static bool IsDuplicateIdempotencyError(FinixApiException exception)
    {
        return exception.Message.Contains("Duplicate", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildRefundIdempotencyId(string paymentReference, string idempotencyKey)
    {
        var value = $"eshop-{paymentReference}-refund-{idempotencyKey}";
        if (value.Length <= 200)
        {
            return value;
        }

        using var sha = SHA256.Create();
        var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        return $"eshop-{paymentReference}-refund-{hash}";
    }

    private static CardDetails NormalizeCard(CardDetails card)
    {
        return card with { Number = new string(card.Number.Where(char.IsDigit).ToArray()) };
    }

    private static void ValidateCard(CardDetails card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The card number must contain between 12 and 19 digits.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The card expiration month must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year
            || (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The card expiration date is in the past.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode)
            || card.SecurityCode.Length is < 3 or > 4
            || !card.SecurityCode.All(char.IsDigit))
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The card security code must be 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(PaymentOperationError.Validation, "The cardholder name is required.");
        }
    }

    private static string LastFour(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    private static long ToCents(decimal amount)
    {
        return checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }
}
