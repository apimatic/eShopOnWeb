using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly IFinixGateway _finix;
    private readonly FinixSettings _settings;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(CatalogContext db, IFinixGateway finix, IOptions<FinixSettings> settings, ILogger<PaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentException(400, "At least one catalog item is required.");
        }

        var requestedItems = request.Items
            .Where(i => i.CatalogItemId > 0 && i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (requestedItems.Count == 0)
        {
            throw new PaymentException(400, "Every order item needs a positive catalog item id and quantity.");
        }

        var catalogItemIds = requestedItems.Select(i => i.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
        var catalogById = catalogItems.ToDictionary(i => i.Id);

        var missing = catalogItemIds.Where(id => !catalogById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requested in requestedItems)
        {
            var catalogItem = catalogById[requested.CatalogItemId];
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                requested.Quantity));
        }

        var order = new Order(buyerId, ToOrderAddress(request.ShipToAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var totalCents = ToCents(order.Total());
        var payment = new OrderPayment
        {
            OrderId = order.Id,
            BuyerId = buyerId,
            Currency = _settings.Currency,
            AmountCents = totalCents,
            Status = PaymentStatus.AwaitingPayment
        };
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return MapOrder(order, payment);
    }

    public Task<OrderDto> PayOrderAsync(string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var (order, payment) = await LoadOrderAsync(orderId, buyerId, cancellationToken);
            if (payment is null)
            {
                throw new PaymentException(409, $"Order {orderId} has no payment record.");
            }

            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return MapOrder(order, payment);
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw new PaymentException(409, $"Order {orderId} has been canceled and cannot be paid.");
            }

            var (source, identityId, securityCode) = await ResolvePaymentSourceAsync(buyerId, request, cancellationToken);
            payment.CustomerIdentityId = identityId;
            payment.PaymentInstrumentId = source;

            var idempotencyId = $"eshop-{payment.Id:N}-auth-{payment.PaymentAttempts}";
            Authorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    payment.AmountCents,
                    source,
                    idempotencyId,
                    orderId,
                    securityCode,
                    cancellationToken);
            }
            catch (ApiException ex)
            {
                var recovered = await TryRecoverAuthorizationAsync(idempotencyId, cancellationToken);
                if (recovered is null)
                {
                    payment.Status = PaymentStatus.PaymentFailed;
                    payment.FailureCode = ex.StatusCode.ToString();
                    payment.FailureMessage = ex.Message;
                    payment.UpdatedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentException(402, $"Finix could not authorize order {orderId}: {ex.Message}", "AUTHORIZATION_FAILED");
                }

                authorization = recovered;
            }

            var state = authorization.State?.Value;
            payment.AuthorizationId = authorization.Id;
            payment.AuthorizationState = state;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(state, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = PaymentStatus.PaymentFailed;
                payment.PaymentAttempts++;
                payment.FailureCode = authorization.FailureCode;
                payment.FailureMessage = authorization.FailureMessage ?? $"Authorization ended in state {state}.";
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(402, $"Finix declined the payment for order {orderId}: {payment.FailureMessage}", payment.FailureCode ?? "AUTHORIZATION_FAILED");
            }

            payment.Status = PaymentStatus.Authorized;
            payment.AuthorizedAt = DateTimeOffset.UtcNow;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            await _db.SaveChangesAsync(cancellationToken);

            return MapOrder(order, payment);
        });

    public Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var (order, payment) = await LoadOrderAsync(orderId, null, cancellationToken);
            if (payment is null)
            {
                throw new PaymentException(404, $"Order {orderId} has no payment record.");
            }

            if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return MapOrder(order, payment);
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw new PaymentException(409, $"Order {orderId} has been canceled and cannot be fulfilled.");
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                throw new PaymentException(409, $"Order {orderId} has no capturable authorization (current payment state: {payment.Status}). Ask the shopper to pay before fulfilling.");
            }

            Authorization authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentException(409, $"Could not read authorization {payment.AuthorizationId} for order {orderId}: {ex.Message}. The hold may have expired; cancel the order or ask the shopper to pay again.", "AUTHORIZATION_UNREADABLE");
            }

            var authorizationState = authorization.State?.Value;
            payment.AuthorizationState = authorizationState;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                Transfer existingTransfer;
                try
                {
                    existingTransfer = await _finix.GetTransferAsync(authorization.Transfer, cancellationToken);
                }
                catch (ApiException ex)
                {
                    throw new PaymentException(502, $"Authorization {payment.AuthorizationId} for order {orderId} already produced transfer {authorization.Transfer}, but the transfer could not be read: {ex.Message}", "TRANSFER_UNREADABLE");
                }

                payment.TransferId = authorization.Transfer;
                payment.TransferState = existingTransfer.State?.Value;
                payment.CapturedAmountCents = existingTransfer.Amount ?? payment.AmountCents;
                payment.FeeCents = existingTransfer.Fee ?? 0;
                payment.NetProceedsCents = payment.CapturedAmountCents - payment.FeeCents;
                payment.UpdatedAt = DateTimeOffset.UtcNow;

                if (string.Equals(payment.TransferState, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(payment.TransferState, "CANCELED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.Status = PaymentStatus.PaymentFailed;
                    payment.FailureCode = existingTransfer.FailureCode ?? "CAPTURE_FAILED";
                    payment.FailureMessage = existingTransfer.FailureMessage ?? $"Finix capture transfer ended in state {payment.TransferState}.";
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentException(502, $"Finix could not complete the capture for order {orderId}: {payment.FailureMessage}", payment.FailureCode);
                }

                payment.Status = PaymentStatus.Fulfilled;
                payment.FulfilledAt = DateTimeOffset.UtcNow;
                payment.FailureCode = null;
                payment.FailureMessage = null;
                await _db.SaveChangesAsync(cancellationToken);

                return MapOrder(order, payment);
            }

            if (!string.Equals(authorizationState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(authorizationState, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(409, $"Authorization {payment.AuthorizationId} for order {orderId} is in state {authorizationState} and can no longer be captured. Cancel the order to release any remaining hold, or ask the shopper to pay again.", "AUTHORIZATION_NOT_CAPTURABLE");
            }

            Authorization captured;
            try
            {
                captured = await _finix.CaptureAuthorizationAsync(payment.AuthorizationId, payment.AmountCents, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentException(409, $"Capture failed for authorization {payment.AuthorizationId} on order {orderId}: {ex.Message}. The hold may have expired or been voided. Cancel the order or ask the shopper to pay again.", "CAPTURE_FAILED");
            }

            if (string.IsNullOrWhiteSpace(captured.Transfer))
            {
                throw new PaymentException(502, $"Finix captured order {orderId} but did not return a transfer id.", "MISSING_TRANSFER");
            }

            Transfer transfer;
            try
            {
                transfer = await WaitForTransferAsync(captured.Transfer, TimeSpan.FromSeconds(8), cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentException(502, $"Finix captured order {orderId} but the capture transfer {captured.Transfer} could not be read: {ex.Message}", "TRANSFER_UNREADABLE");
            }

            payment.TransferId = captured.Transfer;
            payment.TransferState = transfer.State?.Value;
            payment.CapturedAmountCents = transfer.Amount ?? payment.AmountCents;
            payment.FeeCents = transfer.Fee ?? 0;
            payment.NetProceedsCents = payment.CapturedAmountCents - payment.FeeCents;
            payment.UpdatedAt = DateTimeOffset.UtcNow;

            if (string.Equals(payment.TransferState, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payment.TransferState, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                payment.Status = PaymentStatus.PaymentFailed;
                payment.FailureCode = transfer.FailureCode ?? "CAPTURE_FAILED";
                payment.FailureMessage = transfer.FailureMessage ?? $"Finix capture transfer ended in state {payment.TransferState}.";
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(502, $"Finix could not complete the capture for order {orderId}: {payment.FailureMessage}", payment.FailureCode);
            }

            payment.Status = PaymentStatus.Fulfilled;
            payment.FulfilledAt = DateTimeOffset.UtcNow;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            await _db.SaveChangesAsync(cancellationToken);

            return MapOrder(order, payment);
        });

    public Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var (order, payment) = await LoadOrderAsync(orderId, null, cancellationToken);
            if (payment is null)
            {
                throw new PaymentException(404, $"Order {orderId} has no payment record.");
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                return MapOrder(order, payment);
            }

            if (payment.Status is PaymentStatus.Fulfilled or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentException(409, $"Order {orderId} has already been fulfilled. Use the refunds endpoint to return captured funds.");
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                try
                {
                    var current = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    payment.AuthorizationState = current.State?.Value;
                    if (current.IsVoid != true)
                    {
                        var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                        payment.AuthorizationState = voided.State?.Value;
                    }
                }
                catch (ApiException ex)
                {
                    throw new PaymentException(409, $"Could not release the hold on authorization {payment.AuthorizationId} for order {orderId}: {ex.Message}", "VOID_FAILED");
                }
            }

            payment.Status = PaymentStatus.Canceled;
            payment.CanceledAt = DateTimeOffset.UtcNow;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return MapOrder(order, payment);
        });

    public Task<RefundDto> RefundOrderAsync(string buyerId, int orderId, RefundOrderRequest request, CancellationToken cancellationToken) =>
        WithRefundLockAsync(orderId, async () =>
        {
            var (_, payment) = await LoadOrderAsync(orderId, buyerId, cancellationToken);
            if (payment is null)
            {
                throw new PaymentException(404, $"Order {orderId} has no payment record.");
            }

            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                throw new PaymentException(400, "An idempotencyKey is required for refunds.");
            }

            var existing = await _db.RefundRecords
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.OrderPaymentId == payment.Id && r.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (existing is not null)
            {
                return MapRefund(existing, payment.Currency);
            }

            if (payment.Status is not PaymentStatus.Fulfilled and not PaymentStatus.PartiallyRefunded)
            {
                throw new PaymentException(409, $"Order {orderId} has not been fulfilled and captured, so there is nothing to refund (current payment state: {payment.Status}).");
            }

            if (string.IsNullOrWhiteSpace(payment.TransferId) || payment.CapturedAmountCents is null or <= 0)
            {
                throw new PaymentException(409, $"Order {orderId} has no captured transfer to refund.");
            }

            var capturedCents = payment.CapturedAmountCents.Value;
            var remainingCents = capturedCents - payment.RefundedAmountCents;
            var amountCents = request.AmountCents.HasValue
                ? request.AmountCents.Value
                : request.Amount.HasValue
                    ? ToCents(request.Amount.Value)
                    : remainingCents;

            if (amountCents <= 0)
            {
                throw new PaymentException(400, "The refund amount must be greater than zero.");
            }

            if (amountCents > remainingCents)
            {
                throw new PaymentException(400, $"The refund amount {FormatMoney(amountCents)} exceeds the remaining refundable amount {FormatMoney(remainingCents)} for order {orderId}.");
            }

            var isPartial = amountCents < capturedCents;
            if (isPartial && !string.Equals(payment.TransferState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                Transfer settled;
                try
                {
                    settled = await WaitForTransferAsync(payment.TransferId, TimeSpan.FromSeconds(30), cancellationToken);
                }
                catch (ApiException ex)
                {
                    throw new PaymentException(409, $"The captured transfer for order {orderId} could not be read before refunding: {ex.Message}", "TRANSFER_UNREADABLE");
                }

                payment.TransferState = settled.State?.Value;
                if (settled.Amount.HasValue)
                {
                    payment.CapturedAmountCents = settled.Amount;
                    capturedCents = settled.Amount.Value;
                }
                if (settled.Fee.HasValue)
                {
                    payment.FeeCents = settled.Fee;
                }
                if (payment.CapturedAmountCents.HasValue && payment.FeeCents.HasValue)
                {
                    payment.NetProceedsCents = payment.CapturedAmountCents - payment.FeeCents;
                }
                payment.UpdatedAt = DateTimeOffset.UtcNow;

                if (!string.Equals(payment.TransferState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentException(409, $"The payment for order {orderId} is still settling (Finix transfer state: {payment.TransferState}). Partial refunds are available once the transfer succeeds; retry shortly or refund the full amount.", "TRANSFER_NOT_SETTLED");
                }
            }

            Transfer refund;
            try
            {
                refund = await _finix.CreateRefundAsync(payment.TransferId, amountCents, request.IdempotencyKey, cancellationToken);
            }
            catch (ApiException ex)
            {
                var recovered = await TryRecoverRefundAsync(payment.TransferId, request.IdempotencyKey, cancellationToken);
                if (recovered is null)
                {
                    throw new PaymentException(409, $"Finix could not refund {FormatMoney(amountCents)} for order {orderId}: {ex.Message}", "REFUND_FAILED");
                }

                refund = recovered;
            }

            var state = refund.State?.Value;
            var record = new RefundRecord
            {
                OrderPaymentId = payment.Id,
                OrderId = orderId,
                BuyerId = payment.BuyerId,
                IdempotencyKey = request.IdempotencyKey,
                AmountCents = amountCents,
                FinixTransferId = refund.Id,
                FinixState = state,
                Status = string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ? "Succeeded" : "Pending",
                CreatedAt = DateTimeOffset.UtcNow
            };

            if (string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                record.Status = "Failed";
                record.FailureCode = refund.FailureCode ?? "REFUND_FAILED";
                record.FailureMessage = refund.FailureMessage ?? "Finix reported the refund transfer as failed.";
                _db.RefundRecords.Add(record);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentException(502, $"Finix reported the refund for order {orderId} as failed: {record.FailureMessage}", record.FailureCode);
            }

            _db.RefundRecords.Add(record);
            payment.RefundedAmountCents += amountCents;
            payment.Status = payment.RefundedAmountCents >= capturedCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return MapRefund(record, payment.Currency);
        });

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(o => MapOrder(o, paymentsByOrder.TryGetValue(o.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<ReconciliationDto> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentException(400, "The 'from' date must be on or before the 'to' date.");
        }

        var authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);

        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var paymentsByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, StringComparer.Ordinal);
        var paymentsByTransfer = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.TransferId))
            .ToDictionary(p => p.TransferId!, StringComparer.Ordinal);
        var paymentsByRefund = payments
            .SelectMany(p => p.Refunds.Select(r => new { Payment = p, r.FinixTransferId }))
            .Where(x => !string.IsNullOrWhiteSpace(x.FinixTransferId))
            .GroupBy(x => x.FinixTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Payment, StringComparer.Ordinal);

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .ToListAsync(cancellationToken);

        var finixTransactions = new List<FinixTransactionDto>();

        foreach (var authorization in authorizations)
        {
            int? matchedOrderId = null;
            string? matchSource = null;

            if (!string.IsNullOrWhiteSpace(authorization.Id) && paymentsByAuthorization.TryGetValue(authorization.Id, out var payment))
            {
                matchedOrderId = payment.OrderId;
                matchSource = "authorization_id";
            }
            else if (TryGetTagOrderId(authorization.Tags, out var taggedOrderId))
            {
                matchedOrderId = taggedOrderId;
                matchSource = "tag_order_id";
            }

            finixTransactions.Add(new FinixTransactionDto
            {
                Kind = "Authorization",
                Id = authorization.Id ?? string.Empty,
                Type = "AUTHORIZATION",
                State = authorization.State?.Value,
                AmountCents = authorization.Amount,
                Amount = ToMoney(authorization.Amount),
                Currency = authorization.Currency?.Value,
                CreatedAt = authorization.CreatedAt,
                MatchedOrderId = matchedOrderId,
                MatchSource = matchSource
            });
        }

        foreach (var transfer in transfers)
        {
            int? matchedOrderId = null;
            string? matchSource = null;

            if (!string.IsNullOrWhiteSpace(transfer.Id) && paymentsByTransfer.TryGetValue(transfer.Id, out var payment))
            {
                matchedOrderId = payment.OrderId;
                matchSource = "transfer_id";
            }
            else if (!string.IsNullOrWhiteSpace(transfer.Id) && paymentsByRefund.TryGetValue(transfer.Id, out var refundPayment))
            {
                matchedOrderId = refundPayment.OrderId;
                matchSource = "refund_transfer_id";
            }
            else if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer) && paymentsByTransfer.TryGetValue(transfer.ParentTransfer, out var parentPayment))
            {
                matchedOrderId = parentPayment.OrderId;
                matchSource = "parent_transfer_id";
            }
            else if (TryGetTagOrderId(transfer.Tags, out var taggedOrderId))
            {
                matchedOrderId = taggedOrderId;
                matchSource = "tag_order_id";
            }

            finixTransactions.Add(new FinixTransactionDto
            {
                Kind = "Transfer",
                Id = transfer.Id ?? string.Empty,
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                AmountCents = transfer.Amount,
                Amount = ToMoney(transfer.Amount),
                Currency = transfer.Currency?.Value,
                FeeCents = transfer.Fee,
                Fee = ToMoney(transfer.Fee),
                NetCents = transfer.Amount.HasValue && transfer.Fee.HasValue ? transfer.Amount - transfer.Fee : null,
                Net = ToMoney(transfer.Amount.HasValue && transfer.Fee.HasValue ? transfer.Amount - transfer.Fee : null),
                CreatedAt = transfer.CreatedAt,
                ParentId = transfer.ParentTransfer,
                MatchedOrderId = matchedOrderId,
                MatchSource = matchSource
            });
        }

        var eshopOrders = new List<ReconciliationOrderDto>();
        foreach (var order in orders)
        {
            paymentsByOrderLookup(payments, order.Id, out var payment);
            var matchedIds = finixTransactions
                .Where(t => t.MatchedOrderId == order.Id)
                .Select(t => t.Id)
                .ToList();

            eshopOrders.Add(new ReconciliationOrderDto
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Currency = payment?.Currency ?? _settings.Currency,
                PaymentStatus = payment?.Status.ToString() ?? "Unknown",
                AuthorizationId = payment?.AuthorizationId,
                TransferId = payment?.TransferId,
                RefundIds = payment?.Refunds.Select(r => r.Id).ToList() ?? new List<Guid>(),
                MatchedFinixTransactionIds = matchedIds
            });
        }

        return new ReconciliationDto
        {
            From = from,
            To = to,
            FinixTransactionCount = finixTransactions.Count,
            EshopOrderCount = eshopOrders.Count,
            FinixTransactions = finixTransactions,
            EshopOrders = eshopOrders,
            UnmatchedFinix = finixTransactions.Where(t => t.MatchedOrderId is null).ToList(),
            UnmatchedEshop = eshopOrders.Where(o => o.MatchedFinixTransactionIds.Count == 0).ToList()
        };

        static void paymentsByOrderLookup(List<OrderPayment> allPayments, int orderId, out OrderPayment? payment)
        {
            payment = allPayments.FirstOrDefault(p => p.OrderId == orderId);
        }
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        ValidateCard(request.Number, request.ExpirationMonth, request.ExpirationYear);
        var profile = await EnsureCustomerProfileAsync(buyerId, cancellationToken);

        PaymentInstrumentCard paymentInstrument;
        try
        {
            paymentInstrument = await _finix.CreatePaymentInstrumentAsync(
                profile.FinixIdentityId,
                ToFinixCard(request.Number, request.ExpirationMonth, request.ExpirationYear, string.IsNullOrWhiteSpace(request.Cvc) ? request.Cvv ?? string.Empty : request.Cvc, request.Name, request.Address),
                cancellationToken);
        }
        catch (ApiException ex)
        {
            throw new PaymentException(402, $"Finix could not save the card: {ex.Message}", "PAYMENT_INSTRUMENT_FAILED");
        }

        if (string.IsNullOrWhiteSpace(paymentInstrument.Id))
        {
            throw new PaymentException(502, "Finix did not return a payment instrument id.", "PAYMENT_INSTRUMENT_FAILED");
        }

        var savedCard = new SavedCard
        {
            BuyerId = buyerId,
            CustomerIdentityId = profile.FinixIdentityId,
            FinixPaymentInstrumentId = paymentInstrument.Id,
            Brand = paymentInstrument.Brand?.Value,
            Last4 = paymentInstrument.LastFour,
            ExpirationMonth = request.ExpirationMonth,
            ExpirationYear = request.ExpirationYear,
            CardholderName = request.Name
        };

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return MapPaymentMethod(savedCard);
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        var cards = await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.BuyerId == buyerId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(MapPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, Guid paymentMethodId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "The caller is not authenticated.");
        }

        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.Id == paymentMethodId && c.BuyerId == buyerId && !c.IsDeleted, cancellationToken);
        if (card is null)
        {
            throw new PaymentException(404, $"Saved card {paymentMethodId} was not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(card.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("Finix could not disable payment instrument {PaymentInstrumentId}; removing it from the shopper's wallet anyway: {Message}", card.FinixPaymentInstrumentId, ex.Message);
        }

        card.IsDeleted = true;
        card.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Source, string IdentityId, string? SecurityCode)> ResolvePaymentSourceAsync(string buyerId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        var savedCardId = !string.IsNullOrWhiteSpace(request.PaymentMethodId)
            ? request.PaymentMethodId
            : !string.IsNullOrWhiteSpace(request.SavedPaymentMethodId)
                ? request.SavedPaymentMethodId
                : request.SavedCardId;
        var hasSavedCard = !string.IsNullOrWhiteSpace(savedCardId);
        var hasCard = request.Card is not null;

        if (hasSavedCard == hasCard)
        {
            throw new PaymentException(400, "Provide either a saved paymentMethodId or one-off card details, but not both.");
        }

        if (hasSavedCard)
        {
            if (!Guid.TryParse(savedCardId, out var paymentMethodId))
            {
                throw new PaymentException(400, "paymentMethodId is not a valid identifier.");
            }

            var savedCard = await _db.SavedCards
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == paymentMethodId && c.BuyerId == buyerId && !c.IsDeleted, cancellationToken);
            if (savedCard is null)
            {
                throw new PaymentException(404, $"Saved card {savedCardId} was not found.");
            }

            return (savedCard.FinixPaymentInstrumentId, savedCard.CustomerIdentityId, null);
        }

        var card = request.Card!;
        ValidateCard(card.Number, card.ExpirationMonth, card.ExpirationYear);
        var profile = await EnsureCustomerProfileAsync(buyerId, cancellationToken);

        PaymentInstrumentCard paymentInstrument;
        try
        {
            paymentInstrument = await _finix.CreatePaymentInstrumentAsync(
                profile.FinixIdentityId,
                ToFinixCard(card.Number, card.ExpirationMonth, card.ExpirationYear, string.IsNullOrWhiteSpace(card.Cvc) ? card.Cvv ?? string.Empty : card.Cvc, card.Name, card.Address),
                cancellationToken);
        }
        catch (ApiException ex)
        {
            throw new PaymentException(402, $"Finix could not tokenize the card: {ex.Message}", "PAYMENT_INSTRUMENT_FAILED");
        }

        if (string.IsNullOrWhiteSpace(paymentInstrument.Id))
        {
            throw new PaymentException(502, "Finix did not return a payment instrument id.", "PAYMENT_INSTRUMENT_FAILED");
        }

        return (paymentInstrument.Id, profile.FinixIdentityId, card.Cvc);
    }

    private async Task<CustomerProfile> EnsureCustomerProfileAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.CustomerProfiles.FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var gate = GetGate($"customer-{buyerId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.CustomerProfiles.FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            FinixApiReference.Models.Identity identity;
            try
            {
                identity = await _finix.CreateBuyerIdentityAsync(buyerId, cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentException(502, $"Finix could not create a buyer identity: {ex.Message}", "IDENTITY_FAILED");
            }

            var profile = new CustomerProfile
            {
                BuyerId = buyerId,
                FinixIdentityId = identity.Id!
            };
            _db.CustomerProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);
            return profile;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(Order Order, OrderPayment? Payment)> LoadOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(buyerId) && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        var payment = await _db.OrderPayments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

        return (order, payment);
    }

    private async Task<Authorization?> TryRecoverAuthorizationAsync(string idempotencyId, CancellationToken cancellationToken)
    {
        try
        {
            var matches = await _finix.FindAuthorizationsByIdempotencyIdAsync(idempotencyId, cancellationToken);
            return matches.FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogWarning("Could not recover authorization by idempotency id {IdempotencyId}: {Message}", idempotencyId, ex.Message);
            return null;
        }
    }

    private async Task<Transfer?> TryRecoverRefundAsync(string transferId, string idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.FindReversalByIdempotencyIdAsync(transferId, idempotencyKey, cancellationToken);
        }
        catch (Exception ex) when (ex is ApiException or SdkException)
        {
            _logger.LogWarning("Could not recover refund by idempotency key {IdempotencyKey}: {Message}", idempotencyKey, ex.Message);
            return null;
        }
    }

    private async Task<Transfer> WaitForTransferAsync(string transferId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);

        while (DateTimeOffset.UtcNow < deadline && IsTransferPending(transfer))
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static bool IsTransferPending(Transfer transfer) =>
        !string.Equals(transfer.State?.Value, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(transfer.State?.Value, "FAILED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(transfer.State?.Value, "CANCELED", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(transfer.State?.Value, "RETURNED", StringComparison.OrdinalIgnoreCase);

    private static Task<OrderDto> WithOrderLockAsync(int orderId, Func<Task<OrderDto>> action) =>
        WithGateAsync($"order-{orderId}", action);

    private static Task<RefundDto> WithRefundLockAsync(int orderId, Func<Task<RefundDto>> action) =>
        WithGateAsync($"order-{orderId}", action);

    private static async Task<T> WithGateAsync<T>(string key, Func<Task<T>> action)
    {
        var gate = GetGate(key);
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static SemaphoreSlim GetGate(string key) => Gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

    private static OrderAddress ToOrderAddress(AddressDto? address) =>
        new(
            string.IsNullOrWhiteSpace(address?.Street) ? "1 Main St" : address!.Street!,
            string.IsNullOrWhiteSpace(address?.City) ? "Seattle" : address!.City!,
            string.IsNullOrWhiteSpace(address?.State) ? "WA" : address!.State!,
            string.IsNullOrWhiteSpace(address?.Country) ? "USA" : address!.Country!,
            string.IsNullOrWhiteSpace(address?.ZipCode) ? "98101" : address!.ZipCode!);

    private static FinixCardInput ToFinixCard(string number, int month, int year, string cvc, string name, AddressDto? address) =>
        new(
            NormalizeCardNumber(number),
            month,
            year,
            cvc,
            name,
            address is null
                ? null
                : new FinixAddressInput(address.Street, null, address.City, address.State, address.ZipCode, address.Country));

    private static void ValidateCard(string number, int expirationMonth, int expirationYear)
    {
        var normalized = NormalizeCardNumber(number);
        if (normalized.Length is < 12 or > 19)
        {
            throw new PaymentException(400, "A valid card number is required.");
        }

        if (expirationMonth is < 1 or > 12)
        {
            throw new PaymentException(400, "expirationMonth must be between 1 and 12.");
        }

        if (expirationYear < DateTime.UtcNow.Year)
        {
            throw new PaymentException(400, "The card expiration year is in the past.");
        }
    }

    private static string NormalizeCardNumber(string number) =>
        new((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static long ToCents(decimal amount)
    {
        if (amount < 0)
        {
            throw new PaymentException(400, "Amounts cannot be negative.");
        }

        return (long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
    }

    private static decimal? ToMoney(long? cents) => cents.HasValue ? cents.Value / 100m : null;

    private static string FormatMoney(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static bool TryGetTagOrderId(object? tags, out int orderId)
    {
        orderId = 0;
        var value = GetTag(tags, "order_id");
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out orderId);
    }

    private static string? GetTag(object? tags, string key)
    {
        switch (tags)
        {
            case null:
                return null;
            case JsonElement element when element.ValueKind == JsonValueKind.Object:
                return element.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String
                    ? property.GetString()
                    : null;
            case IDictionary<string, string> stringDictionary when stringDictionary.TryGetValue(key, out var stringValue):
                return stringValue;
            case IDictionary<string, object?> objectDictionary when objectDictionary.TryGetValue(key, out var objectValue):
                return objectValue?.ToString();
            default:
                return null;
        }
    }

    private static OrderDto MapOrder(Order order, OrderPayment? payment)
    {
        var total = order.Total();
        var totalCents = payment?.AmountCents ?? ToCents(total);
        var currency = payment?.Currency ?? "USD";

        return new OrderDto
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Total = total,
            TotalCents = totalCents,
            Currency = currency,
            PaymentStatus = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
            AuthorizationId = payment?.AuthorizationId,
            AuthorizationState = payment?.AuthorizationState,
            TransferId = payment?.TransferId,
            TransferState = payment?.TransferState,
            CapturedAmount = ToMoney(payment?.CapturedAmountCents),
            CapturedAmountCents = payment?.CapturedAmountCents,
            FinixFee = ToMoney(payment?.FeeCents),
            FinixFeeCents = payment?.FeeCents,
            NetProceeds = ToMoney(payment?.NetProceedsCents),
            NetProceedsCents = payment?.NetProceedsCents,
            RefundedAmount = payment?.RefundedAmountCents / 100m ?? 0m,
            RefundedAmountCents = payment?.RefundedAmountCents ?? 0,
            FailureCode = payment?.FailureCode,
            FailureMessage = payment?.FailureMessage,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
                LineTotal = i.UnitPrice * i.Units
            }).ToList(),
            Refunds = payment?.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(r => MapRefund(r, currency))
                .ToList() ?? new List<RefundDto>()
        };
    }

    private static RefundDto MapRefund(RefundRecord refund, string currency) => new()
    {
        RefundId = refund.Id,
        OrderId = refund.OrderId,
        AmountCents = refund.AmountCents,
        Amount = refund.AmountCents / 100m,
        Currency = currency,
        Status = refund.Status,
        FinixTransferId = refund.FinixTransferId,
        FinixState = refund.FinixState,
        CreatedAt = refund.CreatedAt
    };

    private static PaymentMethodDto MapPaymentMethod(SavedCard card) => new()
    {
        PaymentMethodId = card.Id,
        Brand = card.Brand,
        Last4 = card.Last4,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear,
        CardholderName = card.CardholderName,
        CreatedAt = card.CreatedAt
    };
}
