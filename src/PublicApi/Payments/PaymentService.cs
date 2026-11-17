using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService : IPaymentService
{
    private readonly CatalogContext _db;
    private readonly IFinixGateway _gateway;
    private readonly IOptions<FinixOptions> _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        IFinixGateway gateway,
        IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _options = options;
        _logger = logger;
    }

    public async Task<CreateOrderResult> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "ORDER_ITEMS_REQUIRED", "At least one catalog item is required.");
        }

        if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "INVALID_ORDER_ITEM", "Each item needs a positive catalog item id and quantity.");
        }

        var catalogItemIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var missing = catalogItemIds.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentOperationException(
                400,
                "CATALOG_ITEM_NOT_FOUND",
                $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = catalogItems.Single(i => i.Id == line.CatalogItemId);
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var address = request.ShipToAddress is null
            ? new Address("Not provided", "Not provided", string.Empty, "Not provided", "Not provided")
            : new Address(
                request.ShipToAddress.Street,
                request.ShipToAddress.City,
                request.ShipToAddress.State,
                request.ShipToAddress.Country,
                request.ShipToAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            Currency = _options.Value.Currency,
            Status = PaymentStatus.AwaitingPayment,
            FulfilmentStatus = FulfilmentStatus.NotFulfilled
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        var totalCents = ToCents(order.Total());
        _logger.LogInformation(
            "Created order {OrderId} for buyer {BuyerId}; total={TotalCents} {Currency} awaiting payment.",
            order.Id,
            buyerId,
            totalCents,
            payment.Currency);

        return new CreateOrderResult
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalCents = totalCents,
            Currency = payment.Currency,
            PaymentStatus = payment.Status.ToString(),
            Items = orderItems.Select(MapLine).ToList()
        };
    }

    public async Task<PaymentView> PayOrderAsync(
        string buyerId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderAsync(request.OrderId, buyerId, cancellationToken);
        var payment = await EnsurePaymentAsync(order, cancellationToken);

        if (payment.Status is PaymentStatus.Authorized
            or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded
            or PaymentStatus.Refunded)
        {
            return await BuildPaymentViewAsync(order, payment, cancellationToken);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentOperationException(
                409,
                "ORDER_CANCELLED",
                "This order was cancelled and can no longer be paid.");
        }

        var totalCents = ToCents(order.Total());
        if (totalCents <= 0)
        {
            throw new PaymentOperationException(400, "INVALID_ORDER_TOTAL", "The order total must be greater than zero.");
        }

        string source;
        try
        {
            source = await ResolvePaymentSourceAsync(buyerId, request, cancellationToken);
        }
        catch (FinixProviderException ex)
        {
            throw MapProviderError(ex, "The card could not be prepared for payment.");
        }

        var claimResult = await TryClaimAsync($"authorize:{order.Id}", "default", cancellationToken);
        if (claimResult.IsDuplicate)
        {
            if (claimResult.Claim.Status == PaymentClaimStatus.Completed)
            {
                payment = await LoadPaymentAsync(order.Id, cancellationToken) ?? payment;
                return await BuildPaymentViewAsync(order, payment, cancellationToken);
            }

            var settled = await TrySettleAuthorizationAsync(
                claimResult.Claim,
                order,
                payment,
                totalCents,
                source,
                cancellationToken);
            if (settled is not null && payment.Status == PaymentStatus.Authorized)
            {
                await CompleteClaimAsync(
                    claimResult.Claim,
                    JsonSerializer.Serialize(new { authorizationId = payment.AuthorizationId, state = payment.AuthorizationState }),
                    cancellationToken);
                return await BuildPaymentViewAsync(order, payment, cancellationToken);
            }

            if (settled is not null)
            {
                await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
                throw new PaymentOperationException(
                    402,
                    payment.FailureCode ?? "AUTHORIZATION_FAILED",
                    payment.FailureMessage ?? "The card authorization failed.",
                    "Ask the shopper to use another card.");
            }

            throw new PaymentOperationException(
                409,
                "PAYMENT_IN_PROGRESS",
                "A payment for this order is already being processed.",
                "Wait for the in-flight payment to settle before retrying.");
        }

        var idempotencyId = $"order-{order.Id}-authorize-{Guid.NewGuid():N}";
        claimResult.Claim.Reference = idempotencyId;
        await SaveClaimAsync(claimResult.Claim, cancellationToken);

        try
        {
            var authorization = await _gateway.AuthorizeAsync(totalCents, source, idempotencyId, cancellationToken);
            await ApplyAuthorizationAsync(order, payment, authorization, totalCents, source, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            if (payment.Status == PaymentStatus.Authorized)
            {
                await CompleteClaimAsync(
                    claimResult.Claim,
                    JsonSerializer.Serialize(new { authorizationId = payment.AuthorizationId, state = payment.AuthorizationState }),
                    cancellationToken);
                _logger.LogInformation(
                    "Authorized order {OrderId} with Finix authorization {AuthorizationId} in state {State}.",
                    order.Id,
                    payment.AuthorizationId,
                    payment.AuthorizationState);
                return await BuildPaymentViewAsync(order, payment, cancellationToken);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw new PaymentOperationException(
                payment.FailureCode == "AUTHORIZATION_AMOUNT_MISMATCH" ? 409 : 402,
                payment.FailureCode ?? "AUTHORIZATION_FAILED",
                payment.FailureMessage ?? "The card authorization failed.",
                "Ask the shopper to use another card.");
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                var settled = await TrySettleAuthorizationAsync(
                    claimResult.Claim,
                    order,
                    payment,
                    totalCents,
                    source,
                    cancellationToken);
                if (settled is not null && payment.Status == PaymentStatus.Authorized)
                {
                    await CompleteClaimAsync(
                        claimResult.Claim,
                        JsonSerializer.Serialize(new { authorizationId = payment.AuthorizationId, state = payment.AuthorizationState }),
                        cancellationToken);
                    return await BuildPaymentViewAsync(order, payment, cancellationToken);
                }

                if (settled is not null)
                {
                    await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
                    throw new PaymentOperationException(
                        402,
                        payment.FailureCode ?? "AUTHORIZATION_FAILED",
                        payment.FailureMessage ?? "The card authorization failed.",
                        "Ask the shopper to use another card.");
                }

                await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                throw new PaymentOperationException(
                    502,
                    "PAYMENT_OUTCOME_UNKNOWN",
                    "The payment provider did not confirm the authorization result.",
                    "Check reconciliation and the order payment state before retrying; the shopper may already have funds on hold.",
                    ex);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = ex.Code;
            payment.FailureMessage = ex.Message;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Finix declined authorization for order {OrderId}: {Code}.",
                order.Id,
                ex.Code);
            throw MapProviderError(ex, "The card authorization was declined.");
        }
    }

    public async Task<PaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken)
            ?? throw new PaymentOperationException(404, "ORDER_NOT_FOUND", "Order not found.");
        var payment = await EnsurePaymentAsync(order, cancellationToken);

        if (payment.FulfilmentStatus == FulfilmentStatus.Fulfilled && payment.Status == PaymentStatus.Captured)
        {
            return await BuildPaymentViewAsync(order, payment, cancellationToken);
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            throw new PaymentOperationException(409, "ORDER_CANCELLED", "This order was cancelled and cannot be fulfilled.");
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentOperationException(
                409,
                "ORDER_NOT_AUTHORIZED",
                "The order is not authorized and cannot be fulfilled.",
                "The shopper must pay for the order before it can be fulfilled.");
        }

        var totalCents = ToCents(order.Total());
        var authorization = await GetAuthorizationForOperatorAsync(payment.AuthorizationId!, cancellationToken);

        if (authorization.IsVoid == true
            || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.Ordinal)
            || string.Equals(authorization.State, "CANCELED", StringComparison.Ordinal)
            || string.Equals(authorization.State, "FAILED", StringComparison.Ordinal))
        {
            payment.AuthorizationState = authorization.State;
            payment.VoidState = authorization.VoidState;
            payment.FailureCode = "AUTHORIZATION_NOT_CAPTURABLE";
            payment.FailureMessage =
                $"Finix authorization {authorization.Id} is in state '{authorization.State}' (void state '{authorization.VoidState}') and can no longer be captured.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(
                409,
                "AUTHORIZATION_NOT_CAPTURABLE",
                payment.FailureMessage,
                "Cancel the order and ask the shopper to pay again, or reconcile the authorization with Finix before retrying.");
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.Ordinal))
        {
            payment.AuthorizationState = authorization.State;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(
                409,
                "AUTHORIZATION_NOT_SETTLED",
                $"Finix authorization {authorization.Id} is in state '{authorization.State}', not 'SUCCEEDED'.",
                "Wait for the authorization to settle, then retry fulfilment; if it does not settle, cancel and ask the shopper to pay again.");
        }

        if (authorization.Amount.HasValue && authorization.Amount.Value != totalCents)
        {
            throw new PaymentOperationException(
                409,
                "AUTHORIZATION_AMOUNT_MISMATCH",
                $"Finix holds {authorization.Amount.Value} cents but the order total is {totalCents} cents.",
                "Do not capture a mismatched amount. Cancel the order and ask the shopper to pay again.");
        }

        var claimResult = await TryClaimAsync($"capture:{order.Id}", "default", cancellationToken);
        if (claimResult.IsDuplicate)
        {
            if (claimResult.Claim.Status == PaymentClaimStatus.Completed)
            {
                payment = await LoadPaymentAsync(order.Id, cancellationToken) ?? payment;
                return await BuildPaymentViewAsync(order, payment, cancellationToken);
            }

            var reread = await TryGetAuthorizationSafeAsync(payment.AuthorizationId!, cancellationToken);
            if (reread is not null
                && string.Equals(reread.State, "SUCCEEDED", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(reread.TransferId))
            {
                var feesComplete = await FinalizeCaptureAsync(order, payment, reread, reread.TransferId!, claimResult.Claim, cancellationToken);
                return await BuildPaymentViewAsync(order, payment, cancellationToken, feesComplete);
            }

            throw new PaymentOperationException(
                409,
                "CAPTURE_IN_PROGRESS",
                "A capture for this order is already being processed.",
                "Wait for the in-flight capture to settle before retrying.");
        }

        try
        {
            var capture = await _gateway.CaptureAsync(payment.AuthorizationId!, totalCents, cancellationToken);
            var transferId = capture.TransferId;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var reread = await _gateway.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                transferId = reread.TransferId;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new FinixProviderException(
                    null,
                    "CAPTURE_MISSING_TRANSFER",
                    "Finix accepted the capture but returned no transfer identifier.",
                    outcomeUnknown: true);
            }

            var feesComplete = await FinalizeCaptureAsync(order, payment, capture, transferId!, claimResult.Claim, cancellationToken);
            return await BuildPaymentViewAsync(order, payment, cancellationToken, feesComplete);
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                var reread = await TryGetAuthorizationSafeAsync(payment.AuthorizationId!, cancellationToken);
                if (reread is not null
                    && string.Equals(reread.State, "SUCCEEDED", StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(reread.TransferId))
                {
                    var feesComplete = await FinalizeCaptureAsync(order, payment, reread, reread.TransferId!, claimResult.Claim, cancellationToken);
                    return await BuildPaymentViewAsync(order, payment, cancellationToken, feesComplete);
                }

                await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                throw new PaymentOperationException(
                    502,
                    "CAPTURE_OUTCOME_UNKNOWN",
                    "The payment provider did not confirm the capture result.",
                    "Check reconciliation before retrying; the shopper may already have been charged.",
                    ex);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw MapProviderError(ex, "The authorization could not be captured.");
        }
    }

    public async Task<PaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken)
            ?? throw new PaymentOperationException(404, "ORDER_NOT_FOUND", "Order not found.");
        var payment = await EnsurePaymentAsync(order, cancellationToken);

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return await BuildPaymentViewAsync(order, payment, cancellationToken);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(
                409,
                "ORDER_ALREADY_CAPTURED",
                "This order has already been captured and cannot be cancelled.",
                "Use the refunds endpoint to return captured funds.");
        }

        if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            var claimResult = await TryClaimAsync($"void:{order.Id}", "default", cancellationToken);
            if (claimResult.IsDuplicate)
            {
                if (claimResult.Claim.Status == PaymentClaimStatus.Completed)
                {
                    payment = await LoadPaymentAsync(order.Id, cancellationToken) ?? payment;
                    return await BuildPaymentViewAsync(order, payment, cancellationToken);
                }

                var reread = await TryGetAuthorizationSafeAsync(payment.AuthorizationId!, cancellationToken);
                if (reread is not null && IsVoided(reread))
                {
                    ApplyVoid(payment, reread);
                    await _db.SaveChangesAsync(cancellationToken);
                    await CompleteClaimAsync(claimResult.Claim, JsonSerializer.Serialize(new { voidState = payment.VoidState }), cancellationToken);
                    return await BuildPaymentViewAsync(order, payment, cancellationToken);
                }

                throw new PaymentOperationException(
                    409,
                    "CANCEL_IN_PROGRESS",
                    "A cancellation for this order is already being processed.",
                    "Wait for the in-flight cancellation to settle before retrying.");
            }

            try
            {
                var voided = await _gateway.VoidAsync(payment.AuthorizationId!, cancellationToken);
                ApplyVoid(payment, voided);
                await _db.SaveChangesAsync(cancellationToken);
                await CompleteClaimAsync(claimResult.Claim, JsonSerializer.Serialize(new { voidState = payment.VoidState }), cancellationToken);
                _logger.LogInformation(
                    "Cancelled order {OrderId}; Finix authorization {AuthorizationId} void state {VoidState}.",
                    order.Id,
                    payment.AuthorizationId,
                    payment.VoidState);
                return await BuildPaymentViewAsync(order, payment, cancellationToken);
            }
            catch (FinixProviderException ex)
            {
                if (ex.OutcomeUnknown)
                {
                    var reread = await TryGetAuthorizationSafeAsync(payment.AuthorizationId!, cancellationToken);
                    if (reread is not null && IsVoided(reread))
                    {
                        ApplyVoid(payment, reread);
                        await _db.SaveChangesAsync(cancellationToken);
                        await CompleteClaimAsync(claimResult.Claim, JsonSerializer.Serialize(new { voidState = payment.VoidState }), cancellationToken);
                        return await BuildPaymentViewAsync(order, payment, cancellationToken);
                    }

                    await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                    throw new PaymentOperationException(
                        502,
                        "CANCEL_OUTCOME_UNKNOWN",
                        "The payment provider did not confirm the cancellation result.",
                        "Check reconciliation before retrying; the hold may already be released.",
                        ex);
                }

                await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
                throw MapProviderError(ex, "The authorization could not be released.");
            }
        }

        payment.Status = PaymentStatus.Cancelled;
        payment.FulfilmentStatus = FulfilmentStatus.Cancelled;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Cancelled order {OrderId} before any funds were held.", order.Id);
        return await BuildPaymentViewAsync(order, payment, cancellationToken);
    }

    public async Task<RefundResult> RefundOrderAsync(
        string buyerId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await LoadOwnedOrderAsync(request.OrderId, buyerId, cancellationToken);
        var payment = await EnsurePaymentAsync(order, cancellationToken);

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(
                409,
                "ORDER_NOT_CAPTURED",
                "Only captured orders can be refunded.",
                "Fulfil the order first so the payment is captured.");
        }

        if (string.IsNullOrWhiteSpace(payment.CapturedTransferId))
        {
            throw new PaymentOperationException(409, "CAPTURE_MISSING", "This order has no captured Finix transfer to refund.");
        }

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException(
                400,
                "IDEMPOTENCY_KEY_REQUIRED",
                "A non-empty idempotencyKey is required for refunds.");
        }

        var claimResult = await TryClaimAsync($"refund:{order.Id}", request.IdempotencyKey, cancellationToken);
        if (claimResult.IsDuplicate)
        {
            if (claimResult.Claim.Status == PaymentClaimStatus.Completed)
            {
                var existing = await _db.PaymentRefunds
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        r => r.OrderId == order.Id && r.IdempotencyKey == request.IdempotencyKey,
                        cancellationToken);
                if (existing is not null)
                {
                    payment = await LoadPaymentAsync(order.Id, cancellationToken) ?? payment;
                    return BuildRefundResult(order, payment, existing);
                }
            }

            var settled = await TrySettleRefundAsync(claimResult.Claim, order, payment, request, cancellationToken);
            if (settled is not null)
            {
                return settled;
            }

            throw new PaymentOperationException(
                409,
                "REFUND_IN_PROGRESS",
                "A refund with this idempotency key is already being processed.",
                "Wait for the in-flight refund to settle before retrying.");
        }

        try
        {
            var transfer = await _gateway.GetTransferAsync(payment.CapturedTransferId, cancellationToken);
            if (string.Equals(transfer.State, "PENDING", StringComparison.Ordinal))
            {
                transfer = await WaitForTransferSettlementAsync(
                    payment.CapturedTransferId,
                    attempts: 5,
                    cancellationToken);
            }

            var capturedCents = transfer.Amount ?? payment.CapturedAmountCents ?? 0;
            var reversals = await _gateway.ListTransferReversalsAsync(payment.CapturedTransferId, cancellationToken);
            if (!reversals.Complete)
            {
                throw new PaymentOperationException(
                    409,
                    "REFUND_LEDGER_INCOMPLETE",
                    "Prior refunds could not be read completely, so a refund beyond the captured amount cannot be ruled out.",
                    "Retry later or reconcile the captured transfer with Finix before refunding.");
            }

            var alreadyRefunded = reversals.Items.Sum(r => r.Amount ?? 0);
            var remaining = capturedCents - alreadyRefunded;
            if (remaining <= 0)
            {
                throw new PaymentOperationException(
                    409,
                    "FULLY_REFUNDED",
                    "This captured payment has already been fully refunded.");
            }

            var amount = ResolveRefundAmount(request, remaining);
            if (amount > int.MaxValue)
            {
                throw new PaymentOperationException(400, "REFUND_TOO_LARGE", "A single refund must fit in a 32-bit cent amount.");
            }

            var isFullCaptureRefund = alreadyRefunded == 0 && amount == capturedCents;
            if (string.Equals(transfer.State, "PENDING", StringComparison.Ordinal))
            {
                if (!isFullCaptureRefund)
                {
                    throw new PaymentOperationException(
                        409,
                        "CAPTURE_NOT_SETTLED",
                        "The captured Finix transfer is still PENDING; partial refunds are only accepted after it succeeds.",
                        "Retry shortly, or refund the full captured amount while the transfer is pending.");
                }
            }
            else if (!string.Equals(transfer.State, "SUCCEEDED", StringComparison.Ordinal))
            {
                throw new PaymentOperationException(
                    409,
                    "CAPTURE_NOT_SETTLED",
                    $"The captured Finix transfer is in state '{transfer.State}', not 'SUCCEEDED'.",
                    "Wait for the capture to settle before refunding.");
            }

            var refund = await _gateway.RefundAsync(payment.CapturedTransferId, (int)amount, cancellationToken);
            if (string.IsNullOrWhiteSpace(refund.Id))
            {
                throw new FinixProviderException(
                    null,
                    "REFUND_MISSING_ID",
                    "Finix accepted the refund but returned no refund identifier.",
                    outcomeUnknown: true);
            }

            var paymentRefund = new PaymentRefund
            {
                OrderId = order.Id,
                FinixRefundId = refund.Id,
                AmountCents = amount,
                IdempotencyKey = request.IdempotencyKey,
                Status = MapRefundStatus(refund.State),
                FailureCode = refund.FailureCode,
                FailureMessage = refund.FailureMessage
            };
            _db.PaymentRefunds.Add(paymentRefund);

            payment.RefundedAmountCents = alreadyRefunded + amount;
            payment.Status = payment.RefundedAmountCents >= capturedCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await CompleteClaimAsync(
                claimResult.Claim,
                JsonSerializer.Serialize(new { refundId = refund.Id, amountCents = amount }),
                cancellationToken);

            _logger.LogInformation(
                "Refunded order {OrderId} with Finix reversal {RefundId}; amount={AmountCents} status={Status}.",
                order.Id,
                refund.Id,
                amount,
                paymentRefund.Status);

            return BuildRefundResult(order, payment, paymentRefund);
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                var settled = await TrySettleRefundAsync(claimResult.Claim, order, payment, request, cancellationToken);
                if (settled is not null)
                {
                    return settled;
                }

                await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                throw new PaymentOperationException(
                    502,
                    "REFUND_OUTCOME_UNKNOWN",
                    "The payment provider did not confirm the refund result.",
                    "Check reconciliation before retrying; the refund may already have been created.",
                    ex);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw MapProviderError(ex, "The refund could not be created.");
        }
        catch (PaymentOperationException)
        {
            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<PaymentView>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return Array.Empty<PaymentView>();
        }

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId))
            .ToDictionaryAsync(p => p.OrderId, cancellationToken);
        var refunds = await _db.PaymentRefunds
            .AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(order => BuildPaymentView(
                order,
                payments.TryGetValue(order.Id, out var payment) ? payment : null,
                refunds.Where(r => r.OrderId == order.Id).ToList()))
            .ToList();
    }

    public async Task<ReconciliationResult> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "INVALID_DATE_RANGE", "The 'from' date must be before or equal to the 'to' date.");
        }

        var transfers = await _gateway.ListTransfersAsync(from, to, cancellationToken);
        var authorizations = await _gateway.ListAuthorizationsAsync(from, to, cancellationToken);

        var payments = await _db.OrderPayments.AsNoTracking().ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.AsNoTracking().ToListAsync(cancellationToken);

        var capturedTransfers = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CapturedTransferId))
            .ToDictionary(p => p.CapturedTransferId!, p => p.OrderId);
        var authorizationsById = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p.OrderId);
        var refundsByTransfer = refunds
            .Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundId))
            .GroupBy(r => r.FinixRefundId)
            .ToDictionary(g => g.Key, g => g.First().OrderId);

        var finixTransfers = transfers.Items
            .Select(transfer =>
            {
                var matchedOrderId = capturedTransfers.TryGetValue(transfer.Id, out var orderId)
                    ? orderId
                    : refundsByTransfer.TryGetValue(transfer.Id, out var refundOrderId)
                        ? refundOrderId
                        : (int?)null;

                return new FinixTransactionView
                {
                    Id = transfer.Id,
                    AmountCents = transfer.Amount,
                    State = transfer.State,
                    Type = transfer.Type,
                    ParentTransferId = transfer.ParentTransferId,
                    CreatedAt = transfer.CreatedAt,
                    OrderId = matchedOrderId,
                    Matched = matchedOrderId.HasValue
                };
            })
            .ToList();

        var finixAuthorizations = authorizations.Items
            .Select(authorization =>
            {
                var matched = authorizationsById.TryGetValue(authorization.Id, out var orderId);
                return new FinixAuthorizationView
                {
                    Id = authorization.Id,
                    AmountCents = authorization.Amount,
                    State = authorization.State,
                    IsVoid = authorization.IsVoid,
                    VoidState = authorization.VoidState,
                    CreatedAt = authorization.CreatedAt,
                    OrderId = matched ? orderId : null,
                    Matched = matched
                };
            })
            .ToList();

        var finixTransferIds = transfers.Items.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var finixAuthorizationIds = authorizations.Items.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);

        var unmatchedAppPayments = payments
            .Where(p =>
                (!string.IsNullOrWhiteSpace(p.CapturedTransferId) && !finixTransferIds.Contains(p.CapturedTransferId!))
                || (!string.IsNullOrWhiteSpace(p.AuthorizationId) && !finixAuthorizationIds.Contains(p.AuthorizationId!)))
            .Select(p => new UnmatchedAppPaymentView
            {
                OrderId = p.OrderId,
                BuyerId = p.BuyerId,
                AuthorizationId = p.AuthorizationId,
                CapturedTransferId = p.CapturedTransferId,
                UpdatedAt = p.UpdatedAt
            })
            .ToList();

        var unmatchedAppRefunds = refunds
            .Where(r => !finixTransferIds.Contains(r.FinixRefundId))
            .Select(r => new UnmatchedAppRefundView
            {
                OrderId = r.OrderId,
                RefundId = r.FinixRefundId,
                AmountCents = r.AmountCents,
                CreatedAt = r.CreatedAt
            })
            .ToList();

        var unmatchedFinixTransfers = finixTransfers.Where(t => !t.Matched).ToList();
        var unmatchedFinixAuthorizations = finixAuthorizations.Where(a => !a.Matched).ToList();

        return new ReconciliationResult
        {
            From = from,
            To = to,
            Truncated = !transfers.Complete || !authorizations.Complete,
            TransfersComplete = transfers.Complete,
            AuthorizationsComplete = authorizations.Complete,
            TransferPagesWalked = transfers.PagesWalked,
            AuthorizationPagesWalked = authorizations.PagesWalked,
            FinixTransferCount = finixTransfers.Count,
            FinixAuthorizationCount = finixAuthorizations.Count,
            UnmatchedFinixTransferCount = unmatchedFinixTransfers.Count,
            UnmatchedFinixAuthorizationCount = unmatchedFinixAuthorizations.Count,
            UnmatchedAppPaymentCount = unmatchedAppPayments.Count,
            UnmatchedAppRefundCount = unmatchedAppRefunds.Count,
            FinixTransfers = finixTransfers,
            FinixAuthorizations = finixAuthorizations,
            UnmatchedFinixTransfers = unmatchedFinixTransfers,
            UnmatchedFinixAuthorizations = unmatchedFinixAuthorizations,
            UnmatchedAppPayments = unmatchedAppPayments,
            UnmatchedAppRefunds = unmatchedAppRefunds
        };
    }

    public async Task<SavedPaymentMethodView> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        ValidateCard(request.Card);
        var identityId = await EnsureIdentityAsync(buyerId, cancellationToken);

        PaymentClaim? claim = null;
        if (!string.IsNullOrWhiteSpace(request.RequestId))
        {
            var claimResult = await TryClaimAsync($"payment-method:{buyerId}", request.RequestId!, cancellationToken);
            if (claimResult.IsDuplicate)
            {
                if (claimResult.Claim.Status == PaymentClaimStatus.Completed
                    && int.TryParse(claimResult.Claim.Reference, out var savedId))
                {
                    var existing = await _db.SavedPaymentMethods
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == savedId && m.BuyerId == buyerId, cancellationToken);
                    if (existing is not null)
                    {
                        return MapSavedPaymentMethod(existing);
                    }
                }

                var found = await TryFindInstrumentSafeAsync("app_request_key", request.RequestId!, cancellationToken);
                if (found is not null)
                {
                    var recovered = await StoreSavedCardAsync(buyerId, found, request.Alias, cancellationToken);
                    claimResult.Claim.Reference = recovered.Id.ToString();
                    await CompleteClaimAsync(claimResult.Claim, recovered.Id.ToString(), cancellationToken);
                    return MapSavedPaymentMethod(recovered);
                }

                throw new PaymentOperationException(
                    409,
                    "PAYMENT_METHOD_IN_PROGRESS",
                    "This card is already being saved.",
                    "Wait for the in-flight save to settle before retrying.");
            }

            claim = claimResult.Claim;
        }

        try
        {
            var instrument = await _gateway.CreateCardInstrumentAsync(
                identityId,
                ToCardInput(request.Card),
                request.RequestId,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(instrument.Fingerprint))
            {
                var duplicate = await _db.SavedPaymentMethods
                    .FirstOrDefaultAsync(
                        m => m.BuyerId == buyerId && m.Fingerprint == instrument.Fingerprint && m.IsActive,
                        cancellationToken);
                if (duplicate is not null)
                {
                    try
                    {
                        await _gateway.DisableInstrumentAsync(instrument.Id, cancellationToken);
                    }
                    catch (FinixProviderException)
                    {
                        // The duplicate Finix instrument is harmless; the shopper keeps using the original.
                    }

                    if (claim is not null)
                    {
                        claim.Reference = duplicate.Id.ToString();
                        await CompleteClaimAsync(claim, duplicate.Id.ToString(), cancellationToken);
                    }

                    return MapSavedPaymentMethod(duplicate);
                }
            }

            var saved = await StoreSavedCardAsync(buyerId, instrument, request.Alias, cancellationToken);
            if (claim is not null)
            {
                claim.Reference = saved.Id.ToString();
                await CompleteClaimAsync(claim, saved.Id.ToString(), cancellationToken);
            }

            _logger.LogInformation(
                "Saved Finix payment instrument {PaymentInstrumentId} for buyer {BuyerId}.",
                instrument.Id,
                buyerId);

            return MapSavedPaymentMethod(saved);
        }
        catch (FinixProviderException ex)
        {
            if (claim is not null)
            {
                if (ex.OutcomeUnknown)
                {
                    var found = await TryFindInstrumentSafeAsync("app_request_key", request.RequestId, cancellationToken);
                    if (found is not null)
                    {
                        var recovered = await StoreSavedCardAsync(buyerId, found, request.Alias, cancellationToken);
                        claim.Reference = recovered.Id.ToString();
                        await CompleteClaimAsync(claim, recovered.Id.ToString(), cancellationToken);
                        return MapSavedPaymentMethod(recovered);
                    }

                    await MarkClaimUnknownAsync(claim, ex.Message, cancellationToken);
                    throw new PaymentOperationException(
                        502,
                        "PAYMENT_METHOD_OUTCOME_UNKNOWN",
                        "The payment provider did not confirm whether the card was saved.",
                        "Check reconciliation or list the shopper's saved cards before retrying.",
                        ex);
                }

                await ReleaseClaimAsync(claim, cancellationToken);
            }

            throw MapProviderError(ex, "The card could not be saved.");
        }
    }

    public async Task<IReadOnlyList<SavedPaymentMethodView>> ListPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(m => m.BuyerId == buyerId && m.IsActive)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        return methods.Select(MapSavedPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var saved = await _db.SavedPaymentMethods
            .FirstOrDefaultAsync(m => m.Id == paymentMethodId && m.BuyerId == buyerId, cancellationToken);

        if (saved is null)
        {
            throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND", "Saved card not found.");
        }

        if (!saved.IsActive)
        {
            return;
        }

        var claimResult = await TryClaimAsync($"disable:{buyerId}", paymentMethodId.ToString(), cancellationToken);
        if (claimResult.IsDuplicate)
        {
            if (claimResult.Claim.Status == PaymentClaimStatus.Completed)
            {
                return;
            }

            var reread = await TryGetInstrumentSafeAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            if (reread is not null && reread.Enabled == false)
            {
                saved.IsActive = false;
                saved.DisabledAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                await CompleteClaimAsync(claimResult.Claim, "disabled", cancellationToken);
                return;
            }

            throw new PaymentOperationException(
                409,
                "PAYMENT_METHOD_DISABLE_IN_PROGRESS",
                "This saved card is already being removed.",
                "Wait for the in-flight removal to settle before retrying.");
        }

        try
        {
            await _gateway.DisableInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            saved.IsActive = false;
            saved.DisabledAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await CompleteClaimAsync(claimResult.Claim, "disabled", cancellationToken);
            _logger.LogInformation(
                "Disabled saved Finix payment instrument {PaymentInstrumentId} for buyer {BuyerId}.",
                saved.FinixPaymentInstrumentId,
                buyerId);
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                var reread = await TryGetInstrumentSafeAsync(saved.FinixPaymentInstrumentId, cancellationToken);
                if (reread is not null && reread.Enabled == false)
                {
                    saved.IsActive = false;
                    saved.DisabledAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    await CompleteClaimAsync(claimResult.Claim, "disabled", cancellationToken);
                    return;
                }

                await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                throw new PaymentOperationException(
                    502,
                    "PAYMENT_METHOD_DISABLE_OUTCOME_UNKNOWN",
                    "The payment provider did not confirm whether the saved card was removed.",
                    "Check the shopper's saved cards before retrying.",
                    ex);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw MapProviderError(ex, "The saved card could not be removed.");
        }
    }

    private async Task<string> EnsureIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.FinixBuyerIdentities
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var claimResult = await TryClaimAsync($"identity:{buyerId}", "default", cancellationToken);
        if (claimResult.IsDuplicate)
        {
            if (claimResult.Claim.Status == PaymentClaimStatus.Completed
                && !string.IsNullOrWhiteSpace(claimResult.Claim.Reference))
            {
                return claimResult.Claim.Reference!;
            }

            var found = await TryFindIdentitySafeAsync(buyerId, cancellationToken);
            if (found is not null)
            {
                await StoreIdentityAsync(buyerId, found.Id, cancellationToken);
                claimResult.Claim.Reference = found.Id;
                await CompleteClaimAsync(claimResult.Claim, found.Id, cancellationToken);
                return found.Id;
            }

            throw new PaymentOperationException(
                409,
                "IDENTITY_IN_PROGRESS",
                "A Finix identity for this shopper is already being created.",
                "Wait for the in-flight request to settle before retrying.");
        }

        try
        {
            var identity = await _gateway.CreateIdentityAsync(buyerId, cancellationToken);
            await StoreIdentityAsync(buyerId, identity.Id, cancellationToken);
            claimResult.Claim.Reference = identity.Id;
            await CompleteClaimAsync(claimResult.Claim, identity.Id, cancellationToken);
            return identity.Id;
        }
        catch (FinixProviderException ex)
        {
            if (ex.OutcomeUnknown)
            {
                var found = await TryFindIdentitySafeAsync(buyerId, cancellationToken);
                if (found is not null)
                {
                    await StoreIdentityAsync(buyerId, found.Id, cancellationToken);
                    claimResult.Claim.Reference = found.Id;
                    await CompleteClaimAsync(claimResult.Claim, found.Id, cancellationToken);
                    return found.Id;
                }

                await MarkClaimUnknownAsync(claimResult.Claim, ex.Message, cancellationToken);
                throw new PaymentOperationException(
                    502,
                    "IDENTITY_OUTCOME_UNKNOWN",
                    "The payment provider did not confirm whether the shopper identity was created.",
                    "Check reconciliation before retrying.",
                    ex);
            }

            await ReleaseClaimAsync(claimResult.Claim, cancellationToken);
            throw MapProviderError(ex, "A Finix identity could not be created for this shopper.");
        }
    }

    private async Task<string> ResolvePaymentSourceAsync(
        string buyerId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.PaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.Id == request.PaymentMethodId.Value && m.BuyerId == buyerId && m.IsActive,
                    cancellationToken);

            if (saved is null)
            {
                throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND", "Saved card not found.");
            }

            var instrument = await _gateway.GetInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            if (instrument is null || instrument.Enabled != true)
            {
                throw new PaymentOperationException(
                    409,
                    "PAYMENT_METHOD_DISABLED",
                    "That saved card is no longer usable.",
                    "Choose another saved card or provide card details.");
            }

            return saved.FinixPaymentInstrumentId;
        }

        if (request.Card is not null)
        {
            ValidateCard(request.Card);
            var identityId = await EnsureIdentityAsync(buyerId, cancellationToken);
            var instrument = await _gateway.CreateCardInstrumentAsync(
                identityId,
                ToCardInput(request.Card),
                null,
                cancellationToken);
            return instrument.Id;
        }

        throw new PaymentOperationException(
            400,
            "PAYMENT_SOURCE_REQUIRED",
            "Provide card details or a saved paymentMethodId.");
    }

    private async Task<bool> FinalizeCaptureAsync(
        Order order,
        OrderPayment payment,
        FinixAuthorization capture,
        string transferId,
        PaymentClaim claim,
        CancellationToken cancellationToken)
    {
        var transfer = await WaitForTransferSettlementAsync(transferId, attempts: 10, cancellationToken);

        if (transfer.State is "FAILED" or "CANCELED" or "RETURNED")
        {
            payment.Status = PaymentStatus.Failed;
            payment.FulfilmentStatus = FulfilmentStatus.NotFulfilled;
            payment.CapturedTransferId = transferId;
            payment.CapturedTransferState = transfer.State;
            payment.FailureCode = transfer.FailureCode ?? "CAPTURE_FAILED";
            payment.FailureMessage = transfer.FailureMessage
                ?? $"Finix capture transfer ended in state {transfer.State}.";
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await ReleaseClaimAsync(claim, cancellationToken);

            throw new PaymentOperationException(
                502,
                "CAPTURE_FAILED",
                payment.FailureMessage,
                "The capture did not settle. Reconcile the transfer with Finix before retrying or asking the shopper to pay again.");
        }

        var fees = await _gateway.GetFeesAsync(transferId, cancellationToken);

        var capturedCents = transfer.Amount ?? capture.Amount ?? ToCents(order.Total());
        var feeCents = fees.FeeCount > 0 ? fees.FeeCents : (transfer.Fee ?? 0);

        payment.Status = PaymentStatus.Captured;
        payment.FulfilmentStatus = FulfilmentStatus.Fulfilled;
        payment.CapturedTransferId = transferId;
        payment.CapturedTransferState = transfer.State;
        payment.CapturedAmountCents = capturedCents;
        payment.FeeCents = feeCents;
        payment.NetCents = capturedCents - feeCents;
        payment.AuthorizationState = capture.State ?? payment.AuthorizationState;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await CompleteClaimAsync(
            claim,
            JsonSerializer.Serialize(new
            {
                capturedTransferId = transferId,
                capturedTransferState = transfer.State,
                capturedAmountCents = capturedCents,
                feeCents,
                netCents = payment.NetCents,
                feesComplete = fees.Complete
            }),
            cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId} with Finix transfer {TransferId} in state {TransferState}; captured={CapturedCents} fee={FeeCents} net={NetCents} feesComplete={FeesComplete}.",
            order.Id,
            transferId,
            transfer.State,
            capturedCents,
            feeCents,
            payment.NetCents,
            fees.Complete);

        return fees.Complete;
    }

    private async Task<FinixTransfer> WaitForTransferSettlementAsync(
        string transferId,
        int attempts,
        CancellationToken cancellationToken)
    {
        var transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
        for (var attempt = 0;
             attempt < attempts && string.Equals(transfer.State, "PENDING", StringComparison.Ordinal);
             attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<FinixAuthorization?> TrySettleAuthorizationAsync(
        PaymentClaim claim,
        Order order,
        OrderPayment payment,
        long totalCents,
        string source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(claim.Reference))
        {
            return null;
        }

        try
        {
            var authorization = await _gateway.FindAuthorizationByIdempotencyAsync(claim.Reference, cancellationToken);
            if (authorization is null)
            {
                return null;
            }

            await ApplyAuthorizationAsync(order, payment, authorization, totalCents, source, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return authorization;
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<RefundResult?> TrySettleRefundAsync(
        PaymentClaim claim,
        Order order,
        OrderPayment payment,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.CapturedTransferId))
        {
            return null;
        }

        try
        {
            var transfer = await _gateway.GetTransferAsync(payment.CapturedTransferId, cancellationToken);
            var reversals = await _gateway.ListTransferReversalsAsync(payment.CapturedTransferId, cancellationToken);
            if (!reversals.Complete)
            {
                return null;
            }

            var capturedCents = transfer.Amount ?? payment.CapturedAmountCents ?? 0;
            var alreadyRefunded = reversals.Items.Sum(r => r.Amount ?? 0);
            var remaining = capturedCents - alreadyRefunded;
            if (remaining <= 0)
            {
                return null;
            }

            var amount = ResolveRefundAmount(request, remaining);
            var existingRefundIds = await _db.PaymentRefunds
                .Where(r => r.OrderId == order.Id)
                .Select(r => r.FinixRefundId)
                .ToListAsync(cancellationToken);

            var match = reversals.Items
                .Where(r => !existingRefundIds.Contains(r.Id) && (r.Amount ?? amount) == amount)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            if (match is null)
            {
                return null;
            }

            var paymentRefund = new PaymentRefund
            {
                OrderId = order.Id,
                FinixRefundId = match.Id,
                AmountCents = match.Amount ?? amount,
                IdempotencyKey = request.IdempotencyKey,
                Status = MapRefundStatus(match.State),
                FailureCode = match.FailureCode,
                FailureMessage = match.FailureMessage
            };
            _db.PaymentRefunds.Add(paymentRefund);

            payment.RefundedAmountCents = alreadyRefunded;
            payment.Status = alreadyRefunded >= capturedCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            await CompleteClaimAsync(
                claim,
                JsonSerializer.Serialize(new { refundId = match.Id, amountCents = paymentRefund.AmountCents }),
                cancellationToken);

            return BuildRefundResult(order, payment, paymentRefund);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task ApplyAuthorizationAsync(
        Order order,
        OrderPayment payment,
        FinixAuthorization authorization,
        long totalCents,
        string source,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(authorization.Id))
        {
            throw new FinixProviderException(
                null,
                "FINIX_AUTH_MISSING_ID",
                "Finix returned an authorization without an identifier.",
                outcomeUnknown: true);
        }

        payment.AuthorizationId = authorization.Id;
        payment.AuthorizationState = authorization.State;
        payment.AuthorizedAmountCents = authorization.Amount ?? totalCents;
        payment.PaymentInstrumentId = source;
        payment.UpdatedAt = DateTimeOffset.UtcNow;

        if (authorization.Amount.HasValue && authorization.Amount.Value != totalCents)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = "AUTHORIZATION_AMOUNT_MISMATCH";
            payment.FailureMessage =
                $"Finix authorized {authorization.Amount.Value} cents but the order total is {totalCents} cents.";

            try
            {
                await _gateway.VoidAsync(authorization.Id, cancellationToken);
            }
            catch (FinixProviderException)
            {
                // Best effort: release the mismatched hold; the operator still sees the mismatch.
            }

            return;
        }

        switch (authorization.State)
        {
            case "SUCCEEDED":
            case "PENDING":
                payment.Status = PaymentStatus.Authorized;
                payment.FailureCode = null;
                payment.FailureMessage = null;
                break;
            case "FAILED":
            case "CANCELED":
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = authorization.FailureCode ?? authorization.State;
                payment.FailureMessage = authorization.FailureMessage
                    ?? $"Finix authorization ended in state {authorization.State}.";
                break;
            default:
                payment.Status = PaymentStatus.Failed;
                payment.FailureCode = "AUTHORIZATION_UNKNOWN_STATE";
                payment.FailureMessage = $"Finix returned authorization state '{authorization.State}'.";
                break;
        }
    }

    private static void ApplyVoid(OrderPayment payment, FinixAuthorization authorization)
    {
        payment.Status = PaymentStatus.Cancelled;
        payment.FulfilmentStatus = FulfilmentStatus.Cancelled;
        payment.AuthorizationState = authorization.State ?? payment.AuthorizationState;
        payment.VoidState = authorization.VoidState;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static bool IsVoided(FinixAuthorization authorization) =>
        authorization.IsVoid == true
        || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.Ordinal)
        || string.Equals(authorization.State, "CANCELED", StringComparison.Ordinal);

    private async Task<FinixAuthorization> GetAuthorizationForOperatorAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixProviderException ex)
        {
            throw new PaymentOperationException(
                ex.StatusCode == 404 ? 409 : 502,
                "AUTHORIZATION_LOOKUP_FAILED",
                "The Finix authorization could not be read, so fulfilment cannot proceed.",
                "Check reconciliation and the authorization id with Finix before retrying.",
                ex);
        }
    }

    private async Task<FinixAuthorization?> TryGetAuthorizationSafeAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<FinixInstrument?> TryGetInstrumentSafeAsync(
        string paymentInstrumentId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetInstrumentAsync(paymentInstrumentId, cancellationToken);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<FinixIdentity?> TryFindIdentitySafeAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.FindIdentityByTagAsync("app_buyer_id", buyerId, cancellationToken);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task<FinixInstrument?> TryFindInstrumentSafeAsync(
        string key,
        string? value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return await _gateway.FindInstrumentByTagAsync(key, value, cancellationToken);
        }
        catch (FinixProviderException)
        {
            return null;
        }
    }

    private async Task StoreIdentityAsync(
        string buyerId,
        string finixIdentityId,
        CancellationToken cancellationToken)
    {
        var existing = await _db.FinixBuyerIdentities
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var identity = new FinixBuyerIdentity
        {
            BuyerId = buyerId,
            FinixIdentityId = finixIdentityId
        };
        _db.FinixBuyerIdentities.Add(identity);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            _db.Entry(identity).State = EntityState.Detached;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(identity).State = EntityState.Detached;
        }
    }

    private async Task<SavedPaymentMethod> StoreSavedCardAsync(
        string buyerId,
        FinixInstrument instrument,
        string? alias,
        CancellationToken cancellationToken)
    {
        var saved = new SavedPaymentMethod
        {
            BuyerId = buyerId,
            FinixPaymentInstrumentId = instrument.Id,
            Brand = instrument.Brand,
            LastFour = instrument.LastFour,
            ExpirationMonth = instrument.ExpirationMonth,
            ExpirationYear = instrument.ExpirationYear,
            Fingerprint = instrument.Fingerprint,
            Alias = alias
        };
        _db.SavedPaymentMethods.Add(saved);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return saved;
        }
        catch (DbUpdateException)
        {
            _db.Entry(saved).State = EntityState.Detached;
            var existing = await _db.SavedPaymentMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.BuyerId == buyerId && m.FinixPaymentInstrumentId == instrument.Id,
                    cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(saved).State = EntityState.Detached;
            var existing = await _db.SavedPaymentMethods
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    m => m.BuyerId == buyerId && m.FinixPaymentInstrumentId == instrument.Id,
                    cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<ClaimResult> TryClaimAsync(
        string scope,
        string key,
        CancellationToken cancellationToken)
    {
        var existing = await _db.PaymentClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Scope == scope && c.Key == key, cancellationToken);
        if (existing is not null)
        {
            return new ClaimResult(existing, true);
        }

        var claim = new PaymentClaim
        {
            Scope = scope,
            Key = key,
            Status = PaymentClaimStatus.Pending
        };

        _db.PaymentClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return new ClaimResult(claim, false);
        }
        catch (DbUpdateException)
        {
            _db.Entry(claim).State = EntityState.Detached;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(claim).State = EntityState.Detached;
        }

        existing = await _db.PaymentClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Scope == scope && c.Key == key, cancellationToken);

        if (existing is null)
        {
            throw new PaymentOperationException(
                409,
                "CLAIM_CONFLICT",
                "A concurrent request is already processing this operation.",
                "Retry after the in-flight request settles.");
        }

        return new ClaimResult(existing, true);
    }

    private async Task SaveClaimAsync(PaymentClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.PaymentClaims.Update(claim);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task CompleteClaimAsync(
        PaymentClaim claim,
        string resultJson,
        CancellationToken cancellationToken)
    {
        claim.Status = PaymentClaimStatus.Completed;
        claim.ResultJson = resultJson;
        claim.CompletedAt = DateTimeOffset.UtcNow;
        claim.FailureMessage = null;

        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.PaymentClaims.Update(claim);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkClaimUnknownAsync(
        PaymentClaim claim,
        string message,
        CancellationToken cancellationToken)
    {
        claim.Status = PaymentClaimStatus.Unknown;
        claim.FailureMessage = message;

        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.PaymentClaims.Update(claim);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task ReleaseClaimAsync(PaymentClaim claim, CancellationToken cancellationToken)
    {
        if (_db.Entry(claim).State == EntityState.Detached)
        {
            _db.PaymentClaims.Attach(claim);
        }

        _db.PaymentClaims.Remove(claim);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private async Task<Order> LoadOwnedOrderAsync(
        int orderId,
        string buyerId,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, "ORDER_NOT_FOUND", "Order not found.");
        }

        return order;
    }

    private async Task<OrderPayment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderPayments.FindAsync(new object[] { orderId }, cancellationToken);

    private async Task<OrderPayment> EnsurePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(order.Id, cancellationToken);
        if (payment is not null)
        {
            return payment;
        }

        payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            Currency = _options.Value.Currency,
            Status = PaymentStatus.AwaitingPayment,
            FulfilmentStatus = FulfilmentStatus.NotFulfilled
        };
        _db.OrderPayments.Add(payment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return payment;
        }
        catch (DbUpdateException)
        {
            _db.Entry(payment).State = EntityState.Detached;
            var existing = await LoadPaymentAsync(order.Id, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
        catch (InvalidOperationException)
        {
            _db.Entry(payment).State = EntityState.Detached;
            var existing = await LoadPaymentAsync(order.Id, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            throw;
        }
    }

    private async Task<PaymentView> BuildPaymentViewAsync(
        Order order,
        OrderPayment payment,
        CancellationToken cancellationToken,
        bool? feesComplete = null)
    {
        var refunds = await _db.PaymentRefunds
            .AsNoTracking()
            .Where(r => r.OrderId == order.Id)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return BuildPaymentView(order, payment, refunds, feesComplete);
    }

    private PaymentView BuildPaymentView(
        Order order,
        OrderPayment? payment,
        IReadOnlyList<PaymentRefund> refunds,
        bool? feesComplete = null)
    {
        var totalCents = ToCents(order.Total());
        var capturedCents = payment?.CapturedAmountCents ?? 0;
        var refundedCents = payment?.RefundedAmountCents ?? refunds.Sum(r => r.AmountCents);

        return new PaymentView
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            TotalCents = totalCents,
            Currency = payment?.Currency ?? _options.Value.Currency,
            PaymentStatus = (payment?.Status ?? PaymentStatus.AwaitingPayment).ToString(),
            FulfilmentStatus = (payment?.FulfilmentStatus ?? FulfilmentStatus.NotFulfilled).ToString(),
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationState = payment?.AuthorizationState,
            AuthorizedAmountCents = payment?.AuthorizedAmountCents,
            VoidState = payment?.VoidState,
            CapturedTransferId = payment?.CapturedTransferId,
            CapturedTransferState = payment?.CapturedTransferState,
            CapturedAmountCents = payment?.CapturedAmountCents,
            FeeCents = payment?.FeeCents,
            NetCents = payment?.NetCents,
            FeesComplete = feesComplete,
            RefundedAmountCents = refundedCents,
            RefundableAmountCents = Math.Max(0, capturedCents - refundedCents),
            PaymentInstrumentId = payment?.PaymentInstrumentId,
            FailureCode = payment?.FailureCode,
            FailureMessage = payment?.FailureMessage,
            Items = order.OrderItems.Select(MapLine).ToList(),
            Refunds = refunds.Select(r => new RefundView
            {
                RefundId = r.FinixRefundId,
                AmountCents = r.AmountCents,
                Status = r.Status.ToString(),
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    private static RefundResult BuildRefundResult(
        Order order,
        OrderPayment payment,
        PaymentRefund refund) =>
        new()
        {
            OrderId = order.Id,
            RefundId = refund.FinixRefundId,
            AmountCents = refund.AmountCents,
            Status = refund.Status.ToString(),
            RefundedAmountCents = payment.RefundedAmountCents,
            RefundableAmountCents = Math.Max(0, (payment.CapturedAmountCents ?? 0) - payment.RefundedAmountCents),
            PaymentStatus = payment.Status.ToString(),
            Currency = payment.Currency,
            IdempotencyKey = refund.IdempotencyKey
        };

    private static OrderLineView MapLine(OrderItem item) =>
        new()
        {
            CatalogItemId = item.ItemOrdered.CatalogItemId,
            ProductName = item.ItemOrdered.ProductName,
            UnitPrice = item.UnitPrice,
            Units = item.Units,
            LineTotal = item.UnitPrice * item.Units
        };

    private static SavedPaymentMethodView MapSavedPaymentMethod(SavedPaymentMethod method) =>
        new()
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.LastFour,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear,
            Alias = method.Alias,
            FinixPaymentInstrumentId = method.FinixPaymentInstrumentId,
            IsActive = method.IsActive,
            CreatedAt = method.CreatedAt
        };

    private static long ResolveRefundAmount(RefundOrderRequest request, long remaining)
    {
        if (request.AmountInCents.HasValue && request.Amount.HasValue)
        {
            throw new PaymentOperationException(
                400,
                "REFUND_AMOUNT_AMBIGUOUS",
                "Provide either amountInCents or amount, not both.");
        }

        long amount;
        if (request.AmountInCents.HasValue)
        {
            amount = request.AmountInCents.Value;
        }
        else if (request.Amount.HasValue)
        {
            amount = ToCents(request.Amount.Value);
        }
        else
        {
            amount = remaining;
        }

        if (amount <= 0)
        {
            throw new PaymentOperationException(400, "INVALID_REFUND_AMOUNT", "The refund amount must be greater than zero.");
        }

        if (amount > remaining)
        {
            throw new PaymentOperationException(
                409,
                "REFUND_EXCEEDS_CAPTURE",
                $"The requested refund of {amount} cents exceeds the {remaining} cents still refundable from this capture.",
                "Refund at most the remaining captured amount.");
        }

        return amount;
    }

    private static RefundStatus MapRefundStatus(string? state) =>
        state switch
        {
            "SUCCEEDED" => RefundStatus.Succeeded,
            "FAILED" or "CANCELED" => RefundStatus.Failed,
            _ => RefundStatus.Pending
        };

    private static void ValidateCard(CardDetails? card)
    {
        if (card is null)
        {
            throw new PaymentOperationException(400, "CARD_REQUIRED", "Card details are required.");
        }

        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(400, "INVALID_CARD_NUMBER", "The card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "INVALID_CARD_EXPIRY", "The expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear is < 2000 or > 9999)
        {
            throw new PaymentOperationException(400, "INVALID_CARD_EXPIRY", "The expiration year must be a four-digit year.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode))
        {
            throw new PaymentOperationException(400, "INVALID_CARD_SECURITY_CODE", "The card security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "INVALID_CARD_NAME", "The cardholder name is required.");
        }
    }

    private static CardInput ToCardInput(CardDetails card) =>
        new(
            new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray()),
            card.ExpirationMonth,
            card.ExpirationYear,
            card.SecurityCode,
            card.Name);

    private static long ToCents(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static PaymentOperationException MapProviderError(
        FinixProviderException exception,
        string fallbackMessage)
    {
        var status = exception.StatusCode;
        return status switch
        {
            400 or 404 or 409 or 422 => new PaymentOperationException(
                status.Value,
                exception.Code ?? "FINIX_REJECTED",
                exception.Message,
                "Correct the request and retry.",
                exception),
            401 or 403 => new PaymentOperationException(
                502,
                "FINIX_AUTHENTICATION_FAILED",
                "The payment provider rejected this application's credentials.",
                "Contact the payment operations team.",
                exception),
            402 => new PaymentOperationException(
                402,
                exception.Code ?? "PAYMENT_DECLINED",
                exception.Message,
                "Ask the shopper to use another card.",
                exception),
            429 => new PaymentOperationException(
                503,
                "FINIX_RATE_LIMITED",
                "The payment provider is rate limiting requests.",
                "Retry after a short delay.",
                exception),
            _ => new PaymentOperationException(
                502,
                exception.Code ?? "FINIX_UNAVAILABLE",
                fallbackMessage,
                null,
                exception)
        };
    }

    private sealed record ClaimResult(PaymentClaim Claim, bool IsDuplicate);
}
