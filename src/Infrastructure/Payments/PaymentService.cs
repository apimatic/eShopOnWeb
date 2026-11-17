using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks =
        new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _options;
    private readonly IUriComposer _uriComposer;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        IFinixClient finix,
        IOptions<FinixOptions> options,
        IUriComposer uriComposer,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderWithRefunds> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<NewOrderItem> items,
        Address shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new ApiException(401, "The caller identity could not be determined from the token.");
        }

        if (items is null || items.Count == 0)
        {
            throw new ApiException(400, "At least one order item is required.");
        }

        if (items.Any(item => item.Quantity <= 0))
        {
            throw new ApiException(400, "Every order item quantity must be greater than zero.");
        }

        var catalogItemIds = items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var missing = catalogItemIds.Except(catalogItems.Select(item => item.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new ApiException(404, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requestedItem in items)
        {
            var catalogItem = catalogItems.First(item => item.Id == requestedItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requestedItem.Quantity));
        }

        var currency = NormalizeCurrency(_options.Currency);
        var order = new Order(buyerId, shippingAddress, orderItems);
        order.SetCurrency(currency);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} with total {Total} {Currency}",
            order.Id, buyerId, order.Total(), currency);

        return new OrderWithRefunds(order, Array.Empty<OrderRefund>());
    }

    public async Task<OrderWithRefunds> AuthorizeOrderAsync(
        int orderId,
        string buyerId,
        CardDetails? card,
        int? savedPaymentMethodId,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var orderLock = GetOrderLock(orderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = EnsureOrderOwnedBy(await GetOrderAsync(orderId, cancellationToken), buyerId);


            if (order.PaymentStatus is OrderPaymentStatus.Authorized
                or OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                throw new ApiException(409, "This order was canceled and can no longer be paid.");
            }

            if ((card is null) == (savedPaymentMethodId is null))
            {
                throw new ApiException(400, "Provide either card details or a saved paymentMethodId, but not both.");
            }

            var currency = GetOrderCurrency(order);
            var amountInCents = ToCents(order.Total());
            if (amountInCents <= 0)
            {
                throw new ApiException(400, "The order total must be greater than zero.");
            }

            string sourceId;
            if (savedPaymentMethodId.HasValue)
            {
                var saved = await _db.SavedPaymentMethods
                    .FirstOrDefaultAsync(method => method.Id == savedPaymentMethodId.Value && method.OwnerId == buyerId,
                        cancellationToken);
                if (saved is null)
                {
                    throw new ApiException(404, "Saved payment method not found.");
                }

                sourceId = saved.FinixPaymentInstrumentId;
            }
            else
            {
                ValidateCard(card!);
                var identityId = await EnsureCustomerIdentityAsync(buyerId, cancellationToken);
                try
                {
                    var instrument = await _finix.CreateCardPaymentInstrumentAsync(card!, identityId, buyerId, cancellationToken);
                    sourceId = instrument.Id;
                }
                catch (FinixApiException ex)
                {
                    order.MarkPaymentFailed(ex.Message);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new ApiException(MapPaymentErrorStatus(ex.StatusCode), ex.Message, ex.Code);
                }
            }

            var key = string.IsNullOrWhiteSpace(idempotencyKey)
                ? order.PaymentIdempotencyKey ?? Guid.NewGuid().ToString("N")
                : idempotencyKey!.Trim();

            order.BeginPaymentAttempt(key);
            await _db.SaveChangesAsync(cancellationToken);

            try
            {
                var authorization = await _finix.CreateAuthorizationAsync(
                    amountInCents,
                    currency,
                    sourceId,
                    key,
                    new Dictionary<string, string>
                    {
                        ["order_id"] = order.Id.ToString(),
                        ["buyer_id"] = buyerId
                    },
                    cancellationToken);

                if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    var message = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                        ? $"Finix did not authorize the payment (state: {authorization.State})."
                        : authorization.FailureMessage!;
                    order.MarkPaymentFailed(message);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new ApiException(402, message, authorization.FailureCode);
                }

                var authorizedAmount = authorization.Amount > 0 ? FromCents(authorization.Amount) : FromCents(amountInCents);
                order.MarkPaymentAuthorized(
                    authorization.Id,
                    authorization.State ?? "SUCCEEDED",
                    authorizedAmount,
                    DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Authorized order {OrderId} with Finix authorization {AuthorizationId} for {Amount} {Currency}",
                    order.Id, authorization.Id, authorizedAmount, currency);

                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                order.MarkPaymentFailed(ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new ApiException(MapPaymentErrorStatus(ex.StatusCode), ex.Message, ex.Code);
            }
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderWithRefunds> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = GetOrderLock(orderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order is null)
            {
                throw new ApiException(404, "Order not found.");
            }

            if (order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                if (!order.IsFulfilled)
                {
                    order.MarkFulfilled(DateTimeOffset.UtcNow);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                throw new ApiException(409, "This order was canceled; there is no held payment to capture.");
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                throw new ApiException(409,
                    "This order has no authorized payment to capture. Ask the shopper to pay for the order first.");
            }

            var authorizationId = order.FinixAuthorizationId!;
            var captureAmountInCents = ToCents(order.AuthorizedAmount ?? order.Total());
            var currency = GetOrderCurrency(order);

            FinixAuthorization? currentAuthorization = null;
            try
            {
                currentAuthorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
                order.UpdateAuthorizationState(currentAuthorization.State ?? order.AuthorizationState ?? "UNKNOWN");
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning(ex, "Could not refresh Finix authorization {AuthorizationId} before capture", authorizationId);
            }

            var existingTransferId = currentAuthorization?.Transfer;
            if (currentAuthorization is not null)
            {
                var cannotCapture = DescribeUncapturableAuthorization(currentAuthorization);
                if (cannotCapture is not null && string.IsNullOrWhiteSpace(existingTransferId))
                {
                    order.MarkCaptureFailed(cannotCapture);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new ApiException(409, cannotCapture);
                }
            }

            FinixTransfer? transfer;
            string? captureState;
            if (!string.IsNullOrWhiteSpace(existingTransferId))
            {
                transfer = await _finix.GetTransferAsync(existingTransferId!, cancellationToken);
                captureState = transfer.State;
            }
            else
            {
                FinixAuthorization capture;
                try
                {
                    capture = await _finix.CaptureAuthorizationAsync(authorizationId, captureAmountInCents, cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    var recovered = await TryRecoverCapturedTransferAsync(authorizationId, cancellationToken);
                    if (recovered is null)
                    {
                        var message =
                            $"Finix could not capture authorization {authorizationId}: {ex.Message} " +
                            "The hold may have expired or been voided. Ask the shopper to authorize the payment again.";
                        order.MarkCaptureFailed(message);
                        await _db.SaveChangesAsync(cancellationToken);
                        throw new ApiException(409, message, ex.Code);
                    }

                    transfer = recovered;
                    captureState = recovered.State;
                    goto TransferLoaded;
                }

                captureState = capture.State;
                var transferId = capture.Transfer;
                if (string.IsNullOrWhiteSpace(transferId))
                {
                    var refreshed = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
                    transferId = refreshed.Transfer;
                }

                if (string.IsNullOrWhiteSpace(transferId))
                {
                    var message =
                        $"Finix accepted the capture for authorization {authorizationId} but did not return a transfer id. " +
                        "Re-run fulfilment after checking the authorization in Finix; no second capture will be created.";
                    order.MarkCaptureFailed(message);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new ApiException(502, message);
                }

                transfer = await _finix.GetTransferAsync(transferId!, cancellationToken);
            }

        TransferLoaded:

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                var message = string.IsNullOrWhiteSpace(transfer.FailureMessage)
                    ? $"Finix reported the capture as {transfer.State}. The payment was not collected."
                    : $"Finix reported the capture as {transfer.State}: {transfer.FailureMessage}";
                order.MarkCaptureFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new ApiException(409, message, transfer.FailureCode);
            }

            var capturedAmount = transfer.Amount > 0 ? FromCents(transfer.Amount) : FromCents(captureAmountInCents);
            var feeAmount = await GetTransferFeeAsync(transfer, cancellationToken);
            var netProceeds = capturedAmount - feeAmount;

            order.MarkCaptured(
                transfer.Id,
                transfer.State ?? captureState ?? "UNKNOWN",
                capturedAmount,
                feeAmount,
                netProceeds,
                DateTimeOffset.UtcNow);
            order.MarkFulfilled(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Captured order {OrderId} with Finix transfer {TransferId}: captured={Captured}, fee={Fee}, net={Net} {Currency}",
                order.Id, transfer.Id, capturedAmount, feeAmount, netProceeds, currency);

            return await ToOrderWithRefundsAsync(order, cancellationToken);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderWithRefunds> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = GetOrderLock(orderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            if (order is null)
            {
                throw new ApiException(404, "Order not found.");
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            if (order.IsFulfilled || order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            {
                throw new ApiException(409,
                    "This order has already been fulfilled. Use the refunds endpoint to return captured funds.");
            }

            if (order.PaymentStatus is OrderPaymentStatus.AwaitingPayment or OrderPaymentStatus.PaymentFailed)
            {
                order.MarkCanceled(DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            var authorizationId = order.FinixAuthorizationId;
            if (string.IsNullOrWhiteSpace(authorizationId))
            {
                order.MarkCanceled(DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            FinixAuthorization? current = null;
            try
            {
                current = await _finix.GetAuthorizationAsync(authorizationId!, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning(ex, "Could not refresh Finix authorization {AuthorizationId} before void", authorizationId);
            }

            if (current is not null && !string.IsNullOrWhiteSpace(current.Transfer))
            {
                throw new ApiException(409,
                    "Finix already created a capture for this order. It can no longer be canceled; use the refunds endpoint.");
            }

            if (current is not null && (current.IsVoid ||
                string.Equals(current.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(current.VoidState, "PENDING", StringComparison.OrdinalIgnoreCase)))
            {
                order.MarkCanceled(DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }

            try
            {
                var voidResult = await _finix.VoidAuthorizationAsync(authorizationId!, cancellationToken);
                var released = voidResult.IsVoid ||
                               string.Equals(voidResult.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                               string.Equals(voidResult.VoidState, "PENDING", StringComparison.OrdinalIgnoreCase);

                if (!released)
                {
                    var message =
                        $"Finix did not confirm release of authorization {authorizationId} " +
                        $"(void state: {voidResult.VoidState ?? "UNKNOWN"}, authorization state: {voidResult.State ?? "UNKNOWN"}). " +
                        "Check the authorization in Finix and retry cancel.";
                    order.MarkCaptureFailed(message);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new ApiException(409, message);
                }

                order.MarkCanceled(DateTimeOffset.UtcNow);
                await _db.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Canceled order {OrderId}; released Finix authorization {AuthorizationId}",
                    order.Id, authorizationId);
                return await ToOrderWithRefundsAsync(order, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var message =
                    $"Finix could not release authorization {authorizationId}: {ex.Message} " +
                    "The hold may already be captured or expired. Check the authorization in Finix and retry cancel.";
                order.MarkCaptureFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new ApiException(409, message, ex.Code);
            }
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderRefund> RefundOrderAsync(
        int orderId,
        string buyerId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new ApiException(400, "An idempotency key is required for refunds.");
        }

        idempotencyKey = idempotencyKey.Trim();

        var orderLock = GetOrderLock(orderId);
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = EnsureOrderOwnedBy(await GetOrderAsync(orderId, cancellationToken), buyerId);


            if (order!.PaymentStatus is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded))
            {
                throw new ApiException(409, "Refunds are only available after the order has been fulfilled and captured.");
            }

            if (string.IsNullOrWhiteSpace(order.FinixCaptureId))
            {
                throw new ApiException(409, "This order has no captured Finix transfer to refund.");
            }

            var existing = await _db.OrderRefunds.FirstOrDefaultAsync(
                refund => refund.OrderId == orderId && refund.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existing is not null)
            {
                _logger.LogInformation("Refund request {IdempotencyKey} for order {OrderId} already processed as {RefundId}",
                    idempotencyKey, orderId, existing.FinixRefundId);
                return existing;
            }

            var refunds = await GetRefundsAsync(orderId, cancellationToken);
            var alreadyRefunded = refunds
                .Where(refund => !IsFailedRefund(refund.State))
                .Sum(refund => refund.Amount);

            var capturedAmount = order.CapturedAmount ?? 0m;
            var remaining = capturedAmount - alreadyRefunded;
            if (remaining <= 0m)
            {
                throw new ApiException(409, "This order has already been fully refunded.");
            }

            var requestedAmount = amount ?? remaining;
            if (requestedAmount <= 0m)
            {
                throw new ApiException(400, "The refund amount must be greater than zero.");
            }

            if (requestedAmount > remaining)
            {
                throw new ApiException(409,
                    $"Refund amount {requestedAmount:0.00} exceeds the remaining refundable amount {remaining:0.00} {order.PaymentCurrency}.");
            }

            var refundAmountInCents = ToCents(requestedAmount);
            var currency = GetOrderCurrency(order);
            var isPartialRefund = requestedAmount < capturedAmount;

            if (isPartialRefund)
            {
                await WaitForCaptureToSettleAsync(order, cancellationToken);
            }

            FinixTransfer refundTransfer;
            try
            {
                refundTransfer = await _finix.CreateReversalAsync(order.FinixCaptureId!, refundAmountInCents, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                if (isPartialRefund && ex.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    await WaitForCaptureToSettleAsync(order, cancellationToken);
                    try
                    {
                        refundTransfer = await _finix.CreateReversalAsync(order.FinixCaptureId!, refundAmountInCents, cancellationToken);
                    }
                    catch (FinixApiException retryException)
                    {
                        var retryMessage = $"Finix could not refund capture {order.FinixCaptureId}: {retryException.Message}";
                        throw new ApiException(409, retryMessage, retryException.Code);
                    }
                }
                else
                {
                    var message = $"Finix could not refund capture {order.FinixCaptureId}: {ex.Message}";
                    throw new ApiException(409, message, ex.Code);
                }
            }

            if (string.Equals(refundTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(refundTransfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                var message = string.IsNullOrWhiteSpace(refundTransfer.FailureMessage)
                    ? $"Finix reported the refund as {refundTransfer.State}."
                    : $"Finix reported the refund as {refundTransfer.State}: {refundTransfer.FailureMessage}";
                throw new ApiException(409, message, refundTransfer.FailureCode);
            }

            var refundedAmount = refundTransfer.Amount > 0
                ? FromCents(refundTransfer.Amount)
                : FromCents(refundAmountInCents);

            var refund = new OrderRefund(
                orderId,
                refundTransfer.Id,
                refundedAmount,
                refundTransfer.Currency ?? currency,
                refundTransfer.State ?? "UNKNOWN",
                idempotencyKey);

            _db.OrderRefunds.Add(refund);
            order.ApplyRefund(alreadyRefunded + refundedAmount);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Refunded order {OrderId} with Finix transfer {RefundId} for {Amount} {Currency}",
                orderId, refundTransfer.Id, refundedAmount, currency);

            return refund;
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<OrderWithRefunds>> GetOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return Array.Empty<OrderWithRefunds>();
        }

        var orderIds = orders.Select(order => order.Id).ToArray();
        var refunds = await _db.OrderRefunds
            .Where(refund => orderIds.Contains(refund.OrderId))
            .ToListAsync(cancellationToken);
        var refundsByOrder = refunds
            .GroupBy(refund => refund.OrderId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<OrderRefund>)group.ToList());

        return orders
            .Select(order => new OrderWithRefunds(
                order,
                refundsByOrder.TryGetValue(order.Id, out var orderRefunds)
                    ? orderRefunds
                    : Array.Empty<OrderRefund>()))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string ownerId,
        CardDetails card,
        CancellationToken cancellationToken = default)
    {
        ValidateCard(card);

        var identityId = await EnsureCustomerIdentityAsync(ownerId, cancellationToken);
        FinixPaymentInstrument instrument;
        try
        {
            instrument = await _finix.CreateCardPaymentInstrumentAsync(card, identityId, ownerId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new ApiException(MapPaymentErrorStatus(ex.StatusCode), ex.Message, ex.Code);
        }

        var saved = new SavedPaymentMethod(
            ownerId,
            instrument.Id,
            instrument.Brand,
            instrument.LastFour,
            instrument.ExpirationMonth ?? card.ExpirationMonth,
            instrument.ExpirationYear ?? card.ExpirationYear,
            instrument.Name ?? card.Name);

        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId} (brand {Brand}, last4 {Last4})",
            saved.Id, ownerId, saved.Brand, saved.LastFour);

        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(
        string ownerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.SavedPaymentMethods
            .Where(method => method.OwnerId == ownerId)
            .OrderByDescending(method => method.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(
        string ownerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
            method => method.Id == paymentMethodId && method.OwnerId == ownerId,
            cancellationToken);

        if (saved is null)
        {
            throw new ApiException(404, "Saved payment method not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning(ex,
                "Finix could not disable payment instrument {PaymentInstrumentId} for saved method {PaymentMethodId}; removing it locally anyway",
                saved.FinixPaymentInstrumentId, saved.Id);
        }

        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ApiException(400, "The 'to' date-time must be greater than or equal to 'from'.");
        }

        var authorizationTask = _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        var transferTask = _finix.ListTransfersAsync(from, to, cancellationToken);
        await Task.WhenAll(authorizationTask, transferTask);

        var authorizations = await authorizationTask;
        var transfers = await transferTask;

        var orders = await _db.Orders
            .Where(order => order.FinixAuthorizationId != null || order.FinixCaptureId != null)
            .ToListAsync(cancellationToken);
        var refunds = await _db.OrderRefunds.ToListAsync(cancellationToken);

        var orderByAuthorization = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            .ToDictionary(order => order.FinixAuthorizationId!, order => order, StringComparer.Ordinal);
        var orderByCapture = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.FinixCaptureId))
            .ToDictionary(order => order.FinixCaptureId!, order => order, StringComparer.Ordinal);
        var orderByRefund = refunds
            .GroupBy(refund => refund.FinixRefundId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var finixIds = new HashSet<string>(StringComparer.Ordinal);
        var transactions = new List<ReconciliationTransaction>();

        foreach (var authorization in authorizations)
        {
            finixIds.Add(authorization.Id);
            orderByAuthorization.TryGetValue(authorization.Id, out var order);
            transactions.Add(new ReconciliationTransaction
            {
                Source = "Finix",
                Type = "AUTHORIZATION",
                FinixId = authorization.Id,
                Amount = FromCents(authorization.Amount),
                Currency = authorization.Currency ?? _options.Currency ?? string.Empty,
                State = authorization.State ?? "UNKNOWN",
                CreatedAt = authorization.CreatedAt ?? from,
                OrderId = order?.Id,
                Matched = order is not null,
                Note = order is null ? "Finix authorization has no matching eShop order." : null
            });
        }

        foreach (var transfer in transfers)
        {
            finixIds.Add(transfer.Id);

            Order? order = null;
            if (string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase))
            {
                if (orderByRefund.TryGetValue(transfer.Id, out var refund))
                {
                    order = orders.FirstOrDefault(candidate => candidate.Id == refund.OrderId);
                }
                else if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer))
                {
                    orderByCapture.TryGetValue(transfer.ParentTransfer!, out order);
                }
            }
            else
            {
                orderByCapture.TryGetValue(transfer.Id, out order);
            }

            var type = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                ? "REFUND"
                : transfer.Type ?? "TRANSFER";

            transactions.Add(new ReconciliationTransaction
            {
                Source = "Finix",
                Type = type,
                FinixId = transfer.Id,
                Amount = FromCents(transfer.Amount),
                Currency = transfer.Currency ?? _options.Currency ?? string.Empty,
                State = transfer.State ?? "UNKNOWN",
                CreatedAt = transfer.CreatedAt ?? from,
                OrderId = order?.Id,
                Matched = order is not null,
                Note = order is null ? "Finix transfer has no matching eShop order." : null
            });
        }

        var eshopOnly = new List<ReconciliationTransaction>();

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId) &&
                !finixIds.Contains(order.FinixAuthorizationId!) &&
                InRange(order.AuthorizedAt, from, to))
            {
                eshopOnly.Add(new ReconciliationTransaction
                {
                    Source = "eShop",
                    Type = "AUTHORIZATION",
                    FinixId = order.FinixAuthorizationId!,
                    Amount = order.AuthorizedAmount ?? 0m,
                    Currency = order.PaymentCurrency,
                    State = order.AuthorizationState ?? order.PaymentStatus,
                    CreatedAt = order.AuthorizedAt ?? order.OrderDate,
                    OrderId = order.Id,
                    Matched = false,
                    Note = "eShop has an authorization that Finix did not return for this range."
                });
            }

            if (!string.IsNullOrWhiteSpace(order.FinixCaptureId) &&
                !finixIds.Contains(order.FinixCaptureId!) &&
                InRange(order.CapturedAt, from, to))
            {
                eshopOnly.Add(new ReconciliationTransaction
                {
                    Source = "eShop",
                    Type = "CAPTURE",
                    FinixId = order.FinixCaptureId!,
                    Amount = order.CapturedAmount ?? 0m,
                    Currency = order.PaymentCurrency,
                    State = order.CaptureState ?? order.PaymentStatus,
                    CreatedAt = order.CapturedAt ?? order.OrderDate,
                    OrderId = order.Id,
                    Matched = false,
                    Note = "eShop has a capture that Finix did not return for this range."
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (!finixIds.Contains(refund.FinixRefundId) && InRange(refund.CreatedAt, from, to))
            {
                eshopOnly.Add(new ReconciliationTransaction
                {
                    Source = "eShop",
                    Type = "REFUND",
                    FinixId = refund.FinixRefundId,
                    Amount = refund.Amount,
                    Currency = refund.Currency,
                    State = refund.State,
                    CreatedAt = refund.CreatedAt,
                    OrderId = refund.OrderId,
                    Matched = false,
                    Note = "eShop has a refund that Finix did not return for this range."
                });
            }
        }

        var matchedCount = transactions.Count(transaction => transaction.Matched);
        return new ReconciliationReport
        {
            From = from,
            To = to,
            FinixTransactions = transactions,
            EshopOnlyTransactions = eshopOnly,
            MatchedCount = matchedCount,
            UnmatchedFinixCount = transactions.Count - matchedCount
        };
    }

    private async Task WaitForCaptureToSettleAsync(Order order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.FinixCaptureId))
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (true)
        {
            FinixTransfer transfer;
            try
            {
                transfer = await _finix.GetTransferAsync(order.FinixCaptureId!, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning(ex, "Could not check settlement state of capture {CaptureId}", order.FinixCaptureId);
                return;
            }

            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                order.UpdateCaptureState(transfer.State!);
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(409,
                    $"Finix capture {order.FinixCaptureId} is in state {transfer.State} and cannot be partially refunded.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new ApiException(409,
                    $"Finix capture {order.FinixCaptureId} is still PENDING. Partial refunds are only allowed after the capture succeeds; " +
                    "retry shortly, or issue a full refund now.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }
    }

    private async Task<FinixTransfer?> TryRecoverCapturedTransferAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            var refreshed = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (string.IsNullOrWhiteSpace(refreshed.Transfer))
            {
                return null;
            }

            return await _finix.GetTransferAsync(refreshed.Transfer!, cancellationToken);
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private async Task<decimal> GetTransferFeeAsync(FinixTransfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            var fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
            if (fees.Count > 0)
            {
                return FromCents(fees.Sum(fee => fee.Amount));
            }
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning(ex, "Could not list Finix fees for transfer {TransferId}; falling back to transfer fee", transfer.Id);
        }

        return transfer.Fee.HasValue ? FromCents(transfer.Fee.Value) : 0m;
    }

    private async Task<string> EnsureCustomerIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        var existing = await _db.PaymentCustomers
            .FirstOrDefaultAsync(customer => customer.OwnerId == ownerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var customerLock = CustomerLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.PaymentCustomers
                .FirstOrDefaultAsync(customer => customer.OwnerId == ownerId, cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            var email = BuildIdentityEmail(ownerId);
            FinixIdentity identity;
            try
            {
                identity = await _finix.CreateBuyerIdentityAsync(email, null, ownerId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new ApiException(MapPaymentErrorStatus(ex.StatusCode),
                    $"Could not create a Finix buyer identity: {ex.Message}", ex.Code);
            }

            var customer = new PaymentCustomer(ownerId, identity.Id);
            _db.PaymentCustomers.Add(customer);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created Finix buyer identity {IdentityId} for buyer {BuyerId}", identity.Id, ownerId);
            return identity.Id;
        }
        finally
        {
            customerLock.Release();
        }
    }

    private async Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(order => order.OrderItems)
            .ThenInclude(item => item.ItemOrdered)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
    }

    private async Task<IReadOnlyList<OrderRefund>> GetRefundsAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.OrderRefunds
            .Where(refund => refund.OrderId == orderId)
            .OrderBy(refund => refund.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<OrderWithRefunds> ToOrderWithRefundsAsync(Order order, CancellationToken cancellationToken)
    {
        var refunds = await GetRefundsAsync(order.Id, cancellationToken);
        return new OrderWithRefunds(order, refunds);
    }

    private static Order EnsureOrderOwnedBy(Order? order, string buyerId)
    {
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new ApiException(404, "Order not found.");
        }

        return order;
    }

    private static string? DescribeUncapturableAuthorization(FinixAuthorization authorization)
    {
        if (authorization.IsVoid ||
            string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Authorization {authorization.Id} was voided and can no longer be captured. " +
                   "Ask the shopper to authorize the payment again.";
        }

        if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(authorization.State, "VOIDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Authorization {authorization.Id} is in state {authorization.State} and can no longer be captured. " +
                   "Ask the shopper to authorize the payment again.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return $"Authorization {authorization.Id} expired at {authorization.ExpiresAt.Value:O} and can no longer be captured. " +
                   "Ask the shopper to authorize the payment again.";
        }

        return null;
    }

    private static bool IsFailedRefund(string state)
    {
        return string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
    {
        return value.HasValue && value.Value >= from && value.Value <= to;
    }

    private static int MapPaymentErrorStatus(int finixStatusCode)
    {
        return finixStatusCode is >= 400 and < 500 ? finixStatusCode : 502;
    }

    private string GetOrderCurrency(Order order)
    {
        var configuredOrOrderCurrency = string.IsNullOrWhiteSpace(order.PaymentCurrency)
            ? _options.Currency
            : order.PaymentCurrency;

        var currency = NormalizeCurrency(configuredOrOrderCurrency);
        if (string.IsNullOrWhiteSpace(order.PaymentCurrency))
        {
            order.SetCurrency(currency);
        }

        return currency;
    }

    private static string NormalizeCurrency(string? currency)
    {
        var value = (currency ?? string.Empty).Trim().ToUpperInvariant();
        if (value is not ("USD" or "CAD"))
        {
            throw new ApiException(500, "Finix currency is not configured or is not supported. Use USD or CAD.");
        }

        return value;
    }

    private static void ValidateCard(CardDetails card)
    {
        if (card is null)
        {
            throw new ApiException(400, "Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new ApiException(400, "Card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new ApiException(400, "Cardholder name is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new ApiException(400, "Card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new ApiException(400, "Card expiration year is in the past.");
        }
    }

    private static string BuildIdentityEmail(string ownerId)
    {
        var email = ownerId.Contains('@') ? ownerId : $"{ownerId}@eshop.local";
        return email.Length <= 100 ? email : email[..100];
    }

    private static long ToCents(decimal amount)
    {
        return checked((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static decimal FromCents(decimal cents)
    {
        return Math.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);
    }

    private static SemaphoreSlim GetOrderLock(int orderId)
    {
        return OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
    }
}
