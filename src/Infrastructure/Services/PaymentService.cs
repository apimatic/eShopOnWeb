using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Orchestrates the eShop order/payment aggregate with Finix. All money is
/// represented in integer cents at the Finix boundary.
/// </summary>
public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        CatalogContext db,
        IFinixClient finix,
        IOptions<FinixSettings> settings,
        ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _settings = settings.Value;
        _logger = logger;
    }

    private string Currency => string.IsNullOrWhiteSpace(_settings.Currency)
        ? "USD"
        : _settings.Currency.Trim().ToUpperInvariant();

    public async Task<OrderDto> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemInput> items,
        ShippingAddressInput? shippingAddress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The caller could not be identified from the token.");
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one order item is required.");
        }

        var normalizedItems = items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderItemInput
            {
                CatalogItemId = g.Key,
                Quantity = g.Sum(i => i.Quantity)
            })
            .ToList();

        if (normalizedItems.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Every item needs a positive catalogItemId and quantity.");
        }

        var catalogItemIds = normalizedItems.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missingIds = catalogItemIds.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new PaymentOperationException(
                400,
                $"Catalog item(s) not found: {string.Join(", ", missingIds)}.");
        }

        var address = BuildAddress(shippingAddress);

        var orderItems = normalizedItems.Select(item =>
        {
            var catalogItem = catalogItems.First(i => i.Id == item.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images\\products\\eCatalog-item-default.png"
                : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, item.Quantity);
        }).ToList();

        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, ToCents(order.Total()), Currency);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created order {OrderId} for buyer {BuyerId} with payment {PaymentId} awaiting payment.",
            order.Id,
            buyerId,
            payment.Id);

        return BuildOrderDto(order, payment, Array.Empty<PaymentRefund>());
    }

    public async Task<OrderDto> PayOrderAsync(
        string buyerId,
        int orderId,
        PayOrderInput input,
        CancellationToken cancellationToken)
    {
        if (input == null)
        {
            throw new PaymentOperationException(400, "A payment request body is required.");
        }

        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = EnsureBuyerOwnsOrder(
                await LoadOrderAsync(orderId, cancellationToken),
                buyerId);

            var payment = await GetOrCreatePaymentAsync(order, cancellationToken);

            if (payment.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return BuildOrderDto(order, payment, await LoadRefundsAsync(payment.Id, cancellationToken));
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw new PaymentOperationException(409, "This order was canceled and can no longer be paid.");
            }

            if (payment.Status == PaymentStatus.Authorized
                && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                return BuildOrderDto(order, payment, await LoadRefundsAsync(payment.Id, cancellationToken));
            }

            string source;
            if (input.PaymentMethodId.HasValue)
            {
                var savedCard = await _db.SavedCards.FirstOrDefaultAsync(
                    c => c.Id == input.PaymentMethodId.Value
                         && c.BuyerId == buyerId
                         && c.IsEnabled,
                    cancellationToken);

                if (savedCard == null)
                {
                    throw new PaymentOperationException(
                        404,
                        "The saved payment method was not found for this shopper.");
                }

                source = savedCard.FinixPaymentInstrumentId;
            }
            else if (input.Card != null)
            {
                ValidateCard(input.Card);
                NormalizeCard(input.Card);
                var identityId = await GetOrCreateIdentityAsync(buyerId, input.Card, cancellationToken);
                var instrument = await _finix.CreateCardPaymentInstrumentAsync(
                    identityId,
                    input.Card,
                    buyerId,
                    cancellationToken);
                source = instrument.Id;
            }
            else
            {
                throw new PaymentOperationException(
                    400,
                    "Provide either paymentMethodId (a saved card) or card details for a one-off payment.");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    payment.AmountInCents,
                    payment.Currency,
                    source,
                    payment.AuthorizationIdempotencyKey,
                    new Dictionary<string, string>
                    {
                        ["eshop_order_id"] = order.Id.ToString(),
                        ["eshop_buyer_id"] = buyerId
                    },
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                payment.MarkAuthorizationFailed(ex.Code, ex.Message);
                await _db.SaveChangesAsync(cancellationToken);
                throw MapFinixException(ex, "authorize the payment");
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed(
                    authorization.FailureCode,
                    authorization.FailureMessage ?? $"Authorization state was {authorization.State}.");
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentOperationException(
                    402,
                    $"The card could not be authorized (Finix state: {authorization.State}). "
                    + (authorization.FailureMessage ?? "Ask the shopper to use another card."),
                    authorization.FailureCode);
            }

            if (authorization.Amount != payment.AmountInCents)
            {
                try
                {
                    await _finix.VoidAuthorizationAsync(authorization.Id, cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    _logger.LogWarning(
                        "Failed to void a mismatched authorization {AuthorizationId}: {Message}",
                        authorization.Id,
                        ex.Message);
                }

                payment.MarkAuthorizationFailed(
                    "AMOUNT_MISMATCH",
                    $"Finix authorized {authorization.Amount} cents but the order total is {payment.AmountInCents} cents.");
                await _db.SaveChangesAsync(cancellationToken);

                throw new PaymentOperationException(
                    502,
                    $"Finix authorized {FormatCents(authorization.Amount)} instead of the order total "
                    + $"{FormatCents(payment.AmountInCents)}. The hold was released; ask the shopper to pay again.");
            }

            payment.MarkAuthorized(authorization.Id, authorization.State!, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Authorized {AmountInCents} cents for order {OrderId} with Finix authorization {AuthorizationId}.",
                payment.AmountInCents,
                order.Id,
                authorization.Id);

            return BuildOrderDto(order, payment, Array.Empty<PaymentRefund>());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentOperationException(404, $"Order {orderId} was not found.");

            var payment = await _db.OrderPayments
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

            if (payment == null)
            {
                throw new PaymentOperationException(
                    409,
                    "This order has no payment record. The shopper must pay before the order can be fulfilled.");
            }

            if (payment.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return BuildOrderDto(order, payment, await LoadRefundsAsync(payment.Id, cancellationToken));
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw new PaymentOperationException(409, "This order was canceled and cannot be fulfilled.");
            }

            if (payment.Status != PaymentStatus.Authorized
                || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                throw new PaymentOperationException(
                    409,
                    "This order has not been authorized. The shopper must pay before the order can be fulfilled.");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(
                    payment.FinixAuthorizationId,
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw MapFinixException(ex, "retrieve the authorization");
            }

            payment.RefreshAuthorization(authorization.State, authorization.ExpiresAt, authorization.IsVoid);

            if (authorization.IsVoid
                || !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    409,
                    $"The card authorization for order {orderId} is in state '{authorization.State}'"
                    + (authorization.IsVoid ? " and has been voided" : string.Empty)
                    + ". It can no longer be captured. Cancel the order or ask the shopper to pay again.",
                    authorization.VoidState);
            }

            if (authorization.ExpiresAt.HasValue
                && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow)
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    409,
                    $"The card authorization for order {orderId} expired at "
                    + $"{authorization.ExpiresAt.Value:O}. It can no longer be captured. "
                    + "Cancel the order or ask the shopper to pay again.",
                    "AUTHORIZATION_EXPIRED");
            }

            if (authorization.Amount != payment.AmountInCents)
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    409,
                    $"The card authorization holds {FormatCents(authorization.Amount)} but the order total is "
                    + $"{FormatCents(payment.AmountInCents)}. It cannot be captured for the order total. "
                    + "Cancel the order or ask the shopper to pay again.",
                    "AMOUNT_MISMATCH");
            }

            FinixAuthorization capture;
            try
            {
                capture = await _finix.CaptureAuthorizationAsync(
                    authorization.Id,
                    payment.AmountInCents,
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(
                    409,
                    $"Finix could not capture the authorization: {ex.Message} "
                    + "The hold may have expired or been voided. Cancel the order or ask the shopper to pay again.",
                    ex.Code);
            }

            var transferId = capture.Transfer;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                transferId = authorization.Transfer;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentOperationException(
                    502,
                    "Finix captured the authorization but did not return a capture transfer id. "
                    + "The order was not marked fulfilled; check the Finix dashboard before retrying.",
                    "MISSING_TRANSFER");
            }

            var transfer = await GetTransferWithRetryAsync(
                transferId,
                TimeSpan.FromSeconds(Math.Max(0, _settings.CaptureSettlementTimeoutSeconds)),
                cancellationToken);

            payment.MarkCaptured(
                transfer.Id,
                transfer.State ?? capture.State ?? "UNKNOWN",
                transfer.Amount,
                transfer.Fee);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Captured order {OrderId} via transfer {TransferId}: amount {AmountInCents}, fee {FeeInCents}, net {NetInCents}.",
                order.Id,
                transfer.Id,
                transfer.Amount,
                transfer.Fee,
                transfer.Amount - transfer.Fee);

            return BuildOrderDto(order, payment, Array.Empty<PaymentRefund>());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentOperationException(404, $"Order {orderId} was not found.");

            var payment = await _db.OrderPayments
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

            if (payment == null)
            {
                payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), Currency);
                _db.OrderPayments.Add(payment);
            }

            if (payment.Status is PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException(
                    409,
                    "This order has already been fulfilled. Use the refunds endpoint instead of cancel.");
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                return BuildOrderDto(order, payment, await LoadRefundsAsync(payment.Id, cancellationToken));
            }

            if (payment.Status == PaymentStatus.Authorized
                && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                FinixAuthorization authorization;
                try
                {
                    authorization = await _finix.GetAuthorizationAsync(
                        payment.FinixAuthorizationId,
                        cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    throw MapFinixException(ex, "retrieve the authorization");
                }

                if (authorization.IsVoid
                    || string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.MarkVoided(authorization.State);
                }
                else
                {
                    FinixAuthorization voided;
                    try
                    {
                        voided = await _finix.VoidAuthorizationAsync(
                            payment.FinixAuthorizationId,
                            cancellationToken);
                    }
                    catch (FinixApiException ex)
                    {
                        throw new PaymentOperationException(
                            409,
                            $"Finix could not release the hold: {ex.Message} "
                            + "No capture has been made. Retry the cancel before fulfilling the order.",
                            ex.Code);
                    }

                    payment.MarkVoided(voided.State);
                }

                _logger.LogInformation(
                    "Canceled order {OrderId} and released Finix authorization {AuthorizationId}.",
                    order.Id,
                    payment.FinixAuthorizationId);
            }
            else
            {
                payment.MarkCanceled();
                _logger.LogInformation("Canceled order {OrderId} before any funds were held.", order.Id);
            }

            await _db.SaveChangesAsync(cancellationToken);
            return BuildOrderDto(order, payment, await LoadRefundsAsync(payment.Id, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResult> RefundOrderAsync(
        string callerBuyerId,
        bool callerIsAdministrator,
        int orderId,
        decimal? amount,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(400, "idempotencyKey is required for a refund.");
        }

        idempotencyKey = idempotencyKey.Trim();
        if (idempotencyKey.Length > 255)
        {
            throw new PaymentOperationException(400, "idempotencyKey must be at most 255 characters.");
        }

        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken)
                        ?? throw new PaymentOperationException(404, $"Order {orderId} was not found.");

            if (!callerIsAdministrator
                && !string.Equals(order.BuyerId, callerBuyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.");
            }

            var payment = await _db.OrderPayments
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

            if (payment == null)
            {
                throw new PaymentOperationException(409, "This order has no captured payment to refund.");
            }

            var existingRefund = await _db.PaymentRefunds
                .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, cancellationToken);

            if (existingRefund != null)
            {
                if (existingRefund.OrderPaymentId != payment.Id)
                {
                    throw new PaymentOperationException(
                        409,
                        "That idempotencyKey has already been used for a different refund.");
                }

                return BuildRefundResult(order, payment, existingRefund);
            }

            if (payment.Status is not (PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded))
            {
                throw new PaymentOperationException(
                    409,
                    "Only a captured payment can be refunded. Fulfil the order first.");
            }

            if (!payment.CapturedAmountInCents.HasValue
                || string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                throw new PaymentOperationException(
                    409,
                    "This order does not have a captured Finix transfer to refund.");
            }

            var capturedAmount = payment.CapturedAmountInCents.Value;
            var alreadyRefunded = await _db.PaymentRefunds
                .Where(r => r.OrderPaymentId == payment.Id)
                .SumAsync(r => r.AmountInCents, cancellationToken);
            var remaining = capturedAmount - alreadyRefunded;

            if (remaining <= 0)
            {
                throw new PaymentOperationException(409, "This payment has already been fully refunded.");
            }

            long requestedAmount;
            if (amount.HasValue)
            {
                if (amount.Value <= 0)
                {
                    throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
                }

                requestedAmount = ToCents(amount.Value);
            }
            else
            {
                requestedAmount = remaining;
            }

            if (requestedAmount > remaining)
            {
                throw new PaymentOperationException(
                    400,
                    $"The refund amount {FormatCents(requestedAmount)} exceeds the remaining captured amount "
                    + $"{FormatCents(remaining)}.");
            }

            var captureTransferId = payment.FinixCaptureTransferId;
            var isPartial = requestedAmount < capturedAmount;

            var transfer = await GetTransferWithRetryAsync(
                captureTransferId,
                isPartial
                    ? TimeSpan.FromSeconds(Math.Max(0, _settings.RefundSettlementTimeoutSeconds))
                    : TimeSpan.Zero,
                cancellationToken);

            if (!string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                payment.RefreshCapture(
                    transfer.State ?? payment.CaptureState ?? "UNKNOWN",
                    transfer.Amount,
                    transfer.Fee);
                await _db.SaveChangesAsync(cancellationToken);
            }
            else if (isPartial)
            {
                throw new PaymentOperationException(
                    409,
                    "The capture is still pending settlement at Finix, so a partial refund cannot be processed yet. "
                    + "Retry shortly, or refund the full captured amount.",
                    "CAPTURE_PENDING");
            }

            FinixTransfer reversal;
            try
            {
                reversal = await _finix.CreateReversalAsync(
                    captureTransferId,
                    requestedAmount,
                    idempotencyKey,
                    cancellationToken);
            }
            catch (FinixApiException ex) when (ex.StatusCode == 422 && !string.IsNullOrWhiteSpace(ex.TransferId))
            {
                // Finix can return the existing reversal on a duplicate idempotency key.
                // Recover it so the caller still gets the original refund id.
                var duplicate = await _finix.GetTransferAsync(ex.TransferId, cancellationToken);
                var recovered = new PaymentRefund(
                    payment.Id,
                    duplicate.Id,
                    idempotencyKey,
                    duplicate.Amount,
                    duplicate.State ?? "UNKNOWN");
                _db.PaymentRefunds.Add(recovered);
                payment.AddRefund(recovered);
                await _db.SaveChangesAsync(cancellationToken);
                return BuildRefundResult(order, payment, recovered);
            }
            catch (FinixApiException ex)
            {
                throw MapFinixException(ex, "refund the payment");
            }

            var refund = new PaymentRefund(
                payment.Id,
                reversal.Id,
                idempotencyKey,
                reversal.Amount,
                reversal.State ?? "UNKNOWN");
            _db.PaymentRefunds.Add(refund);
            payment.AddRefund(refund);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refunded {AmountInCents} cents on order {OrderId} with Finix transfer {RefundTransferId}.",
                refund.AmountInCents,
                order.Id,
                refund.FinixTransferId);

            return BuildRefundResult(order, payment, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The caller could not be identified from the token.");
        }

        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(cancellationToken);

        var paymentIds = payments.Select(p => p.Id).ToList();
        var refunds = await _db.PaymentRefunds
            .Where(r => paymentIds.Contains(r.OrderPaymentId))
            .ToListAsync(cancellationToken);

        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var refundsByPayment = refunds
            .GroupBy(r => r.OrderPaymentId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PaymentRefund>)g.ToList());

        var result = new List<OrderDto>(orders.Count);
        foreach (var order in orders)
        {
            if (paymentsByOrder.TryGetValue(order.Id, out var payment))
            {
                refundsByPayment.TryGetValue(payment.Id, out var paymentRefunds);
                result.Add(BuildOrderDto(order, payment, paymentRefunds ?? Array.Empty<PaymentRefund>()));
            }
            else
            {
                var synthetic = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), Currency);
                result.Add(BuildOrderDto(order, synthetic, Array.Empty<PaymentRefund>()));
            }
        }

        return result;
    }

    public async Task<ReconciliationReportDto> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "The 'from' date-time must be before or equal to 'to'.");
        }

        var finixTransfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        var payments = await _db.OrderPayments.ToListAsync(cancellationToken);
        var refunds = await _db.PaymentRefunds.ToListAsync(cancellationToken);

        var finixById = finixTransfers.ToDictionary(t => t.Id);
        var report = new ReconciliationReportDto { From = from, To = to };

        report.FinixTransactions = finixTransfers
            .Select(t => new ReconciliationTransactionDto
            {
                Id = t.Id,
                Type = t.Type,
                State = t.State,
                AmountInCents = t.Amount,
                FeeInCents = t.Fee,
                Currency = t.Currency,
                CreatedAt = t.CreatedAt,
                Source = t.Source,
                ParentTransfer = t.ParentTransfer,
                IdempotencyId = t.IdempotencyId,
                Tags = t.Tags
            })
            .ToList();

        foreach (var transfer in finixTransfers)
        {
            var capturePayment = payments.FirstOrDefault(p => p.FinixCaptureTransferId == transfer.Id);
            if (capturePayment != null)
            {
                report.Matched.Add(new ReconciliationMatchDto
                {
                    Kind = "capture",
                    OrderId = capturePayment.OrderId,
                    FinixTransactionId = transfer.Id,
                    AmountInCents = transfer.Amount,
                    EshopState = capturePayment.Status.ToString()
                });
                continue;
            }

            var refund = refunds.FirstOrDefault(r => r.FinixTransferId == transfer.Id);
            if (refund != null)
            {
                var refundPayment = payments.FirstOrDefault(p => p.Id == refund.OrderPaymentId);
                report.Matched.Add(new ReconciliationMatchDto
                {
                    Kind = "refund",
                    OrderId = refundPayment?.OrderId ?? 0,
                    FinixTransactionId = transfer.Id,
                    AmountInCents = transfer.Amount,
                    EshopState = refundPayment?.Status.ToString()
                });
                continue;
            }

            report.UnmatchedFinixTransactions.Add(new ReconciliationMismatchDto
            {
                Kind = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                    ? "refund"
                    : "capture",
                FinixTransactionId = transfer.Id,
                AmountInCents = transfer.Amount,
                CreatedAt = transfer.CreatedAt,
                Detail = "Finix has this transaction but eShopOnWeb has no matching order payment or refund."
            });
        }

        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId)
                && payment.CapturedAt.HasValue
                && payment.CapturedAt.Value >= from
                && payment.CapturedAt.Value <= to
                && !finixById.ContainsKey(payment.FinixCaptureTransferId))
            {
                report.UnmatchedEshopRecords.Add(new ReconciliationMismatchDto
                {
                    Kind = "capture",
                    OrderId = payment.OrderId,
                    FinixTransactionId = payment.FinixCaptureTransferId,
                    AmountInCents = payment.CapturedAmountInCents ?? payment.AmountInCents,
                    CreatedAt = payment.CapturedAt,
                    Detail = "eShopOnWeb has a capture but Finix did not list the transaction in this range."
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (refund.CreatedAt >= from
                && refund.CreatedAt <= to
                && !finixById.ContainsKey(refund.FinixTransferId))
            {
                var refundPayment = payments.FirstOrDefault(p => p.Id == refund.OrderPaymentId);
                report.UnmatchedEshopRecords.Add(new ReconciliationMismatchDto
                {
                    Kind = "refund",
                    OrderId = refundPayment?.OrderId,
                    FinixTransactionId = refund.FinixTransferId,
                    AmountInCents = refund.AmountInCents,
                    CreatedAt = refund.CreatedAt,
                    Detail = "eShopOnWeb has a refund but Finix did not list the transaction in this range."
                });
            }
        }

        return report;
    }

    public async Task<SavedCardDto> SavePaymentMethodAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken)
    {
        if (card == null)
        {
            throw new PaymentOperationException(400, "Card details are required.");
        }

        ValidateCard(card);
        NormalizeCard(card);

        var identityId = await GetOrCreateIdentityAsync(buyerId, card, cancellationToken);
        FinixPaymentInstrument instrument;
        try
        {
            instrument = await _finix.CreateCardPaymentInstrumentAsync(
                identityId,
                card,
                buyerId,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw MapFinixException(ex, "save the card");
        }

        var savedCard = new SavedCard(
            buyerId,
            instrument.Id,
            instrument.Brand ?? "UNKNOWN",
            instrument.Last4 ?? "0000",
            instrument.ExpirationMonth > 0 ? instrument.ExpirationMonth : card.ExpirationMonth,
            instrument.ExpirationYear > 0 ? instrument.ExpirationYear : card.ExpirationYear,
            card.Name);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Saved card {PaymentMethodId} ({Brand} ending {Last4}) for buyer {BuyerId}.",
            savedCard.Id,
            savedCard.Brand,
            savedCard.Last4,
            buyerId);

        return ToSavedCardDto(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardDto>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId && c.IsEnabled)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(ToSavedCardDto).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var card = await _db.SavedCards.FirstOrDefaultAsync(
            c => c.Id == paymentMethodId && c.BuyerId == buyerId,
            cancellationToken);

        if (card == null)
        {
            throw new PaymentOperationException(404, "The saved payment method was not found for this shopper.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404)
        {
            _logger.LogInformation(
                "Finix payment instrument {PaymentInstrumentId} was already absent while deleting saved card {PaymentMethodId}.",
                card.FinixPaymentInstrumentId,
                card.Id);
        }
        catch (FinixApiException ex)
        {
            // The local disable is authoritative for this application: even if Finix is
            // temporarily unavailable, the card must stop being usable through the API.
            _logger.LogWarning(
                "Finix could not disable payment instrument {PaymentInstrumentId}: {Message}. "
                + "The saved card is still being removed locally.",
                card.FinixPaymentInstrumentId,
                ex.Message);
        }

        card.Disable();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> GetOrCreateIdentityAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken)
    {
        var profile = await _db.CustomerPaymentProfiles
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);

        if (profile != null)
        {
            return profile.FinixIdentityId;
        }

        var (firstName, lastName) = SplitName(card.Name);
        var email = buyerId.Contains('@') ? buyerId : null;

        FinixIdentity identity;
        try
        {
            identity = await _finix.CreateBuyerIdentityAsync(
                buyerId,
                email,
                firstName,
                lastName,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw MapFinixException(ex, "create the shopper's Finix identity");
        }

        var newProfile = new CustomerPaymentProfile(buyerId, identity.Id);
        _db.CustomerPaymentProfiles.Add(newProfile);
        await _db.SaveChangesAsync(cancellationToken);
        return identity.Id;
    }

    private async Task<FinixTransfer> GetTransferWithRetryAsync(
        string transferId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    private static Order EnsureBuyerOwnsOrder(Order? order, string buyerId)
    {
        if (order == null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, "Order was not found for this shopper.");
        }

        return order;
    }

    private async Task<OrderPayment> GetOrCreatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _db.OrderPayments
            .FirstOrDefaultAsync(p => p.OrderId == order.Id, cancellationToken);

        if (payment != null)
        {
            return payment;
        }

        payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), Currency);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);
        return payment;
    }

    private async Task<IReadOnlyList<PaymentRefund>> LoadRefundsAsync(
        int orderPaymentId,
        CancellationToken cancellationToken)
    {
        return await _db.PaymentRefunds
            .Where(r => r.OrderPaymentId == orderPaymentId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private static OrderDto BuildOrderDto(
        Order order,
        OrderPayment payment,
        IReadOnlyList<PaymentRefund> refunds)
    {
        var captured = payment.CapturedAmountInCents;
        var refunded = refunds.Sum(r => r.AmountInCents);

        var paymentDto = new PaymentDto
        {
            Status = payment.Status.ToString(),
            AmountInCents = payment.AmountInCents,
            Amount = FromCents(payment.AmountInCents),
            Currency = payment.Currency,
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureTransferId = payment.FinixCaptureTransferId,
            CaptureState = payment.CaptureState,
            CapturedAmountInCents = captured,
            CapturedAmount = captured.HasValue ? FromCents(captured.Value) : null,
            FinixFeeInCents = payment.FinixFeeInCents,
            FinixFee = payment.FinixFeeInCents.HasValue ? FromCents(payment.FinixFeeInCents.Value) : null,
            NetAmountInCents = payment.NetAmountInCents,
            NetAmount = payment.NetAmountInCents.HasValue ? FromCents(payment.NetAmountInCents.Value) : null,
            RefundedAmountInCents = refunded,
            RefundedAmount = FromCents(refunded),
            RefundableAmountInCents = captured.HasValue ? Math.Max(0, captured.Value - refunded) : null,
            RefundableAmount = captured.HasValue ? FromCents(Math.Max(0, captured.Value - refunded)) : null,
            Refunds = refunds.Select(ToRefundDto).ToList(),
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage,
            AuthorizedAt = payment.AuthorizedAt,
            CapturedAt = payment.CapturedAt,
            CanceledAt = payment.CanceledAt
        };

        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            AmountInCents = payment.AmountInCents,
            Items = order.OrderItems.Select(item => new OrderItemDto
            {
                CatalogItemId = item.ItemOrdered.CatalogItemId,
                ProductName = item.ItemOrdered.ProductName,
                UnitPrice = item.UnitPrice,
                Quantity = item.Units,
                LineTotal = item.UnitPrice * item.Units
            }).ToList(),
            Payment = paymentDto
        };
    }

    private static RefundDto ToRefundDto(PaymentRefund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixTransferId,
            IdempotencyKey = refund.IdempotencyKey,
            AmountInCents = refund.AmountInCents,
            Amount = FromCents(refund.AmountInCents),
            State = refund.State,
            CreatedAt = refund.CreatedAt
        };
    }

    private static RefundResult BuildRefundResult(
        Order order,
        OrderPayment payment,
        PaymentRefund refund)
    {
        var captured = payment.CapturedAmountInCents ?? 0;
        var refundable = Math.Max(0, captured - payment.RefundedAmountInCents);

        return new RefundResult
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixTransferId,
            Status = refund.State,
            AmountInCents = refund.AmountInCents,
            Amount = FromCents(refund.AmountInCents),
            RefundedAmountInCents = payment.RefundedAmountInCents,
            RefundableAmountInCents = refundable,
            OrderId = order.Id,
            PaymentStatus = payment.Status.ToString()
        };
    }

    private static SavedCardDto ToSavedCardDto(SavedCard card)
    {
        return new SavedCardDto
        {
            PaymentMethodId = card.Id,
            Brand = card.Brand,
            Last4 = card.Last4,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CardholderName = card.CardholderName,
            Enabled = card.IsEnabled,
            CreatedAt = card.CreatedAt
        };
    }

    private static Address BuildAddress(ShippingAddressInput? input)
    {
        return new Address(
            string.IsNullOrWhiteSpace(input?.Street) ? "N/A" : input!.Street!,
            string.IsNullOrWhiteSpace(input?.City) ? "N/A" : input!.City!,
            input?.State ?? string.Empty,
            string.IsNullOrWhiteSpace(input?.Country) ? "USA" : input!.Country!,
            string.IsNullOrWhiteSpace(input?.ZipCode) ? "00000" : input!.ZipCode!);
    }

    private static void ValidateCard(CardInput card)
    {
        var digits = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentOperationException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "Expiration month must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year
            || (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentOperationException(400, "The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode)
            && (card.SecurityCode.Length is < 3 or > 4 || !card.SecurityCode.All(char.IsDigit)))
        {
            throw new PaymentOperationException(400, "The security code must be 3 or 4 digits.");
        }

        if (card.BillingAddress != null)
        {
            var address = card.BillingAddress;
            if (string.IsNullOrWhiteSpace(address.Line1)
                || string.IsNullOrWhiteSpace(address.City)
                || string.IsNullOrWhiteSpace(address.Region)
                || string.IsNullOrWhiteSpace(address.PostalCode)
                || string.IsNullOrWhiteSpace(address.Country))
            {
                throw new PaymentOperationException(
                    400,
                    "A billing address, when supplied, needs line1, city, region, postalCode and country.");
            }
        }
    }

    private static void NormalizeCard(CardInput card)
    {
        card.Number = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        card.Name = card.Name?.Trim();
        card.SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode)
            ? null
            : card.SecurityCode.Trim();
    }

    private static (string? FirstName, string? LastName) SplitName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, null);
        }

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return (parts[0], null);
        }

        return (parts[0], string.Join(' ', parts.Skip(1)));
    }

    private static PaymentOperationException MapFinixException(FinixApiException ex, string operation)
    {
        var statusCode = ex.StatusCode switch
        {
            400 or 422 => 400,
            402 => 402,
            404 => 404,
            409 => 409,
            503 => 503,
            _ => 502
        };

        return new PaymentOperationException(
            statusCode,
            $"Finix could not {operation}: {ex.Message}",
            ex.Code);
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static decimal FromCents(long cents)
    {
        return cents / 100m;
    }

    private static string FormatCents(long cents)
    {
        return (cents / 100m).ToString("0.00");
    }

    private static SemaphoreSlim GetOrderLock(int orderId)
    {
        return OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
    }
}
