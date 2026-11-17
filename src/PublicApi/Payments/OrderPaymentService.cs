using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IOrderPaymentService
{
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken);
    Task<PayOrderResponse> PayOrderAsync(int orderId, PayOrderRequest request, CancellationToken cancellationToken);
    Task<FulfilOrderResponse> FulfilOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<CancelOrderResponse> CancelOrderAsync(int orderId, CancellationToken cancellationToken);
    Task<RefundOrderResponse> RefundOrderAsync(int orderId, RefundOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(CancellationToken cancellationToken);
    Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}

public sealed class OrderPaymentService : IOrderPaymentService
{
    private const string AuthorizationSucceeded = "SUCCEEDED";
    private const string AuthorizationCanceled = "CANCELED";
    private const string AuthorizationFailed = "FAILED";
    private const int MaxReconciliationPages = 100;

    private readonly CatalogContext _db;
    private readonly IFinixGateway _gateway;
    private readonly IShopperIdentityService _identityService;
    private readonly ICurrentUser _currentUser;
    private readonly PaymentClaimStore _claims;
    private readonly FinixOptions _finixOptions;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        CatalogContext db,
        IFinixGateway gateway,
        IShopperIdentityService identityService,
        ICurrentUser currentUser,
        PaymentClaimStore claims,
        FinixOptions finixOptions,
        ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _gateway = gateway;
        _identityService = identityService;
        _currentUser = currentUser;
        _claims = claims;
        _finixOptions = finixOptions;
        _logger = logger;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;
        var items = request.Items ?? new List<CreateOrderItemRequest>();

        if (items.Count == 0)
        {
            throw new PaymentApiException(400, "At least one catalog item is required.", "EMPTY_ORDER");
        }

        if (items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new PaymentApiException(
                400,
                "Every order item needs a positive catalogItemId and quantity.",
                "INVALID_ORDER_ITEM");
        }

        var grouped = items
            .GroupBy(item => item.CatalogItemId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity));

        var catalogIds = grouped.Keys.ToList();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        if (catalogItems.Count != catalogIds.Count)
        {
            var missing = catalogIds.Where(id => !catalogItems.ContainsKey(id));
            throw new PaymentApiException(
                400,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.",
                "UNKNOWN_CATALOG_ITEM");
        }

        var orderItems = grouped
            .OrderBy(pair => pair.Key)
            .Select(pair =>
            {
                var catalogItem = catalogItems[pair.Key];
                var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                    ? "placeholder.png"
                    : catalogItem.PictureUri;

                return new OrderItem(
                    new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri),
                    catalogItem.Price,
                    pair.Value);
            })
            .ToList();

        var order = new Order(shopperId, ToAddress(request.ShipTo), orderItems);
        order.SetCurrency(_finixOptions.Currency!);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created order {OrderId} for shopper {ShopperId} for {TotalCents} {Currency} cents.",
            order.Id,
            shopperId,
            order.TotalCents,
            order.Currency);

        return ToCreateOrderResponse(order);
    }

    public async Task<PayOrderResponse> PayOrderAsync(
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;
        var order = await FindShopperOrderAsync(orderId, shopperId, cancellationToken);
        ValidatePayRequest(request);

        var claimKey = $"PAY:{order.Id}";
        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            if (IsPaid(order.PaymentStatus))
            {
                return ToPayOrderResponse(order);
            }

            if (order.PaymentStatus == OrderPaymentStatus.PaymentFailed)
            {
                await _claims.RemoveAsync(claim, cancellationToken);
                claim = null;
            }
            else if (await TrySettleAuthorizationAsync(order, claim, cancellationToken))
            {
                return ToPayOrderResponse(order);
            }
            else
            {
                await _claims.RemoveAsync(claim, cancellationToken);
                claim = null;
            }
        }

        if (order.PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.PaymentFailed))
        {
            throw new PaymentApiException(
                409,
                $"Order {order.Id} cannot be paid while it is {order.PaymentStatus}.",
                "ORDER_NOT_PAYABLE");
        }

        claim = new PaymentOperationClaim(claimKey, "PAY");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            var racedClaim = await _claims.FindAsync(claimKey, cancellationToken);
            if (racedClaim is not null
                && await TrySettleAuthorizationAsync(order, racedClaim, cancellationToken))
            {
                return ToPayOrderResponse(order);
            }

            throw new PaymentApiException(
                409,
                "A payment for this order is already in progress; retry shortly.",
                "PAYMENT_IN_PROGRESS");
        }

        try
        {
            var paymentInstrumentId = await ResolvePaymentInstrumentAsync(order, shopperId, request, cancellationToken);
            var idempotencyId = $"PAY:{order.PaymentReference}";

            var authorization = await _gateway.AuthorizeAsync(
                paymentInstrumentId,
                order.TotalCents,
                idempotencyId,
                request.Card?.SecurityCode,
                cancellationToken);

            if (!IsAuthorizationSucceeded(authorization))
            {
                order.MarkPaymentFailed(
                    authorization.FailureCode ?? "AUTHORIZATION_FAILED",
                    authorization.FailureMessage ?? $"Finix returned authorization state {authorization.State ?? "UNKNOWN"}.");
                await _db.SaveChangesAsync(cancellationToken);
                await _claims.RemoveAsync(claim, cancellationToken);

                throw new PaymentApiException(
                    402,
                    order.PaymentFailureMessage!,
                    order.PaymentFailureCode);
            }

            if (authorization.Amount != order.TotalCents)
            {
                await TryReleaseMismatchedHoldAsync(authorization.Id, cancellationToken);
                order.MarkPaymentFailed(
                    "AMOUNT_MISMATCH",
                    $"Finix authorized {authorization.Amount} cents but the order total is {order.TotalCents} cents; the hold was released.");
                await _db.SaveChangesAsync(cancellationToken);
                await _claims.RemoveAsync(claim, cancellationToken);

                throw new PaymentApiException(502, order.PaymentFailureMessage!, "AMOUNT_MISMATCH");
            }

            order.MarkAuthorized(authorization.Id, paymentInstrumentId, authorization.Amount, authorization.State);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, authorization.Id, null, cancellationToken);

            _logger.LogInformation(
                "Authorized {AmountCents} cents for order {OrderId} as authorization {AuthorizationId}.",
                authorization.Amount,
                order.Id,
                authorization.Id);

            return ToPayOrderResponse(order);
        }
        catch (PaymentProviderException exception)
        {
            if (exception.OutcomeUnknown)
            {
                if (await TrySettleAuthorizationAsync(order, claim, cancellationToken))
                {
                    return ToPayOrderResponse(order);
                }

                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the authorization; the outcome is unknown. Repeating the payment will settle it.",
                    "AUTHORIZATION_UNKNOWN");
            }

            order.MarkPaymentFailed(exception.Code ?? "AUTHORIZATION_FAILED", exception.Message);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.RemoveAsync(claim, cancellationToken);

            throw new PaymentApiException(402, exception.Message, exception.Code ?? "AUTHORIZATION_FAILED");
        }
    }

    public async Task<FulfilOrderResponse> FulfilOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);

        if (IsCaptured(order.PaymentStatus))
        {
            return ToFulfilOrderResponse(order);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            throw new PaymentApiException(
                409,
                $"Order {order.Id} is {order.PaymentStatus}; only an authorized payment can be fulfilled.",
                "ORDER_NOT_AUTHORIZED");
        }

        var authorizationId = order.FinixAuthorizationId!;
        var claimKey = $"CAPTURE:{order.Id}";
        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            if (await TrySettleCaptureAsync(order, claim, cancellationToken))
            {
                return ToFulfilOrderResponse(order);
            }

            var authorization = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (IsAuthorizationUnavailable(authorization))
            {
                order.MarkCaptureUnavailable(
                    authorization.FailureCode ?? "AUTHORIZATION_NOT_CAPTURABLE",
                    BuildCaptureUnavailableMessage(authorization));
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentApiException(
                    409,
                    order.PaymentFailureMessage!,
                    order.PaymentFailureCode ?? "AUTHORIZATION_NOT_CAPTURABLE");
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            claim = null;
        }

        claim = new PaymentOperationClaim(claimKey, "CAPTURE");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            var racedClaim = await _claims.FindAsync(claimKey, cancellationToken);
            if (racedClaim is not null
                && await TrySettleCaptureAsync(order, racedClaim, cancellationToken))
            {
                return ToFulfilOrderResponse(order);
            }

            throw new PaymentApiException(
                409,
                "A capture for this order is already in progress; retry shortly.",
                "CAPTURE_IN_PROGRESS");
        }

        try
        {
            var capture = await _gateway.CaptureAsync(authorizationId, order.AuthorizedAmountCents, cancellationToken);
            var transferId = capture.TransferId;

            if (string.IsNullOrWhiteSpace(transferId))
            {
                var reRead = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
                transferId = reRead.TransferId;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                order.MarkCaptureUnavailable(
                    "CAPTURE_TRANSFER_MISSING",
                    "Finix accepted the capture but did not return a transfer id; re-read the authorization before retrying fulfilment.");
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentApiException(
                    409,
                    order.PaymentFailureMessage!,
                    "CAPTURE_TRANSFER_MISSING");
            }

            var transfer = await _gateway.GetTransferAsync(transferId!, cancellationToken);
            var capturedAmount = transfer is { Amount: > 0 }
                ? transfer.Amount
                : (capture.Amount > 0 ? capture.Amount : order.AuthorizedAmountCents);
            var fee = transfer?.Fee ?? 0;

            order.MarkCaptured(transferId!, capturedAmount, fee, capturedAmount - fee, transfer?.State ?? capture.State);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, transferId, null, cancellationToken);

            _logger.LogInformation(
                "Captured {CapturedAmountCents} cents for order {OrderId} as transfer {TransferId}.",
                capturedAmount,
                order.Id,
                transferId);

            return ToFulfilOrderResponse(order);
        }
        catch (PaymentProviderException exception)
        {
            if (await TrySettleCaptureAsync(order, claim, cancellationToken))
            {
                return ToFulfilOrderResponse(order);
            }

            if (exception.OutcomeUnknown)
            {
                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the capture; the outcome is unknown. Re-run fulfilment to settle it.",
                    "CAPTURE_UNKNOWN");
            }

            order.MarkCaptureUnavailable(
                exception.Code ?? "CAPTURE_FAILED",
                $"The card hold can no longer be captured: {exception.Message}. The order was not fulfilled; ask the shopper to pay again.");
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.RemoveAsync(claim, cancellationToken);

            throw new PaymentApiException(409, order.PaymentFailureMessage!, exception.Code ?? "CAPTURE_FAILED");
        }
    }

    public async Task<CancelOrderResponse> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await FindOrderAsync(orderId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return ToCancelOrderResponse(order);
        }

        if (IsCaptured(order.PaymentStatus))
        {
            throw new PaymentApiException(
                409,
                $"Order {order.Id} has already been captured; use the refunds endpoint instead of cancelling.",
                "ORDER_ALREADY_CAPTURED");
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            throw new PaymentApiException(
                409,
                $"Order {order.Id} is {order.PaymentStatus}; only an authorized payment can be cancelled.",
                "ORDER_NOT_CANCELLABLE");
        }

        var authorizationId = order.FinixAuthorizationId!;
        var claimKey = $"CANCEL:{order.Id}";
        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            if (await TrySettleVoidAsync(order, claim, cancellationToken))
            {
                return ToCancelOrderResponse(order);
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            claim = null;
        }

        claim = new PaymentOperationClaim(claimKey, "CANCEL");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            var racedClaim = await _claims.FindAsync(claimKey, cancellationToken);
            if (racedClaim is not null
                && await TrySettleVoidAsync(order, racedClaim, cancellationToken))
            {
                return ToCancelOrderResponse(order);
            }

            throw new PaymentApiException(
                409,
                "A cancellation for this order is already in progress; retry shortly.",
                "CANCEL_IN_PROGRESS");
        }

        try
        {
            await _gateway.VoidAsync(authorizationId, cancellationToken);
            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, authorizationId, null, cancellationToken);

            _logger.LogInformation(
                "Voided authorization {AuthorizationId} for order {OrderId}.",
                authorizationId,
                order.Id);

            return ToCancelOrderResponse(order);
        }
        catch (PaymentProviderException exception)
        {
            if (await TrySettleVoidAsync(order, claim, cancellationToken))
            {
                return ToCancelOrderResponse(order);
            }

            if (exception.OutcomeUnknown)
            {
                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the void; the outcome is unknown. Re-run cancellation to settle it.",
                    "CANCEL_UNKNOWN");
            }

            order.MarkCaptureUnavailable(
                exception.Code ?? "VOID_FAILED",
                $"The authorization could not be voided: {exception.Message}");
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.RemoveAsync(claim, cancellationToken);

            throw new PaymentApiException(409, order.PaymentFailureMessage!, exception.Code ?? "VOID_FAILED");
        }
    }

    public async Task<RefundOrderResponse> RefundOrderAsync(
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        var order = await FindShopperOrderAsync(orderId, _currentUser.UserId, cancellationToken);

        if (order.PaymentStatus is not (OrderPaymentStatus.Captured or OrderPaymentStatus.PartiallyRefunded))
        {
            throw new PaymentApiException(
                409,
                $"Order {order.Id} is {order.PaymentStatus}; only a captured payment can be refunded.",
                "ORDER_NOT_REFUNDABLE");
        }

        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentApiException(
                400,
                "IdempotencyKey is required so a repeated refund request cannot refund twice.",
                "MISSING_IDEMPOTENCY_KEY");
        }

        var claimKey = $"REFUND:{order.Id}:{idempotencyKey}";
        using var gate = await _claims.AcquireAsync(claimKey, cancellationToken);

        var claim = await _claims.FindAsync(claimKey, cancellationToken);
        if (claim is not null)
        {
            var existing = await _db.RefundRecords
                .FirstOrDefaultAsync(
                    record => record.OrderId == order.Id && record.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (existing is not null)
            {
                return ToRefundOrderResponse(order, existing);
            }

            var settled = await TrySettleRefundAsync(order, claim, idempotencyKey, request.AmountCents, cancellationToken);
            if (settled is not null)
            {
                return ToRefundOrderResponse(order, settled);
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            claim = null;
        }

        var refundable = order.CapturedAmountCents - order.RefundedAmountCents;
        var amount = request.AmountCents ?? refundable;

        if (amount <= 0)
        {
            throw new PaymentApiException(400, "The refund amount must be greater than zero.", "INVALID_REFUND_AMOUNT");
        }

        if (amount > refundable)
        {
            throw new PaymentApiException(
                409,
                $"Refund amount {amount} cents exceeds the refundable balance of {refundable} cents.",
                "REFUND_EXCEEDS_CAPTURE");
        }

        if (amount > int.MaxValue)
        {
            throw new PaymentApiException(
                400,
                "A single refund cannot exceed the Finix integer limit.",
                "REFUND_TOO_LARGE");
        }

        await EnsurePartialRefundAllowedAsync(order, amount, cancellationToken);

        claim = new PaymentOperationClaim(claimKey, "REFUND");
        if (!await _claims.TryAddAsync(claim, cancellationToken))
        {
            var raced = await _db.RefundRecords
                .FirstOrDefaultAsync(
                    record => record.OrderId == order.Id && record.IdempotencyKey == idempotencyKey,
                    cancellationToken);

            if (raced is not null)
            {
                return ToRefundOrderResponse(order, raced);
            }

            throw new PaymentApiException(
                409,
                "A refund for this key is already in progress; retry shortly.",
                "REFUND_IN_PROGRESS");
        }

        try
        {
            var refund = await _gateway.RefundAsync(order.FinixTransferId!, amount, idempotencyKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(refund.Id))
            {
                throw new PaymentApiException(
                    502,
                    "Finix created a refund without returning its id.",
                    "MISSING_REFUND_ID");
            }

            var record = new RefundRecord(order.Id, idempotencyKey, refund.Id, amount, refund.State);
            _db.RefundRecords.Add(record);
            order.MarkRefunded(amount);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, refund.Id, null, cancellationToken);

            _logger.LogInformation(
                "Refunded {AmountCents} cents for order {OrderId} as refund {RefundId}.",
                amount,
                order.Id,
                refund.Id);

            return ToRefundOrderResponse(order, record);
        }
        catch (PaymentProviderException exception)
        {
            if (exception.ReferenceId is { Length: > 0 } existingRefundId)
            {
                var existing = await _db.RefundRecords.FirstOrDefaultAsync(
                    record => record.FinixRefundTransferId == existingRefundId,
                    cancellationToken);

                if (existing is null)
                {
                    existing = new RefundRecord(order.Id, idempotencyKey, existingRefundId, amount, "PENDING");
                    _db.RefundRecords.Add(existing);
                    order.MarkRefunded(amount);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                await _claims.CompleteAsync(claim, existingRefundId, null, cancellationToken);
                return ToRefundOrderResponse(order, existing);
            }

            var settled = await TrySettleRefundAsync(order, claim, idempotencyKey, amount, cancellationToken);
            if (settled is not null)
            {
                return ToRefundOrderResponse(order, settled);
            }

            if (exception.StatusCode == 409
                && exception.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                await _claims.RemoveAsync(claim, cancellationToken);
                throw new PaymentApiException(
                    409,
                    "Finix is still processing the captured transfer; a partial refund can be issued once it has succeeded. Retry shortly, or refund the full captured amount now.",
                    "CAPTURE_STILL_PENDING");
            }

            if (exception.OutcomeUnknown)
            {
                throw new PaymentApiException(
                    502,
                    "Finix did not confirm the refund; the outcome is unknown. Repeating the same idempotency key will settle it.",
                    "REFUND_UNKNOWN");
            }

            await _claims.RemoveAsync(claim, cancellationToken);
            throw new PaymentApiException(409, exception.Message, exception.Code ?? "REFUND_FAILED");
        }
    }

    public async Task<IReadOnlyList<MyOrderResponse>> GetMyOrdersAsync(CancellationToken cancellationToken)
    {
        var shopperId = _currentUser.UserId;

        var orders = await _db.Orders
            .Include(order => order.OrderItems)
            .Where(order => order.BuyerId == shopperId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        return orders.Select(ToMyOrderResponse).ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentApiException(400, "'to' must be on or after 'from'.", "INVALID_DATE_RANGE");
        }

        var transfers = new List<FinixTransferInfo>();
        var authorizations = new List<FinixAuthorizationInfo>();
        var pagesWalked = 0;
        var truncated = false;
        string? nextCursor = null;

        string? cursor = null;
        while (pagesWalked < MaxReconciliationPages)
        {
            var page = await _gateway.ListTransfersPageAsync(from, to, cursor, cancellationToken);
            transfers.AddRange(page.Items);
            pagesWalked++;
            cursor = page.NextCursor;

            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }

            if (pagesWalked >= MaxReconciliationPages)
            {
                truncated = true;
                nextCursor = cursor;
            }
        }

        cursor = null;
        while (pagesWalked < MaxReconciliationPages)
        {
            var page = await _gateway.ListAuthorizationsPageAsync(from, to, cursor, cancellationToken);
            authorizations.AddRange(page.Items);
            pagesWalked++;
            cursor = page.NextCursor;

            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }

            if (pagesWalked >= MaxReconciliationPages)
            {
                truncated = true;
                nextCursor = cursor;
            }
        }

        var orders = await _db.Orders
            .Where(order => order.FinixAuthorizationId != null || order.FinixTransferId != null)
            .ToListAsync(cancellationToken);
        var refunds = await _db.RefundRecords.ToListAsync(cancellationToken);

        var authorizationIds = authorizations.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
        var transferIds = transfers.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var ordersByAuthorization = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            .GroupBy(order => order.FinixAuthorizationId!)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ordersByTransfer = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixTransferId))
            .GroupBy(order => order.FinixTransferId!)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var refundsByTransfer = refunds
            .GroupBy(record => record.FinixRefundTransferId)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ordersById = orders.ToDictionary(order => order.Id);

        var lines = new List<ReconciliationLine>();

        foreach (var authorization in authorizations)
        {
            var matched = ordersByAuthorization.TryGetValue(authorization.Id, out var order);
            lines.Add(new ReconciliationLine
            {
                Source = "finix",
                Kind = "authorization",
                FinixId = authorization.Id,
                OrderId = order?.Id,
                AmountCents = authorization.Amount,
                State = authorization.State,
                Matched = matched,
                Issue = matched ? null : "Finix authorization has no matching eShop order in the range."
            });
        }

        foreach (var transfer in transfers)
        {
            var isRefund = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                           || !string.IsNullOrWhiteSpace(transfer.ParentTransferId);

            if (isRefund)
            {
                var matched = refundsByTransfer.TryGetValue(transfer.Id, out var refund);
                ordersById.TryGetValue(refund?.OrderId ?? 0, out var refundOrder);

                lines.Add(new ReconciliationLine
                {
                    Source = "finix",
                    Kind = "refund",
                    FinixId = transfer.Id,
                    OrderId = refundOrder?.Id,
                    AmountCents = transfer.Amount,
                    State = transfer.State,
                    Matched = matched,
                    Issue = matched ? null : "Finix reversal has no matching eShop refund in the range."
                });
            }
            else
            {
                var matched = ordersByTransfer.TryGetValue(transfer.Id, out var order);
                lines.Add(new ReconciliationLine
                {
                    Source = "finix",
                    Kind = "capture",
                    FinixId = transfer.Id,
                    OrderId = order?.Id,
                    AmountCents = transfer.Amount,
                    State = transfer.State,
                    Matched = matched,
                    Issue = matched ? null : "Finix transfer has no matching eShop order in the range."
                });
            }
        }

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId)
                && !authorizationIds.Contains(order.FinixAuthorizationId!))
            {
                lines.Add(new ReconciliationLine
                {
                    Source = "eshop",
                    Kind = "authorization",
                    FinixId = order.FinixAuthorizationId,
                    OrderId = order.Id,
                    AmountCents = order.AuthorizedAmountCents,
                    State = order.PaymentStatus.ToString(),
                    Matched = false,
                    Issue = "eShop order has no matching Finix authorization in the range."
                });
            }

            if (!string.IsNullOrWhiteSpace(order.FinixTransferId)
                && !transferIds.Contains(order.FinixTransferId!))
            {
                lines.Add(new ReconciliationLine
                {
                    Source = "eshop",
                    Kind = "capture",
                    FinixId = order.FinixTransferId,
                    OrderId = order.Id,
                    AmountCents = order.CapturedAmountCents,
                    State = order.PaymentStatus.ToString(),
                    Matched = false,
                    Issue = "eShop order has no matching Finix capture transfer in the range."
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (!transferIds.Contains(refund.FinixRefundTransferId))
            {
                lines.Add(new ReconciliationLine
                {
                    Source = "eshop",
                    Kind = "refund",
                    FinixId = refund.FinixRefundTransferId,
                    OrderId = refund.OrderId,
                    AmountCents = refund.AmountCents,
                    State = "REFUNDED",
                    Matched = false,
                    Issue = "eShop refund has no matching Finix reversal in the range."
                });
            }
        }

        return new ReconciliationResponse
        {
            From = from.ToString("O"),
            To = to.ToString("O"),
            Lines = lines,
            FinixRecordCount = transfers.Count + authorizations.Count,
            EshopRecordCount = orders.Count + refunds.Count,
            PagesWalked = pagesWalked,
            Truncated = truncated,
            NextCursor = nextCursor
        };
    }

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(item => item.OrderItems)
            .FirstOrDefaultAsync(item => item.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new PaymentApiException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private async Task<Order> FindShopperOrderAsync(
        int orderId,
        string shopperId,
        CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(item => item.OrderItems)
            .FirstOrDefaultAsync(item => item.Id == orderId && item.BuyerId == shopperId, cancellationToken);

        if (order is null)
        {
            throw new PaymentApiException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private async Task<string> ResolvePaymentInstrumentAsync(
        Order order,
        string shopperId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.PaymentMethodId))
        {
            var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
                card => card.ShopperId == shopperId
                        && card.FinixPaymentInstrumentId == request.PaymentMethodId
                        && !card.IsDeleted,
                cancellationToken);

            if (savedCard is null)
            {
                throw new PaymentApiException(
                    404,
                    $"Saved payment method {request.PaymentMethodId} was not found for this shopper.",
                    "PAYMENT_METHOD_NOT_FOUND");
            }

            var providerCard = await _gateway.GetCardAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
            if (providerCard is null || !providerCard.Enabled)
            {
                throw new PaymentApiException(
                    400,
                    "That saved card is no longer usable; save it again or pay with another card.",
                    "PAYMENT_METHOD_DISABLED");
            }

            var identity = await _db.ShopperFinixIdentities
                .FindAsync(new object[] { shopperId }, cancellationToken);

            if (identity?.FinixIdentityId is { Length: > 0 } identityId
                && providerCard.IdentityId is { Length: > 0 } providerIdentityId
                && !string.Equals(identityId, providerIdentityId, StringComparison.Ordinal))
            {
                throw new PaymentApiException(
                    403,
                    "That saved card does not belong to this shopper.",
                    "PAYMENT_METHOD_OWNERSHIP");
            }

            return savedCard.FinixPaymentInstrumentId;
        }

        if (request.Card is null)
        {
            throw new PaymentApiException(
                400,
                "Provide either a saved paymentMethodId or card details.",
                "MISSING_PAYMENT_INSTRUMENT");
        }

        var card = NormalizeCard(request.Card);
        var finixIdentityId = await _identityService.EnsureAsync(shopperId, cancellationToken);
        var created = await _gateway.CreateCardAsync(finixIdentityId, card, cancellationToken);

        if (string.IsNullOrWhiteSpace(created.Id))
        {
            throw new PaymentApiException(
                502,
                "Finix tokenized the card without returning a payment instrument id.",
                "MISSING_PAYMENT_INSTRUMENT_ID");
        }

        return created.Id;
    }

    private async Task<bool> TrySettleAuthorizationAsync(
        Order order,
        PaymentOperationClaim claim,
        CancellationToken cancellationToken)
    {
        try
        {
            var idempotencyId = $"PAY:{order.PaymentReference}";
            var authorization = await _gateway.FindAuthorizationByIdempotencyAsync(
                idempotencyId,
                claim.CreatedAt.AddMinutes(-2),
                cancellationToken);

            if (authorization is null || !IsAuthorizationSucceeded(authorization))
            {
                return false;
            }

            if (authorization.Amount != order.TotalCents)
            {
                return false;
            }

            order.MarkAuthorized(
                authorization.Id,
                authorization.Source ?? order.FinixPaymentInstrumentId ?? "unknown",
                authorization.Amount,
                authorization.State);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, authorization.Id, null, cancellationToken);
            return true;
        }
        catch (PaymentProviderException)
        {
            return false;
        }
    }

    private async Task<bool> TrySettleCaptureAsync(
        Order order,
        PaymentOperationClaim claim,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            return false;
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(authorization.TransferId))
            {
                return false;
            }

            var transfer = await _gateway.GetTransferAsync(authorization.TransferId, cancellationToken);
            var capturedAmount = transfer is { Amount: > 0 }
                ? transfer.Amount
                : (authorization.Amount > 0 ? authorization.Amount : order.AuthorizedAmountCents);
            var fee = transfer?.Fee ?? 0;

            order.MarkCaptured(authorization.TransferId!, capturedAmount, fee, capturedAmount - fee, transfer?.State ?? authorization.State);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, authorization.TransferId, null, cancellationToken);
            return true;
        }
        catch (PaymentProviderException)
        {
            return false;
        }
    }

    private async Task<bool> TrySettleVoidAsync(
        Order order,
        PaymentOperationClaim claim,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            return false;
        }

        try
        {
            var authorization = await _gateway.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
            if (!IsAuthorizationVoided(authorization))
            {
                return false;
            }

            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, order.FinixAuthorizationId, null, cancellationToken);
            return true;
        }
        catch (PaymentProviderException)
        {
            return false;
        }
    }

    private async Task EnsurePartialRefundAllowedAsync(
        Order order,
        long amount,
        CancellationToken cancellationToken)
    {
        if (amount >= order.CapturedAmountCents || string.IsNullOrWhiteSpace(order.FinixTransferId))
        {
            return;
        }

        var transfer = await _gateway.GetTransferAsync(order.FinixTransferId, cancellationToken);
        if (transfer is null)
        {
            throw new PaymentApiException(
                409,
                "The captured transfer could not be found at Finix.",
                "CAPTURE_TRANSFER_NOT_FOUND");
        }

        if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(
                409,
                "Finix is still processing the captured transfer; a partial refund can be issued once the transfer has succeeded. Retry shortly, or refund the full captured amount now.",
                "CAPTURE_STILL_PENDING");
        }

        if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentApiException(
                409,
                $"The captured transfer is {transfer.State} at Finix and cannot be refunded.",
                "CAPTURE_NOT_REFUNDABLE");
        }
    }

    private async Task<RefundRecord?> TrySettleRefundAsync(
        Order order,
        PaymentOperationClaim claim,
        string idempotencyKey,
        long? requestedAmount,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.FinixTransferId))
        {
            return null;
        }

        try
        {
            var amount = requestedAmount ?? (order.CapturedAmountCents - order.RefundedAmountCents);
            var matches = await _gateway.FindRefundsAsync(
                order.FinixTransferId,
                claim.CreatedAt.AddMinutes(-2),
                cancellationToken);

            var match = matches.FirstOrDefault(item => item.Amount == amount && !string.IsNullOrWhiteSpace(item.Id))
                        ?? matches.FirstOrDefault(item => item.Amount > 0 && !string.IsNullOrWhiteSpace(item.Id))
                        ?? matches.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Id));
            if (match is null)
            {
                return null;
            }

            var existing = await _db.RefundRecords.FirstOrDefaultAsync(
                record => record.FinixRefundTransferId == match.Id,
                cancellationToken);

            if (existing is not null)
            {
                return existing;
            }

            var record = new RefundRecord(
                order.Id,
                idempotencyKey,
                match.Id,
                match.Amount > 0 ? match.Amount : amount,
                match.State);

            _db.RefundRecords.Add(record);
            order.MarkRefunded(record.AmountCents);
            await _db.SaveChangesAsync(cancellationToken);
            await _claims.CompleteAsync(claim, match.Id, null, cancellationToken);
            return record;
        }
        catch (PaymentProviderException)
        {
            return null;
        }
    }

    private async Task TryReleaseMismatchedHoldAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _gateway.VoidAsync(authorizationId, cancellationToken);
        }
        catch (PaymentProviderException exception)
        {
            _logger.LogWarning(
                exception,
                "Could not release mismatched authorization {AuthorizationId}.",
                authorizationId);
        }
    }

    private static void ValidatePayRequest(PayOrderRequest request)
    {
        var hasSavedCard = !string.IsNullOrWhiteSpace(request.PaymentMethodId);
        var hasCard = request.Card is not null;

        if (hasSavedCard == hasCard)
        {
            throw new PaymentApiException(
                400,
                "Provide exactly one of paymentMethodId or card.",
                "AMBIGUOUS_PAYMENT_INSTRUMENT");
        }
    }

    private static CardDetails NormalizeCard(CardPaymentRequest request) =>
        CardValidation.Normalize(
            request.Number,
            request.ExpirationMonth,
            request.ExpirationYear,
            request.Name,
            request.SecurityCode);

    private static Address ToAddress(ShippingAddressRequest? request)
    {
        if (request is null)
        {
            return new Address("1 Main Street", "Default City", "NA", "USA", "00000");
        }

        return new Address(
            ValueOr(request.Street, "1 Main Street"),
            ValueOr(request.City, "Default City"),
            ValueOr(request.State, "NA"),
            ValueOr(request.Country, "USA"),
            ValueOr(request.ZipCode, "00000"));
    }

    private static string ValueOr(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static bool IsAuthorizationSucceeded(FinixAuthorizationInfo authorization) =>
        string.Equals(authorization.State, AuthorizationSucceeded, StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationVoided(FinixAuthorizationInfo authorization) =>
        authorization.IsVoid
        || string.Equals(authorization.VoidState, AuthorizationSucceeded, StringComparison.OrdinalIgnoreCase)
        || string.Equals(authorization.State, AuthorizationCanceled, StringComparison.OrdinalIgnoreCase);

    private static bool IsAuthorizationUnavailable(FinixAuthorizationInfo authorization) =>
        IsAuthorizationVoided(authorization)
        || string.Equals(authorization.State, AuthorizationFailed, StringComparison.OrdinalIgnoreCase)
        || string.Equals(authorization.State, AuthorizationCanceled, StringComparison.OrdinalIgnoreCase);

    private static bool IsPaid(OrderPaymentStatus status) =>
        status is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded;

    private static bool IsCaptured(OrderPaymentStatus status) =>
        status is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded;

    private static string BuildCaptureUnavailableMessage(FinixAuthorizationInfo authorization)
    {
        if (IsAuthorizationVoided(authorization))
        {
            return "The card hold was voided or released, so it can no longer be captured. " +
                   "The order was not fulfilled; ask the shopper to pay again.";
        }

        return $"The card hold can no longer be captured ({authorization.FailureMessage ?? authorization.State ?? "unknown state"}). " +
               "The order was not fulfilled; ask the shopper to pay again.";
    }

    private static CreateOrderResponse ToCreateOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.PaymentStatus.ToString(),
        TotalCents = order.TotalCents,
        Currency = order.Currency,
        OrderDate = order.OrderDate
    };

    private static PayOrderResponse ToPayOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.PaymentStatus.ToString(),
        AuthorizationId = order.FinixAuthorizationId,
        AuthorizationState = order.FinixAuthorizationState,
        PaymentMethodId = order.FinixPaymentInstrumentId,
        AuthorizedAmountCents = order.AuthorizedAmountCents,
        TotalCents = order.TotalCents,
        Currency = order.Currency,
        FailureCode = order.PaymentFailureCode,
        FailureMessage = order.PaymentFailureMessage
    };

    private static FulfilOrderResponse ToFulfilOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.PaymentStatus.ToString(),
        AuthorizationId = order.FinixAuthorizationId,
        TransferId = order.FinixTransferId,
        CaptureState = order.FinixCaptureState,
        CapturedAmountCents = order.CapturedAmountCents,
        FeeCents = order.FeeCents,
        NetCents = order.NetCents,
        RefundedAmountCents = order.RefundedAmountCents
    };

    private static CancelOrderResponse ToCancelOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.PaymentStatus.ToString(),
        AuthorizationId = order.FinixAuthorizationId,
        Released = order.PaymentStatus == OrderPaymentStatus.Cancelled
    };

    private static RefundOrderResponse ToRefundOrderResponse(Order order, RefundRecord record) => new()
    {
        OrderId = order.Id,
        RefundId = record.FinixRefundTransferId,
        RefundAmountCents = record.AmountCents,
        TotalRefundedCents = order.RefundedAmountCents,
        CapturedAmountCents = order.CapturedAmountCents,
        RefundState = record.FinixState,
        Status = order.PaymentStatus.ToString()
    };

    private static MyOrderResponse ToMyOrderResponse(Order order) => new()
    {
        OrderId = order.Id,
        Status = order.PaymentStatus.ToString(),
        OrderDate = order.OrderDate,
        TotalCents = order.TotalCents,
        Currency = order.Currency,
        AuthorizedAmountCents = order.AuthorizedAmountCents,
        CapturedAmountCents = order.CapturedAmountCents,
        FeeCents = order.FeeCents,
        NetCents = order.NetCents,
        RefundedAmountCents = order.RefundedAmountCents,
        AuthorizationId = order.FinixAuthorizationId,
        AuthorizationState = order.FinixAuthorizationState,
        TransferId = order.FinixTransferId,
        CaptureState = order.FinixCaptureState,
        FailureCode = order.PaymentFailureCode,
        FailureMessage = order.PaymentFailureMessage
    };
}
