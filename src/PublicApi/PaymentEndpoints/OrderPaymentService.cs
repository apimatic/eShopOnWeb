using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Finix;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan SettlementPollTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SettlementPollInterval = TimeSpan.FromSeconds(2);

    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly IOptions<FinixOptions> _finixOptions;
    private readonly PaymentOperationLock _operationLock;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(CatalogContext db, IFinixClient finix, IOptions<FinixOptions> finixOptions,
        PaymentOperationLock operationLock, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _finixOptions = finixOptions;
        _operationLock = operationLock;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        if (request.RequestedItems.Count == 0)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "At least one catalog item is required.");
        }

        var quantities = request.RequestedItems
            .GroupBy(i => i.RequestedCatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

        if (quantities.Any(q => q.Key <= 0 || q.Value <= 0))
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                "Catalog item ids and quantities must be greater than zero.");
        }

        var ids = quantities.Keys.ToList();
        var catalogItems = await _db.CatalogItems
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var missing = ids.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = quantities.Select(pair =>
        {
            var catalogItem = catalogItems.First(i => i.Id == pair.Key);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, pair.Value);
        }).ToList();

        var order = new Order(buyerId, BuildAddress(request.RequestedAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, ToCents(order.Total()), ResolveCurrency());
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        return MapOrder(order, payment);
    }

    public async Task<PaymentDto> AuthorizeAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        using var operation = await _operationLock.AcquireAsync($"order-{orderId}", cancellationToken);

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);

        if (order == null)
        {
            throw new PaymentException(StatusCodes.Status404NotFound, "Order not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            payment = new OrderPayment(order.Id, buyerId, ToCents(order.Total()), ResolveCurrency());
            _db.OrderPayments.Add(payment);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return MapPayment(payment);
        }

        if (payment.Status == PaymentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order was cancelled and can no longer be paid.");
        }

        if (payment.Status == PaymentStatus.PendingAuthorization &&
            !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            var existingAuthorization = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId!,
                cancellationToken);
            payment.UpdateAuthorizationState(existingAuthorization.State ?? "UNKNOWN",
                existingAuthorization.IsVoid, existingAuthorization.VoidState, DateTimeOffset.UtcNow);

            if (string.Equals(existingAuthorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorized(existingAuthorization.Id, existingAuthorization.State!,
                    payment.FinixPaymentInstrumentId!, payment.SavedPaymentMethodId, DateTimeOffset.UtcNow);
                order.MarkAuthorized();
                await _db.SaveChangesAsync(cancellationToken);
                return MapPayment(payment);
            }

            if (string.Equals(existingAuthorization.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                return MapPayment(payment);
            }

            payment.MarkAuthorizationFailed(existingAuthorization.FailureCode,
                existingAuthorization.FailureMessage, DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }

        if (request.Card != null && request.RequestedPaymentMethodId.HasValue)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                "Provide either card details or a saved paymentMethodId, not both.");
        }

        string source;
        int? savedPaymentMethodId = null;

        if (request.RequestedPaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods
                .FirstOrDefaultAsync(p => p.Id == request.RequestedPaymentMethodId.Value && p.BuyerId == buyerId,
                    cancellationToken);

            if (saved == null)
            {
                throw new PaymentException(StatusCodes.Status404NotFound, "Saved payment method not found.");
            }

            source = saved.FinixPaymentInstrumentId;
            savedPaymentMethodId = saved.Id;
        }
        else if (request.Card != null)
        {
            source = await CreatePaymentInstrumentFromCardAsync(buyerId, request.Card, cancellationToken);
        }
        else
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                "Provide card details or a saved paymentMethodId.");
        }

        string idempotencyId;
        if (payment.Status == PaymentStatus.PendingAuthorization &&
            !string.IsNullOrWhiteSpace(payment.AuthorizationIdempotencyId))
        {
            idempotencyId = payment.AuthorizationIdempotencyId!;
        }
        else
        {
            idempotencyId = payment.PrepareAuthorizationAttempt(DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);
        }

        var tags = new Dictionary<string, string>
        {
            ["eshop_order_id"] = order.Id.ToString(),
            ["source"] = "eshoponweb"
        };

        FinixAuthorizationResponse authorization;
        try
        {
            authorization = await _finix.CreateAuthorizationAsync(payment.AmountCents, payment.Currency, source,
                idempotencyId, tags, cancellationToken);
        }
        catch (FinixApiException ex) when (!string.IsNullOrWhiteSpace(ex.DuplicateResourceId))
        {
            authorization = await _finix.GetAuthorizationAsync(ex.DuplicateResourceId!, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentException(MapFinixStatus(ex),
                $"Finix could not authorize this payment: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        if (string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorized(authorization.Id, authorization.State!, source, savedPaymentMethodId, now);
            order.MarkAuthorized();
            await _db.SaveChangesAsync(cancellationToken);
            return MapPayment(payment);
        }

        if (string.Equals(authorization.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorizationPending(authorization.Id, authorization.State!, source, savedPaymentMethodId, now);
            await _db.SaveChangesAsync(cancellationToken);
            return MapPayment(payment);
        }

        payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage, now);
        await _db.SaveChangesAsync(cancellationToken);

        throw new PaymentException(StatusCodes.Status422UnprocessableEntity,
            BuildFailureMessage("Finix declined the card", authorization.FailureCode, authorization.FailureMessage,
                authorization.State));
    }

    public async Task<PaymentDto> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var operation = await _operationLock.AcquireAsync($"order-{orderId}", cancellationToken);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentException(StatusCodes.Status404NotFound, "Order not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order has no payment record. It cannot be fulfilled.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return MapPayment(payment);
        }

        if (payment.Status == PaymentStatus.Cancelled || order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order was cancelled, so there are no funds to capture.");
        }

        if (payment.Status == PaymentStatus.PendingAuthorization && payment.FinixAuthorizationId != null)
        {
            var current = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            payment.UpdateAuthorizationState(current.State ?? "UNKNOWN", current.IsVoid, current.VoidState,
                DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);

            if (string.Equals(current.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorized(current.Id, current.State!, payment.FinixPaymentInstrumentId!,
                    payment.SavedPaymentMethodId, DateTimeOffset.UtcNow);
                order.MarkAuthorized();
                await _db.SaveChangesAsync(cancellationToken);
            }
            else
            {
                throw new PaymentException(StatusCodes.Status409Conflict,
                    $"The authorization {payment.FinixAuthorizationId} is still {current.State} at Finix. " +
                    "Retry fulfilment once the authorization succeeds, or cancel the order.");
            }
        }

        if (payment.Status == PaymentStatus.AuthorizationFailed)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "The shopper's payment authorization failed, so there are no held funds to capture. " +
                "Ask the shopper to pay for the order again.");
        }

        if (payment.Status != PaymentStatus.Authorized)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order has not been authorized. The shopper must pay for it before it can be fulfilled.");
        }

        if (payment.IsAuthorizationVoided ||
            string.Equals(payment.FinixAuthorizationState, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(payment.FinixAuthorizationState, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                $"The authorization {payment.FinixAuthorizationId} can no longer be captured because it is " +
                $"{payment.FinixAuthorizationState ?? "voided"}. Cancel the order or ask the shopper to pay again.");
        }

        FinixAuthorizationResponse capture;
        try
        {
            capture = await _finix.CaptureAuthorizationAsync(payment.FinixAuthorizationId!, payment.AmountCents,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                $"Finix could not capture authorization {payment.FinixAuthorizationId}. " +
                "The hold may have expired or been voided; cancel the order or ask the shopper to pay again. " +
                $"Finix said: {ex.Message}");
        }

        if (!string.Equals(capture.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                BuildFailureMessage($"Finix did not capture authorization {payment.FinixAuthorizationId}",
                    capture.FailureCode, capture.FailureMessage, capture.State));
        }

        var transferId = capture.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            var refreshed = await _finix.GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            transferId = refreshed.Transfer;
        }

        long capturedCents = capture.Amount > 0 ? capture.Amount : payment.AmountCents;
        long feeCents = 0;
        string transferState = capture.State ?? "PENDING";

        if (!string.IsNullOrWhiteSpace(transferId))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(transferId!, cancellationToken);
                feeCents = transfer.Fee;
                transferState = transfer.State ?? transferState;
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning(ex, "Captured payment for order {OrderId} but could not read transfer {TransferId}.",
                    order.Id, transferId);
            }
        }

        var netCents = Math.Max(0, capturedCents - feeCents);
        payment.MarkCaptured(transferId ?? string.Empty, transferState, capturedCents, feeCents, netCents,
            DateTimeOffset.UtcNow);
        order.MarkFulfilled();
        await _db.SaveChangesAsync(cancellationToken);

        return MapPayment(payment);
    }

    public async Task<PaymentDto> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        using var operation = await _operationLock.AcquireAsync($"order-{orderId}", cancellationToken);

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentException(StatusCodes.Status404NotFound, "Order not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order has no payment record to cancel.");
        }

        if (payment.Status == PaymentStatus.Cancelled)
        {
            return MapPayment(payment);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "This order has already been captured. Issue a refund instead of cancelling it.");
        }

        if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            try
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                payment.UpdateAuthorizationState(voided.State ?? payment.FinixAuthorizationState ?? "CANCELED",
                    voided.IsVoid, voided.VoidState, DateTimeOffset.UtcNow);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentException(StatusCodes.Status409Conflict,
                    $"Finix could not release authorization {payment.FinixAuthorizationId}. " +
                    "The hold may already have been captured or expired. " +
                    $"Finix said: {ex.Message}");
            }
        }

        payment.MarkCancelled(payment.FinixAuthorizationState, payment.FinixVoidState, DateTimeOffset.UtcNow);
        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);

        return MapPayment(payment);
    }

    public async Task<RefundDto> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "An idempotencyKey is required for refunds.");
        }

        using var operation = await _operationLock.AcquireAsync($"order-{orderId}", cancellationToken);

        var order = await _db.Orders
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, cancellationToken);

        if (order == null)
        {
            throw new PaymentException(StatusCodes.Status404NotFound, "Order not found.");
        }

        var payment = await LoadPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            throw new PaymentException(StatusCodes.Status409Conflict, "This order has no captured payment to refund.");
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                $"Only captured payments can be refunded. This payment is {payment.Status}.");
        }

        var existing = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == request.IdempotencyKey);
        if (existing != null)
        {
            return MapRefund(existing);
        }

        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "No Finix capture transfer is recorded for this order, so it cannot be refunded.");
        }

        var remaining = payment.RefundableAmountCents;
        if (remaining <= 0)
        {
            throw new PaymentException(StatusCodes.Status409Conflict, "This payment has already been fully refunded.");
        }

        var requestedCents = request.AmountCents.HasValue
            ? request.AmountCents.Value
            : request.Amount.HasValue
                ? ToCents(request.Amount.Value)
                : remaining;
        if (requestedCents <= 0)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "The refund amount must be greater than zero.");
        }

        if (requestedCents > remaining)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                $"The refund amount exceeds the remaining captured amount of {ToAmount(remaining):0.00} " +
                $"{payment.Currency}.");
        }

        var isFullOriginalRefund = requestedCents == (payment.CapturedAmountCents ?? 0);

        if (!isFullOriginalRefund && !string.Equals(payment.FinixTransferState, "SUCCEEDED",
                StringComparison.OrdinalIgnoreCase))
        {
            await WaitForTransferSettlementAsync(payment, cancellationToken);
        }

        FinixTransferResponse reversal;
        try
        {
            reversal = await _finix.RefundTransferAsync(payment.FinixTransferId!, requestedCents,
                request.IdempotencyKey, BuildOrderTags(order.Id), cancellationToken);
        }
        catch (FinixApiException ex) when (!isFullOriginalRefund && ex.StatusCode == StatusCodes.Status409Conflict &&
                                           ex.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
        {
            await WaitForTransferSettlementAsync(payment, cancellationToken);
            reversal = await _finix.RefundTransferAsync(payment.FinixTransferId!, requestedCents,
                request.IdempotencyKey, BuildOrderTags(order.Id), cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentException(MapFinixStatus(ex),
                $"Finix could not create the refund: {ex.Message}");
        }

        var now = DateTimeOffset.UtcNow;
        var refund = payment.AddRefund(requestedCents, request.IdempotencyKey, reversal.Id, reversal.State, now);
        _db.Refunds.Add(refund);

        if (payment.Status == PaymentStatus.Refunded)
        {
            order.MarkRefunded(true);
        }
        else
        {
            order.MarkRefunded(false);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return MapRefund(refund);
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = orderIds.Count == 0
            ? new List<OrderPayment>()
            : await _db.OrderPayments
                .AsNoTracking()
                .Include(p => p.Refunds)
                .Where(p => orderIds.Contains(p.OrderId))
                .ToListAsync(cancellationToken);

        var paymentByOrder = payments.ToDictionary(p => p.OrderId);
        return orders.Select(o => MapOrder(o, paymentByOrder.GetValueOrDefault(o.Id))).ToList();
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);
        ValidateCard(request.Card);

        FinixIdentityResponse identity;
        FinixPaymentInstrumentResponse instrument;
        try
        {
            identity = await _finix.CreateBuyerIdentityAsync(buyerId, request.Card.EffectiveName, cancellationToken);
            var card = new FinixCardRequest
            {
                Number = NormalizeCardNumber(request.Card.EffectiveNumber),
                ExpirationMonth = request.Card.EffectiveExpirationMonth,
                ExpirationYear = request.Card.EffectiveExpirationYear,
                SecurityCode = request.Card.EffectiveSecurityCode,
                Name = request.Card.EffectiveName,
                Identity = identity.Id,
                Address = MapAddress(request.Card.Address),
                Tags = new Dictionary<string, string> { ["source"] = "eshoponweb" }
            };
            instrument = await _finix.CreatePaymentInstrumentAsync(card, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentException(MapFinixStatus(ex),
                $"Finix could not save this card: {ex.Message}");
        }

        var saved = new SavedPaymentMethod(buyerId, instrument.Id, identity.Id, instrument.Brand,
            instrument.LastFour, instrument.ExpirationMonth, instrument.ExpirationYear, instrument.Name,
            DateTimeOffset.UtcNow);

        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        return new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand ?? "UNKNOWN",
            Last4 = saved.Last4 ?? string.Empty,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear,
            CardholderName = saved.CardholderName
        };
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        var saved = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(p => p.BuyerId == buyerId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return saved.Select(MapPaymentMethod).ToList();
    }

    public async Task<bool> DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        EnsureBuyer(buyerId);

        var saved = await _db.SavedPaymentMethods
            .FirstOrDefaultAsync(p => p.Id == paymentMethodId && p.BuyerId == buyerId, cancellationToken);

        if (saved == null)
        {
            throw new PaymentException(StatusCodes.Status404NotFound, "Saved payment method not found.");
        }

        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "The 'from' date must be before 'to'.");
        }

        var authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = orderIds.Count == 0
            ? new List<OrderPayment>()
            : await _db.OrderPayments
                .AsNoTracking()
                .Include(p => p.Refunds)
                .Where(p => orderIds.Contains(p.OrderId))
                .ToListAsync(cancellationToken);

        var paymentByOrder = payments.ToDictionary(p => p.OrderId);
        var orderById = orders.ToDictionary(o => o.Id);

        var paymentByFinixId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
                paymentByFinixId[payment.FinixAuthorizationId!] = payment.OrderId;
            if (!string.IsNullOrWhiteSpace(payment.FinixTransferId))
                paymentByFinixId[payment.FinixTransferId!] = payment.OrderId;
            foreach (var refund in payment.Refunds.Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundTransferId)))
                paymentByFinixId[refund.FinixRefundTransferId!] = payment.OrderId;
        }

        var finixTransactions = new List<ReconciliationFinixTransactionDto>();

        foreach (var authorization in authorizations)
        {
            var orderId = ResolveOrderId(authorization.Tags, authorization.Id, paymentByFinixId);
            finixTransactions.Add(new ReconciliationFinixTransactionDto
            {
                FinixId = authorization.Id,
                Type = "AUTHORIZATION",
                State = authorization.State ?? "UNKNOWN",
                AmountCents = authorization.Amount,
                Amount = ToAmount(authorization.Amount),
                Currency = authorization.Currency ?? string.Empty,
                CreatedAt = authorization.CreatedAt,
                MatchedOrderId = orderId,
                Matched = orderId.HasValue && orderById.ContainsKey(orderId.Value)
            });
        }

        foreach (var transfer in transfers)
        {
            var orderId = ResolveOrderId(transfer.Tags, transfer.Id, paymentByFinixId);
            finixTransactions.Add(new ReconciliationFinixTransactionDto
            {
                FinixId = transfer.Id,
                Type = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                    ? "REFUND"
                    : "TRANSFER",
                State = transfer.State ?? "UNKNOWN",
                AmountCents = transfer.Amount,
                Amount = ToAmount(transfer.Amount),
                Currency = transfer.Currency ?? string.Empty,
                CreatedAt = transfer.CreatedAt,
                MatchedOrderId = orderId,
                Matched = orderId.HasValue && orderById.ContainsKey(orderId.Value)
            });
        }

        var reconciliationOrders = new List<ReconciliationOrderDto>();
        var matchedFinixIds = new HashSet<string>(
            finixTransactions.Where(t => t.Matched).Select(t => t.FinixId), StringComparer.Ordinal);

        foreach (var order in orders)
        {
            paymentByOrder.TryGetValue(order.Id, out var payment);
            var refundIds = payment?.Refunds
                .Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundTransferId))
                .Select(r => r.FinixRefundTransferId!)
                .ToList() ?? new List<string>();

            var ids = new List<string>();
            if (!string.IsNullOrWhiteSpace(payment?.FinixAuthorizationId)) ids.Add(payment!.FinixAuthorizationId!);
            if (!string.IsNullOrWhiteSpace(payment?.FinixTransferId)) ids.Add(payment!.FinixTransferId!);
            ids.AddRange(refundIds);

            reconciliationOrders.Add(new ReconciliationOrderDto
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status.ToString(),
                AmountCents = payment?.AmountCents ?? ToCents(order.Total()),
                Amount = ToAmount(payment?.AmountCents ?? ToCents(order.Total())),
                Currency = payment?.Currency ?? ResolveCurrency(),
                OrderDate = order.OrderDate,
                FinixAuthorizationId = payment?.FinixAuthorizationId,
                FinixTransferId = payment?.FinixTransferId,
                FinixRefundIds = refundIds,
                Matched = ids.Any(matchedFinixIds.Contains)
            });
        }

        finixTransactions = finixTransactions
            .OrderBy(t => t.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        return new ReconciliationResponse
        {
            From = from,
            To = to,
            GeneratedAt = DateTimeOffset.UtcNow,
            FinixAuthorizationCount = authorizations.Count,
            FinixTransferCount = transfers.Count,
            OrderCount = orders.Count,
            FinixTransactions = finixTransactions,
            Orders = reconciliationOrders,
            UnmatchedFinixTransactions = finixTransactions.Where(t => !t.Matched).ToList(),
            UnmatchedOrders = reconciliationOrders.Where(o => !o.Matched).ToList()
        };
    }

    private async Task<OrderPayment?> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.OrderPayments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    private async Task WaitForTransferSettlementAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (string.Equals(payment.FinixTransferState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            throw new PaymentException(StatusCodes.Status409Conflict,
                "No Finix capture transfer is recorded for this order.");
        }

        var deadline = DateTimeOffset.UtcNow.Add(SettlementPollTimeout);
        while (true)
        {
            FinixTransferResponse transfer;
            try
            {
                transfer = await _finix.GetTransferAsync(payment.FinixTransferId!, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentException(MapFinixStatus(ex),
                    $"Could not read capture transfer {payment.FinixTransferId} from Finix: {ex.Message}");
            }

            var fee = transfer.Fee;
            var net = Math.Max(0, transfer.Amount - fee);
            payment.UpdateTransferState(transfer.State ?? "UNKNOWN", fee, net, DateTimeOffset.UtcNow);
            await _db.SaveChangesAsync(cancellationToken);

            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentException(StatusCodes.Status409Conflict,
                    $"Capture transfer {payment.FinixTransferId} is {transfer.State} at Finix and cannot be refunded.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new PaymentException(StatusCodes.Status409Conflict,
                    $"Capture transfer {payment.FinixTransferId} is still {transfer.State} at Finix. " +
                    "Partial refunds become available once the transfer succeeds; retry shortly, " +
                    "or refund the full captured amount.");
            }

            await Task.Delay(SettlementPollInterval, cancellationToken);
        }
    }

    private async Task<string> CreatePaymentInstrumentFromCardAsync(string buyerId, CardRequest card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);

        try
        {
            var identity = await _finix.CreateBuyerIdentityAsync(buyerId, card.EffectiveName, cancellationToken);
            var instrument = await _finix.CreatePaymentInstrumentAsync(new FinixCardRequest
            {
                Number = NormalizeCardNumber(card.EffectiveNumber),
                ExpirationMonth = card.EffectiveExpirationMonth,
                ExpirationYear = card.EffectiveExpirationYear,
                SecurityCode = card.EffectiveSecurityCode,
                Name = card.EffectiveName,
                Identity = identity.Id,
                Address = MapAddress(card.Address),
                Tags = new Dictionary<string, string> { ["source"] = "eshoponweb" }
            }, cancellationToken);

            return instrument.Id;
        }
        catch (FinixApiException ex)
        {
            throw new PaymentException(MapFinixStatus(ex),
                $"Finix could not use this card: {ex.Message}");
        }
    }

    private static int? ResolveOrderId(Dictionary<string, string>? tags, string finixId,
        Dictionary<string, int> paymentByFinixId)
    {
        if (tags != null)
        {
            foreach (var key in new[] { "eshop_order_id", "order_id" })
            {
                if (tags.TryGetValue(key, out var value) && int.TryParse(value, out var orderId))
                {
                    return orderId;
                }
            }
        }

        return paymentByFinixId.TryGetValue(finixId, out var mappedOrderId) ? mappedOrderId : null;
    }

    private static Dictionary<string, string> BuildOrderTags(int orderId) =>
        new() { ["eshop_order_id"] = orderId.ToString(), ["source"] = "eshoponweb" };

    private static void EnsureBuyer(string buyerId)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(StatusCodes.Status401Unauthorized,
                "A signed-in shopper is required.");
        }
    }

    private static void ValidateCard(CardRequest? card)
    {
        if (card == null)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.EffectiveNumber))
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "Card number is required.");
        }

        if (string.IsNullOrWhiteSpace(card.EffectiveName))
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "Cardholder name is required.");
        }

        if (card.EffectiveExpirationMonth < 1 || card.EffectiveExpirationMonth > 12)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                "Expiration month must be between 1 and 12.");
        }

        if (card.EffectiveExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentException(StatusCodes.Status400BadRequest, "The card has expired.");
        }
    }

    private static string NormalizeCardNumber(string number) =>
        Regex.Replace(number, @"\s|-", string.Empty);

    private static FinixAddress? MapAddress(ShippingAddressRequest? address)
    {
        if (address == null)
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Street,
            City = address.City,
            Region = address.State,
            PostalCode = address.ZipCode,
            Country = address.Country
        };
    }

    private Address BuildAddress(ShippingAddressRequest? address)
    {
        if (address == null)
        {
            return new Address("123 Main St.", "Kent", "OH", "United States", "44240");
        }

        if (string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentException(StatusCodes.Status400BadRequest,
                "Street, city, country and zipCode are required when a shipping address is supplied.");
        }

        return new Address(address.Street, address.City, address.State ?? string.Empty,
            address.Country, address.ZipCode);
    }

    private string ResolveCurrency() => _finixOptions.Value.ResolveCurrency();

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal ToAmount(long cents) => cents / 100m;

    private static string BuildFailureMessage(string prefix, string? failureCode, string? failureMessage,
        string? state)
    {
        var parts = new List<string> { prefix };
        if (!string.IsNullOrWhiteSpace(failureCode)) parts.Add($"code {failureCode}");
        if (!string.IsNullOrWhiteSpace(failureMessage)) parts.Add(failureMessage!);
        if (!string.IsNullOrWhiteSpace(state)) parts.Add($"state {state}");
        return string.Join(": ", parts) + ".";
    }

    private static int MapFinixStatus(FinixApiException ex)
    {
        if (ex.StatusCode >= 400 && ex.StatusCode < 500)
        {
            return ex.StatusCode;
        }

        return StatusCodes.Status502BadGateway;
    }

    private OrderDto MapOrder(Order order, OrderPayment? payment)
    {
        var totalCents = payment?.AmountCents ?? ToCents(order.Total());
        return new OrderDto
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = ToAmount(totalCents),
            Currency = payment?.Currency ?? ResolveCurrency(),
            ShipToAddress = new ShippingAddressRequest
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Quantity = i.Units,
                LineTotal = i.UnitPrice * i.Units
            }).ToList(),
            Payment = payment == null ? null : MapPayment(payment)
        };
    }

    private static PaymentDto MapPayment(OrderPayment payment)
    {
        return new PaymentDto
        {
            Status = payment.Status.ToString(),
            Amount = ToAmount(payment.AmountCents),
            Currency = payment.Currency,
            AuthorizedAmount = string.IsNullOrWhiteSpace(payment.FinixAuthorizationId)
                ? 0
                : ToAmount(payment.AmountCents),
            CapturedAmount = ToAmount(payment.CapturedAmountCents ?? 0),
            Fee = ToAmount(payment.FinixFeeCents ?? 0),
            Net = ToAmount(payment.NetCents ?? 0),
            RefundedAmount = ToAmount(payment.RefundedAmountCents),
            RefundableAmount = ToAmount(payment.RefundableAmountCents),
            FinixAuthorizationId = payment.FinixAuthorizationId,
            FinixAuthorizationState = payment.FinixAuthorizationState,
            FinixTransferId = payment.FinixTransferId,
            FinixTransferState = payment.FinixTransferState,
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage,
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(MapRefund)
                .ToList()
        };
    }

    private static RefundDto MapRefund(Refund refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            Amount = ToAmount(refund.AmountCents),
            Status = refund.FinixState ?? "PENDING",
            FinixRefundTransferId = refund.FinixRefundTransferId,
            CreatedAt = refund.CreatedAt
        };
    }

    private static PaymentMethodDto MapPaymentMethod(SavedPaymentMethod paymentMethod)
    {
        return new PaymentMethodDto
        {
            PaymentMethodId = paymentMethod.Id,
            Brand = paymentMethod.Brand ?? "UNKNOWN",
            Last4 = paymentMethod.Last4 ?? string.Empty,
            ExpirationMonth = paymentMethod.ExpirationMonth,
            ExpirationYear = paymentMethod.ExpirationYear,
            CardholderName = paymentMethod.CardholderName
        };
    }
}
