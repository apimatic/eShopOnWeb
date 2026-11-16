using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Finix;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentService
{
    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly IUriComposer _uriComposer;
    private readonly OrderLockProvider _locks;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, IFinixClient finix, IUriComposer uriComposer,
        OrderLockProvider locks, ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _uriComposer = uriComposer;
        _locks = locks;
        _logger = logger;
    }

    public async Task<OrderView> CreateOrderAsync(string userId, CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var lines = BuildOrderLines(request);
        if (lines.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item and quantity is required.");
        }

        var catalogItemIds = lines.Select(l => l.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(i => i.Id));
            throw new PaymentValidationException($"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var catalogItemsById = catalogItems.ToDictionary(i => i.Id);
        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogItemsById[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(pictureUri));
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var shippingAddress = MapOrderAddress(request.ShippingAddress ?? request.ShipToAddress);
        var order = new Order(userId, shippingAddress, orderItems);
        order.InitializePayment(ToCents(order.Total()), _finix.Currency);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for {BuyerId} with {AmountCents} {Currency}.",
            order.Id, userId, order.AmountCents, order.Currency);

        return ToOrderView(order, Array.Empty<OrderRefund>());
    }

    public async Task<OrderView> PayAsync(string userId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = await _locks.AcquireAsync(request.OrderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken)
                        ?? throw new PaymentNotFoundException($"Order {request.OrderId} was not found.");

            if (!string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
            {
                throw new PaymentForbiddenException("This order belongs to a different shopper.");
            }

            if (order.Status == Order.StatusCancelled)
            {
                throw new PaymentConflictException("This order was cancelled and can no longer be paid.");
            }

            if (order.Status == Order.StatusFulfilled || order.CapturedAmountCents.HasValue)
            {
                return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId) &&
                order.PaymentStatus is Order.PaymentStatusAuthorized or Order.PaymentStatusCaptured
                    or Order.PaymentStatusPartiallyRefunded or Order.PaymentStatusRefunded)
            {
                return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
            }

            var card = ResolveCard(request);
            string source;
            string? securityCode;

            var savedCardId = request.PaymentMethodId ?? request.SavedPaymentMethodId ?? request.SavedCardId ?? request.CardId;
            if (savedCardId.HasValue)
            {
                var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
                    c => c.Id == savedCardId.Value && c.UserId == userId, cancellationToken)
                    ?? throw new PaymentNotFoundException("Saved payment method was not found.");

                source = savedCard.FinixPaymentInstrumentId;
                securityCode = ResolveSecurityCode(request);
            }
            else
            {
                if (card is null)
                {
                    throw new PaymentValidationException(
                        "Provide card details or the id of one of the shopper's saved payment methods.");
                }

                var (firstName, lastName) = SplitName(card.Name, userId);
                var identity = await _finix.CreateBuyerIdentityAsync(userId, firstName, lastName,
                    card.Address, cancellationToken);
                var paymentInstrument = await _finix.CreatePaymentInstrumentAsync(card, identity.Id,
                    cancellationToken);
                source = paymentInstrument.Id;
                securityCode = card.SecurityCode;
            }

            var idempotencyId = $"eshop-order-{order.Id}-auth-{order.PaymentAttemptKey}";
            var tags = new Dictionary<string, string>
            {
                ["order_id"] = order.Id.ToString(CultureInfo.InvariantCulture),
                ["eshop_order_id"] = order.Id.ToString(CultureInfo.InvariantCulture),
                ["eshop_order_ref"] = order.PaymentReference,
                ["buyer_id"] = userId
            };

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(order.AmountCents, source,
                    idempotencyId, tags, securityCode, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var message = BuildFinixFailureMessage("Payment authorization", ex);
                order.MarkPaymentFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                authorization.IsVoid == true)
            {
                var message = BuildAuthorizationFailure(authorization);
                order.MarkPaymentFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            if (authorization.Amount.HasValue && authorization.Amount.Value != order.AmountCents)
            {
                try
                {
                    await _finix.VoidAuthorizationAsync(authorization.Id, cancellationToken);
                }
                catch (FinixApiException voidException)
                {
                    _logger.LogWarning(voidException,
                        "Could not release mismatched authorization {AuthorizationId} for order {OrderId}.",
                        authorization.Id, order.Id);
                }

                var message =
                    $"Finix authorized {authorization.Amount.Value} cents instead of the order total " +
                    $"{order.AmountCents} cents. The hold was released; retry the payment.";
                order.MarkPaymentFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            order.MarkAuthorized(authorization.Id, authorization.State!, source,
                authorization.Amount ?? order.AmountCents, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Authorized order {OrderId} with Finix authorization {AuthorizationId} for {AmountCents} {Currency}.",
                order.Id, authorization.Id, order.AmountCents, order.Currency);

            return ToOrderView(order, Array.Empty<OrderRefund>());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = await _locks.AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

            if (order.Status == Order.StatusFulfilled || order.CapturedAmountCents.HasValue)
            {
                return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
            }

            if (order.Status == Order.StatusCancelled)
            {
                throw new PaymentConflictException("This order was cancelled and cannot be fulfilled.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                throw new PaymentConflictException(
                    "This order has no authorized payment to capture. Collect a payment before fulfilling it.");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var message = BuildFinixFailureMessage("Reading the authorization", ex);
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            var unusableReason = GetUnusableAuthorizationReason(authorization);
            if (unusableReason is not null)
            {
                var nextStep = unusableReason.Contains("already captured", StringComparison.OrdinalIgnoreCase)
                    ? "Use the refunds endpoint to return captured funds."
                    : "Cancel the order and collect a new payment before fulfilling it.";
                var message =
                    $"Finix authorization {order.FinixAuthorizationId} can no longer be captured: {unusableReason}. " +
                    nextStep;
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            if (authorization.Amount.HasValue && authorization.Amount.Value < order.AmountCents)
            {
                var message =
                    $"Finix authorization {order.FinixAuthorizationId} only holds {authorization.Amount.Value} cents " +
                    $"but the order total is {order.AmountCents} cents. Cancel the order and collect a new payment.";
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            FinixAuthorization captured;
            try
            {
                captured = await _finix.CaptureAuthorizationAsync(order.FinixAuthorizationId,
                    order.AmountCents, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var nextStep = ex.Message.Contains("already captured", StringComparison.OrdinalIgnoreCase)
                    ? " Use the refunds endpoint to return captured funds."
                    : " The hold may be expired, voided or already captured; cancel the order and collect a new payment.";
                var message = BuildFinixFailureMessage("Capturing the authorized payment", ex) + nextStep;
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            if (!string.Equals(captured.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(captured.Transfer))
            {
                var message =
                    $"Finix did not complete the capture for authorization {order.FinixAuthorizationId}: " +
                    $"{captured.FailureCode ?? captured.State ?? "unknown result"}" +
                    $"{(string.IsNullOrWhiteSpace(captured.FailureMessage) ? string.Empty : $" - {captured.FailureMessage}")}. " +
                    "Cancel the order and collect a new payment.";
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            FinixTransfer transfer;
            try
            {
                transfer = await _finix.GetTransferAsync(captured.Transfer, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var message = BuildFinixFailureMessage("Reading the captured transfer", ex);
                order.MarkAuthorizationUnusable(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentConflictException(message);
            }

            var capturedAmount = transfer.Amount ?? order.AmountCents;
            var fee = transfer.Fee ?? 0;
            var net = capturedAmount - fee;

            order.MarkFulfilled(captured.Transfer, captured.State!, capturedAmount, fee, net,
                DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Fulfilled order {OrderId}; captured {CapturedAmount} cents with fee {Fee} cents and net {Net} cents.",
                order.Id, capturedAmount, fee, net);

            return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = await _locks.AcquireAsync(orderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

            if (order.Status == Order.StatusCancelled)
            {
                return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
            }

            if (order.Status == Order.StatusFulfilled || order.CapturedAmountCents.HasValue)
            {
                throw new PaymentConflictException(
                    "This order was already fulfilled. Use the refunds endpoint to return captured funds.");
            }

            string? authorizationState = null;
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                FinixAuthorization authorization;
                try
                {
                    authorization = await _finix.GetAuthorizationAsync(order.FinixAuthorizationId,
                        cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    var message = BuildFinixFailureMessage("Reading the authorization", ex);
                    throw new PaymentConflictException(message);
                }

                if (!string.IsNullOrWhiteSpace(authorization.Transfer))
                {
                    throw new PaymentConflictException(
                        "Finix reports that this authorization was already captured. Use the refunds endpoint " +
                        "to return captured funds.");
                }

                if (authorization.IsVoid == true ||
                    string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    authorizationState = authorization.VoidState ?? "SUCCEEDED";
                }
                else if (string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var voided = await _finix.VoidAuthorizationAsync(order.FinixAuthorizationId,
                            cancellationToken);
                        authorizationState = voided.VoidState ?? voided.State ?? "PENDING";
                    }
                    catch (FinixApiException ex)
                    {
                        var message =
                            BuildFinixFailureMessage("Releasing the authorized payment", ex) +
                            " If the payment was already captured, use the refunds endpoint instead.";
                        throw new PaymentConflictException(message);
                    }
                }
                else
                {
                    authorizationState = authorization.VoidState ?? authorization.State;
                }
            }

            order.MarkCancelled(authorizationState);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Cancelled order {OrderId}; authorization state {AuthorizationState}.",
                order.Id, authorizationState ?? "none");

            return ToOrderView(order, await LoadRefundsAsync(order.Id, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundOperationResponse> RefundAsync(string userId, RefundOrderRequest request,
        string? headerIdempotencyKey, CancellationToken cancellationToken)
    {
        var gate = await _locks.AcquireAsync(request.OrderId, cancellationToken);
        try
        {
            var order = await LoadOrderAsync(request.OrderId, cancellationToken)
                        ?? throw new PaymentNotFoundException($"Order {request.OrderId} was not found.");

            if (!string.Equals(order.BuyerId, userId, StringComparison.Ordinal))
            {
                throw new PaymentForbiddenException("This order belongs to a different shopper.");
            }

            if (!order.CapturedAmountCents.HasValue ||
                string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
            {
                throw new PaymentConflictException(
                    "This order has not been fulfilled, so there is no captured payment to refund.");
            }

            var idempotencyKey = FirstNonEmpty(request.IdempotencyKey, request.IdempotencyId,
                request.Key, headerIdempotencyKey);
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new PaymentValidationException(
                    "An idempotency key is required for refunds. Supply idempotencyKey or the Idempotency-Key header.");
            }

            idempotencyKey = idempotencyKey!.Trim();
            var existingRefund = await _db.OrderRefunds.FirstOrDefaultAsync(
                r => r.OrderId == order.Id && r.IdempotencyKey == idempotencyKey, cancellationToken);
            if (existingRefund is not null)
            {
                var existingRefunds = await LoadRefundsAsync(order.Id, cancellationToken);
                return ToRefundOperationResponse(order, existingRefund, existingRefunds);
            }

            var remaining = order.CapturedAmountCents.Value - order.RefundedAmountCents;
            if (remaining <= 0)
            {
                throw new PaymentConflictException("This order has already been fully refunded.");
            }

            var refundAmount = request.AmountCents ??
                               (request.Amount.HasValue ? ToCents(request.Amount.Value) : remaining);

            if (refundAmount <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (refundAmount > remaining)
            {
                throw new PaymentConflictException(
                    $"The refund amount {refundAmount} cents exceeds the {remaining} cents still refundable " +
                    $"on the {order.CapturedAmountCents.Value} cent capture.");
            }

            FinixTransfer captureTransfer;
            try
            {
                captureTransfer = await _finix.GetTransferAsync(order.FinixCaptureTransferId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentConflictException(BuildFinixFailureMessage("Reading the captured transfer", ex));
            }

            if (string.Equals(captureTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(captureTransfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConflictException(
                    $"Finix reports the captured transfer {order.FinixCaptureTransferId} is {captureTransfer.State}, " +
                    "so it cannot be refunded.");
            }

            // Finix permits a full refund while a capture is still pending, but a
            // partial refund must wait until the capture transfer has succeeded.
            var isFullRefund = refundAmount == remaining;
            if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
                !isFullRefund)
            {
                captureTransfer = await WaitForSettledTransferAsync(order.FinixCaptureTransferId,
                    cancellationToken);
            }

            if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
                !(isFullRefund &&
                  string.Equals(captureTransfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                throw new PaymentConflictException(
                    $"Finix is still settling captured transfer {order.FinixCaptureTransferId} " +
                    $"(state {captureTransfer.State ?? "UNKNOWN"}). Retry the refund once it has succeeded.");
            }

            FinixTransfer reversal;
            try
            {
                reversal = await _finix.CreateReversalAsync(order.FinixCaptureTransferId, refundAmount,
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentConflictException(BuildFinixFailureMessage("Creating the refund", ex));
            }

            if (string.Equals(reversal.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(reversal.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentConflictException(
                    $"Finix did not complete the refund: {reversal.FailureCode ?? reversal.State}" +
                    $"{(string.IsNullOrWhiteSpace(reversal.FailureMessage) ? string.Empty : $" - {reversal.FailureMessage}")}.");
            }

            var refund = new OrderRefund(order.Id, reversal.Id, refundAmount, order.Currency,
                reversal.State ?? "PENDING", idempotencyKey, DateTimeOffset.UtcNow,
                reversal.FailureCode, reversal.FailureMessage);

            _db.OrderRefunds.Add(refund);
            await _db.SaveChangesAsync(cancellationToken);

            order.ApplyRefund(order.RefundedAmountCents + refundAmount);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refunded order {OrderId} by {RefundAmount} cents with Finix reversal {ReversalId}; total refunded {RefundedAmount}.",
                order.Id, refundAmount, reversal.Id, order.RefundedAmountCents);

            var refunds = await LoadRefundsAsync(order.Id, cancellationToken);
            return ToRefundOperationResponse(order, refund, refunds);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(string userId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == userId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var refunds = await _db.OrderRefunds
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        var refundsByOrder = refunds.GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<OrderRefund>)g.OrderBy(r => r.CreatedAt).ToList());

        return new MyOrdersResponse
        {
            Orders = orders.Select(order => ToOrderView(order,
                refundsByOrder.TryGetValue(order.Id, out var orderRefunds)
                    ? orderRefunds
                    : Array.Empty<OrderRefund>())).ToList()
        };
    }

    public async Task<SavedCardView> SaveCardAsync(string userId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        var card = ResolveCard(request);
        if (card is null)
        {
            throw new PaymentValidationException("Card number, expiration month and expiration year are required.");
        }

        var (firstName, lastName) = SplitName(card.Name, userId);
        var identity = await _finix.CreateBuyerIdentityAsync(userId, firstName, lastName, card.Address,
            cancellationToken);
        var paymentInstrument = await _finix.CreatePaymentInstrumentAsync(card, identity.Id, cancellationToken);

        var savedCard = new SavedCard(userId, paymentInstrument.Id, identity.Id, paymentInstrument.Brand,
            paymentInstrument.LastFour, paymentInstrument.ExpirationMonth ?? card.ExpirationMonth,
            paymentInstrument.ExpirationYear ?? card.ExpirationYear, card.Name);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved Finix payment instrument {PaymentInstrumentId} for {BuyerId}.",
            paymentInstrument.Id, userId);

        return ToSavedCardView(savedCard);
    }

    public async Task<SavedCardsResponse> GetPaymentMethodsAsync(string userId,
        CancellationToken cancellationToken)
    {
        var cards = await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var views = cards.Select(ToSavedCardView).ToList();
        return new SavedCardsResponse { PaymentMethods = views, Cards = views.ToList() };
    }

    public async Task DeletePaymentMethodAsync(string userId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
            c => c.Id == paymentMethodId && c.UserId == userId, cancellationToken)
            ?? throw new PaymentNotFoundException("Saved payment method was not found.");

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("Finix payment instrument {PaymentInstrumentId} was already absent; removing local card.",
                savedCard.FinixPaymentInstrumentId);
        }

        _db.SavedCards.Remove(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted saved payment method {PaymentMethodId} for {BuyerId}.",
            paymentMethodId, userId);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentValidationException("'from' must be earlier than or equal to 'to'.");
        }

        var transfersTask = _finix.ListTransfersAsync(from, to, cancellationToken);
        var authorizationsTask = _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        await Task.WhenAll(transfersTask, authorizationsTask);

        var transfers = (await transfersTask).ToList();
        var authorizations = (await authorizationsTask).ToList();

        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var refunds = await _db.OrderRefunds.AsNoTracking().ToListAsync(cancellationToken);
        var refundsByOrder = refunds.GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var ordersInRange = orders.Where(order => IsOrderInRange(order, from, to, refundsByOrder))
            .OrderBy(o => o.Id)
            .ToList();

        // Finix list endpoints can lag behind a transaction that was just created.
        // Directly fetch any Finix resource this application already knows about so
        // reconciliation is immediately useful, while still surfacing truly unknown
        // Finix transactions from the list response.
        var transferIds = transfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var authorizationIds = authorizations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var order in ordersInRange)
        {
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId) &&
                !authorizationIds.Contains(order.FinixAuthorizationId!))
            {
                try
                {
                    var authorization = await _finix.GetAuthorizationAsync(order.FinixAuthorizationId!,
                        cancellationToken);
                    if (IsInRange(authorization.CreatedAt, from, to))
                    {
                        authorizations.Add(authorization);
                        authorizationIds.Add(authorization.Id);
                    }
                }
                catch (FinixApiException ex) when (ex.StatusCode == 404)
                {
                }
            }

            if (!string.IsNullOrWhiteSpace(order.FinixCaptureTransferId) &&
                !transferIds.Contains(order.FinixCaptureTransferId!))
            {
                try
                {
                    var transfer = await _finix.GetTransferAsync(order.FinixCaptureTransferId!,
                        cancellationToken);
                    if (IsInRange(transfer.CreatedAt, from, to))
                    {
                        transfers.Add(transfer);
                        transferIds.Add(transfer.Id);
                    }
                }
                catch (FinixApiException ex) when (ex.StatusCode == 404)
                {
                }
            }
        }

        foreach (var refund in refunds)
        {
            if (!string.IsNullOrWhiteSpace(refund.FinixTransferId) &&
                !transferIds.Contains(refund.FinixTransferId))
            {
                try
                {
                    var transfer = await _finix.GetTransferAsync(refund.FinixTransferId,
                        cancellationToken);
                    if (IsInRange(transfer.CreatedAt, from, to))
                    {
                        transfers.Add(transfer);
                        transferIds.Add(transfer.Id);
                    }
                }
                catch (FinixApiException ex) when (ex.StatusCode == 404)
                {
                }
            }
        }

        var knownFinixIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var knownOrderReferences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.PaymentReference))
            {
                knownOrderReferences[order.PaymentReference] = order.Id;
            }

            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                knownFinixIds[order.FinixAuthorizationId!] = order.Id;
            }

            if (!string.IsNullOrWhiteSpace(order.FinixCaptureTransferId))
            {
                knownFinixIds[order.FinixCaptureTransferId!] = order.Id;
            }
        }

        foreach (var refund in refunds)
        {
            if (!string.IsNullOrWhiteSpace(refund.FinixTransferId))
            {
                knownFinixIds[refund.FinixTransferId] = refund.OrderId;
            }
        }

        var entries = new List<ReconciliationEntry>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var transfer in transfers)
        {
            var matchedOrderId = MatchTransfer(transfer, knownFinixIds, knownOrderReferences);
            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
            }

            entries.Add(new ReconciliationEntry
            {
                Id = transfer.Id,
                ResourceType = "TRANSFER",
                Type = transfer.Type,
                State = transfer.State,
                Amount = transfer.Amount,
                Fee = transfer.Fee,
                Currency = transfer.Currency,
                CreatedAt = transfer.CreatedAt,
                OrderId = matchedOrderId,
                Matched = matchedOrderId.HasValue,
                MatchStatus = matchedOrderId.HasValue ? "Matched" : "UnmatchedFinixTransaction",
                Tags = transfer.Tags
            });
        }

        foreach (var authorization in authorizations)
        {
            var matchedOrderId = MatchAuthorization(authorization, knownFinixIds, knownOrderReferences);
            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
            }

            entries.Add(new ReconciliationEntry
            {
                Id = authorization.Id,
                ResourceType = "AUTHORIZATION",
                Type = "AUTHORIZATION",
                State = authorization.State,
                Amount = authorization.Amount,
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt,
                OrderId = matchedOrderId,
                Matched = matchedOrderId.HasValue,
                MatchStatus = matchedOrderId.HasValue ? "Matched" : "UnmatchedFinixTransaction",
                Tags = authorization.Tags
            });
        }

        entries = entries.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id, StringComparer.Ordinal).ToList();

        var orderEntries = ordersInRange.Select(order =>
        {
            var hasPayment = !string.IsNullOrWhiteSpace(order.FinixAuthorizationId) ||
                             !string.IsNullOrWhiteSpace(order.FinixCaptureTransferId);
            var matched = matchedOrderIds.Contains(order.Id);

            return new ReconciliationOrderEntry
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status,
                PaymentStatus = order.PaymentStatus,
                AmountCents = order.AmountCents,
                Currency = order.Currency,
                AuthorizationId = order.FinixAuthorizationId,
                CaptureId = order.FinixCaptureTransferId,
                RefundedAmount = order.RefundedAmountCents,
                Matched = matched,
                MatchStatus = matched
                    ? "Matched"
                    : hasPayment
                        ? "UnmatchedEshopOrder"
                        : "NoPayment"
            };
        }).ToList();

        var unmatchedFinix = entries.Where(e => !e.Matched).ToList();
        var unmatchedOrders = orderEntries.Where(o => o.MatchStatus == "UnmatchedEshopOrder").ToList();

        return new ReconciliationResponse
        {
            From = from,
            To = to,
            Transactions = entries,
            FinixTransactions = entries.ToList(),
            UnmatchedFinixTransactions = unmatchedFinix,
            Orders = orderEntries,
            EshopOrders = orderEntries.ToList(),
            UnmatchedEshopOrders = unmatchedOrders,
            Summary = new ReconciliationSummary
            {
                FinixTransactionCount = entries.Count,
                MatchedFinixTransactionCount = entries.Count(e => e.Matched),
                UnmatchedFinixTransactionCount = unmatchedFinix.Count,
                EshopOrderCount = orderEntries.Count,
                MatchedEshopOrderCount = orderEntries.Count(o => o.Matched),
                UnmatchedEshopOrderCount = unmatchedOrders.Count
            }
        };
    }

    private static List<OrderLine> BuildOrderLines(CreateOrderRequest request)
    {
        var lines = new List<OrderLine>();

        foreach (var item in request.Items ?? new List<CreateOrderItemRequest>())
        {
            if (item is null)
            {
                continue;
            }

            var catalogItemId = item.CatalogItemId > 0 ? item.CatalogItemId : item.Id;
            var quantity = item.Quantity > 0 ? item.Quantity : item.Units;
            if (catalogItemId <= 0 || quantity <= 0)
            {
                throw new PaymentValidationException(
                    "Each order item must have a positive catalogItemId and quantity.");
            }

            lines.Add(new OrderLine(catalogItemId, quantity));
        }

        if (lines.Count == 0 && request.CatalogItemIds is { Count: > 0 })
        {
            lines.AddRange(request.CatalogItemIds.Select(id => new OrderLine(id, 1)));
        }

        return lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new OrderLine(g.Key, g.Sum(l => l.Quantity)))
            .ToList();
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    private async Task<IReadOnlyList<OrderRefund>> LoadRefundsAsync(int orderId,
        CancellationToken cancellationToken)
    {
        return await _db.OrderRefunds
            .AsNoTracking()
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private static OrderView ToOrderView(Order order, IReadOnlyList<OrderRefund> refunds)
    {
        var total = order.Total();
        var refundViews = refunds.Select(ToRefundView).ToList();
        var payment = new PaymentView
        {
            Status = order.PaymentStatus,
            Currency = order.Currency,
            AmountCents = order.AmountCents,
            AuthorizationId = order.FinixAuthorizationId,
            AuthorizationState = order.FinixAuthorizationState,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            PaymentInstrumentId = order.FinixPaymentInstrumentId,
            CaptureId = order.FinixCaptureTransferId,
            CaptureState = order.FinixCaptureState,
            CapturedAmount = order.CapturedAmountCents,
            Fee = order.FinixFeeCents,
            Net = order.NetProceedsCents,
            RefundedAmount = order.RefundedAmountCents,
            FailureMessage = order.PaymentFailureMessage,
            Refunds = refundViews
        };

        return new OrderView
        {
            OrderId = order.Id,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            OrderDate = order.OrderDate,
            Currency = order.Currency,
            OrderTotal = total,
            Total = total,
            AmountCents = order.AmountCents,
            AuthorizationId = order.FinixAuthorizationId,
            AuthorizationState = order.FinixAuthorizationState,
            AuthorizationExpiresAt = order.AuthorizationExpiresAt,
            PaymentInstrumentId = order.FinixPaymentInstrumentId,
            CaptureId = order.FinixCaptureTransferId,
            CaptureState = order.FinixCaptureState,
            CapturedAmount = order.CapturedAmountCents,
            CapturedAmountCents = order.CapturedAmountCents,
            Fee = order.FinixFeeCents,
            FeeCents = order.FinixFeeCents,
            Net = order.NetProceedsCents,
            NetCents = order.NetProceedsCents,
            RefundedAmount = order.RefundedAmountCents,
            RefundedAmountCents = order.RefundedAmountCents,
            PaymentFailureMessage = order.PaymentFailureMessage,
            Items = order.OrderItems.Select(item => new OrderItemView
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Units = item.Units,
                LineTotal = item.UnitPrice * item.Units
            }).ToList(),
            Payment = payment
        };
    }

    private static RefundView ToRefundView(OrderRefund refund)
    {
        return new RefundView
        {
            RefundId = refund.Id,
            OrderId = refund.OrderId,
            Amount = refund.AmountCents,
            AmountCents = refund.AmountCents,
            Currency = refund.Currency,
            Status = refund.Status,
            FinixTransferId = refund.FinixTransferId,
            ReversalId = refund.FinixTransferId,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt,
            FailureCode = refund.FailureCode,
            FailureMessage = refund.FailureMessage
        };
    }

    private static RefundOperationResponse ToRefundOperationResponse(Order order, OrderRefund refund,
        IReadOnlyList<OrderRefund> refunds)
    {
        var orderView = ToOrderView(order, refunds);
        return new RefundOperationResponse
        {
            OrderId = order.Id,
            RefundId = refund.Id,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            RefundedAmount = order.RefundedAmountCents,
            Refund = ToRefundView(refund),
            Payment = orderView.Payment,
            Order = orderView
        };
    }

    private static SavedCardView ToSavedCardView(SavedCard card)
    {
        return new SavedCardView
        {
            PaymentMethodId = card.Id,
            Id = card.Id,
            Brand = card.Brand ?? string.Empty,
            Last4 = card.Last4 ?? string.Empty,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Name = card.CardholderName,
            CreatedAt = card.CreatedAt
        };
    }

    private static FinixCardDetails? ResolveCard(PaymentCardRequest request)
    {
        var source = request.Card is not null &&
                     !string.IsNullOrWhiteSpace(FirstNonEmpty(request.Card.Number, request.Card.CardNumber))
            ? request.Card
            : request;

        var number = FirstNonEmpty(source.Number, source.CardNumber);
        var expirationMonth = source.ExpirationMonth > 0
            ? source.ExpirationMonth
            : source.ExpMonth > 0 ? source.ExpMonth : source.ExpiryMonth;
        var expirationYear = source.ExpirationYear > 0
            ? source.ExpirationYear
            : source.ExpYear > 0 ? source.ExpYear : source.ExpiryYear;
        var securityCode = FirstNonEmpty(source.SecurityCode, source.Cvv, source.Cvc);
        var name = FirstNonEmpty(source.Name, source.CardholderName);
        var address = source.Address ?? source.BillingAddress;

        if (string.IsNullOrWhiteSpace(number) || expirationMonth <= 0 || expirationYear <= 0)
        {
            return null;
        }

        return new FinixCardDetails
        {
            Number = number!.Replace(" ", string.Empty).Replace("-", string.Empty),
            ExpirationMonth = expirationMonth,
            ExpirationYear = expirationYear,
            SecurityCode = securityCode,
            Name = name,
            Address = MapFinixAddress(address)
        };
    }

    private static string? ResolveSecurityCode(PaymentCardRequest request)
    {
        var fromCard = request.Card is null
            ? null
            : FirstNonEmpty(request.Card.SecurityCode, request.Card.Cvv, request.Card.Cvc);
        return FirstNonEmpty(request.SecurityCode, request.Cvv, request.Cvc, fromCard);
    }

    private static FinixAddress? MapFinixAddress(AddressRequest? address)
    {
        if (address is null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = FirstNonEmpty(address.Street, address.Line1, address.Address1),
            City = address.City,
            Region = FirstNonEmpty(address.State, address.Region),
            PostalCode = FirstNonEmpty(address.ZipCode, address.PostalCode, address.Zip),
            Country = string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country
        };
    }

    private static Address MapOrderAddress(AddressRequest? address)
    {
        return new Address(
            FirstNonEmpty(address?.Street, address?.Line1, address?.Address1) ?? "1 Main St",
            FirstNonEmpty(address?.City) ?? "Seattle",
            FirstNonEmpty(address?.State, address?.Region) ?? "WA",
            FirstNonEmpty(address?.Country) ?? "USA",
            FirstNonEmpty(address?.ZipCode, address?.PostalCode, address?.Zip) ?? "98101");
    }

    private static (string FirstName, string LastName) SplitName(string? name, string userId)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            var localPart = userId.Split('@')[0];
            trimmed = string.IsNullOrWhiteSpace(localPart) ? "eShop Customer" : localPart;
        }

        var parts = trimmed!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = Truncate(parts.Length > 0 ? parts[0] : "eShop", 20);
        var last = Truncate(parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : "Customer", 20);
        return (first, last);
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static int ToCents(decimal amount)
    {
        return checked((int)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string BuildAuthorizationFailure(FinixAuthorization authorization)
    {
        var detail = FirstNonEmpty(authorization.FailureMessage, authorization.FailureCode,
            authorization.State, "unknown result");
        return $"Payment authorization failed: {detail}.";
    }

    private static string BuildFinixFailureMessage(string action, FinixApiException exception)
    {
        return $"{action} failed: {exception.Message}";
    }

    private async Task<FinixTransfer> WaitForSettledTransferAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        FinixTransfer transfer;

        do
        {
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
            if (transfer.State is "SUCCEEDED" or "FAILED" or "CANCELED")
            {
                return transfer;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < deadline);

        return transfer;
    }

    private static string? GetUnusableAuthorizationReason(FinixAuthorization authorization)
    {
        if (authorization.IsVoid == true ||
            string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return "the authorization was voided";
        }

        if (!string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            return "the authorization was already captured";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Finix reports authorization state {authorization.State ?? "UNKNOWN"}";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"the authorization expired at {authorization.ExpiresAt.Value:O}";
        }

        return null;
    }

    private static bool IsInRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
        => value.HasValue && value.Value >= from && value.Value <= to;

    private static bool IsOrderInRange(Order order, DateTimeOffset from, DateTimeOffset to,
        IReadOnlyDictionary<int, List<OrderRefund>> refundsByOrder)
    {
        static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
            => value.HasValue && value.Value >= from && value.Value <= to;

        if (InRange(order.OrderDate, from, to) ||
            InRange(order.AuthorizedAt, from, to) ||
            InRange(order.FulfilledAt, from, to) ||
            InRange(order.CancelledAt, from, to))
        {
            return true;
        }

        return refundsByOrder.TryGetValue(order.Id, out var refunds) &&
               refunds.Any(r => InRange(r.CreatedAt, from, to));
    }

    private static int? MatchTransfer(FinixTransfer transfer, IReadOnlyDictionary<string, int> knownFinixIds,
        IReadOnlyDictionary<string, int> knownOrderReferences)
    {
        if (knownFinixIds.TryGetValue(transfer.Id, out var orderId))
        {
            return orderId;
        }

        if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer) &&
            knownFinixIds.TryGetValue(transfer.ParentTransfer, out orderId))
        {
            return orderId;
        }

        return TryGetOrderIdFromTags(transfer.Tags, knownOrderReferences);
    }

    private static int? MatchAuthorization(FinixAuthorization authorization,
        IReadOnlyDictionary<string, int> knownFinixIds,
        IReadOnlyDictionary<string, int> knownOrderReferences)
    {
        if (knownFinixIds.TryGetValue(authorization.Id, out var orderId))
        {
            return orderId;
        }

        return TryGetOrderIdFromTags(authorization.Tags, knownOrderReferences);
    }

    private static int? TryGetOrderIdFromTags(IReadOnlyDictionary<string, string>? tags,
        IReadOnlyDictionary<string, int> knownOrderReferences)
    {
        if (tags is null)
        {
            return null;
        }

        if (tags.TryGetValue("eshop_order_ref", out var reference) &&
            knownOrderReferences.TryGetValue(reference, out var orderId))
        {
            return orderId;
        }

        return null;
    }

    private sealed record OrderLine(int CatalogItemId, int Quantity);
}










