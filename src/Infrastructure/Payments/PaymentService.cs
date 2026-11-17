using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> IdentityLocks = new();

    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _options;
    private readonly IUriComposer _uriComposer;

    public PaymentService(
        CatalogContext db,
        IFinixClient finix,
        IOptions<FinixOptions> options,
        IUriComposer uriComposer)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _uriComposer = uriComposer;
    }

    public async Task<OrderDto> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderItemInput> items,
        ShippingAddressInput? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException("The caller identity is missing.", 401);
        }

        if (items == null || items.Count == 0)
        {
            throw new PaymentOperationException("At least one catalog item is required.", 400);
        }

        foreach (var item in items)
        {
            if (item.Quantity <= 0)
            {
                throw new PaymentOperationException("Item quantities must be greater than zero.", 400);
            }
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var item in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == item.CatalogItemId);
            if (catalogItem == null)
            {
                throw new PaymentOperationException($"Catalog item {item.CatalogItemId} was not found.", 400);
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, item.Quantity));
        }

        var address = shippingAddress == null
            ? new Address("123 Main St", "San Francisco", "CA", "USA", "94105")
            : new Address(
                shippingAddress.Street,
                shippingAddress.City,
                shippingAddress.State,
                shippingAddress.Country,
                shippingAddress.ZipCode);

        var order = new Order(buyerId, address, orderItems);
        order.SetCurrency(_options.Currency);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return MapOrder(order, Array.Empty<OrderRefund>());
    }

    public async Task<OrderDto> PayOrderAsync(
        string buyerId,
        int orderId,
        CardInput? card,
        int? savedPaymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order == null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException("Order not found.", 404);
            }

            if (order.PaymentStatus is OrderPaymentStatus.Authorized
                or OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return MapOrder(order, await LoadRefundsAsync(orderId, cancellationToken));
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                throw new PaymentOperationException("The order is canceled and can no longer be paid.", 409);
            }

            if (savedPaymentMethodId.HasValue == (card != null))
            {
                throw new PaymentOperationException(
                    "Provide either card details or a saved payment method id, but not both.",
                    400);
            }

            string paymentInstrumentId;
            if (savedPaymentMethodId.HasValue)
            {
                var savedCard = await _db.PaymentMethods
                    .FirstOrDefaultAsync(
                        p => p.Id == savedPaymentMethodId.Value
                             && p.BuyerId == buyerId
                             && !p.IsDeleted,
                        cancellationToken);

                if (savedCard == null || string.IsNullOrWhiteSpace(savedCard.CardId))
                {
                    throw new PaymentOperationException("Saved payment method not found.", 404);
                }

                paymentInstrumentId = savedCard.CardId;
            }
            else
            {
                ValidateCard(card!);
                var identityId = await GetOrCreateIdentityIdAsync(buyerId, cancellationToken);
                var instrument = await _finix.CreatePaymentInstrumentAsync(
                    new FinixCreatePaymentInstrumentRequest
                    {
                        Identity = identityId,
                        Name = card!.Name,
                        Number = NormalizeCardNumber(card.Number),
                        SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode,
                        ExpirationMonth = card.ExpirationMonth,
                        ExpirationYear = card.ExpirationYear,
                        Address = ToFinixAddress(card.Address),
                        Tags = new Dictionary<string, string>
                        {
                            ["order_id"] = order.Id.ToString(),
                            ["order_ref"] = order.PaymentReference
                        }
                    },
                    cancellationToken);

                paymentInstrumentId = instrument.Id;
            }

            var amountInCents = ToCents(order.Total());
            var idempotencyId = $"eshop-{order.PaymentReference}-authorize";

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    new FinixCreateAuthorizationRequest
                    {
                        Amount = amountInCents,
                        Currency = _options.Currency,
                        Merchant = _options.MerchantId,
                        Source = paymentInstrumentId,
                        IdempotencyId = idempotencyId,
                        Tags = new Dictionary<string, string>
                        {
                            ["order_id"] = order.Id.ToString(),
                            ["order_ref"] = order.PaymentReference
                        }
                    },
                    cancellationToken);
            }
            catch (FinixApiException ex) when (IsIdempotencyConflict(ex))
            {
                authorization = await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken)
                    ?? throw new PaymentOperationException(
                        $"Finix rejected the authorization: {ex.Message}",
                        409);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                var message = authorization.FailureMessage
                              ?? $"Finix returned authorization state {authorization.State ?? "UNKNOWN"}.";
                order.MarkPaymentFailed(message);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException($"Authorization failed: {message}", 402);
            }

            order.MarkPaymentAuthorized(
                authorization.Id,
                authorization.State ?? "SUCCEEDED",
                FromCents(authorization.Amount),
                authorization.Currency ?? _options.Currency,
                paymentInstrumentId);
            await _db.SaveChangesAsync(cancellationToken);

            return MapOrder(order, Array.Empty<OrderRefund>());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> FulfilOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order == null)
            {
                throw new PaymentOperationException("Order not found.", 404);
            }

            if (order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return MapOrder(order, await LoadRefundsAsync(orderId, cancellationToken));
            }

            if (order.PaymentStatus != OrderPaymentStatus.Authorized
                || string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                throw new PaymentOperationException(
                    $"Order {orderId} cannot be fulfilled because its payment status is {order.PaymentStatus}. " +
                    "The shopper must authorize payment first.",
                    409);
            }

            var authorization = await _finix.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
            order.SetAuthorizationState(authorization.State ?? "UNKNOWN");

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || authorization.IsVoid
                || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(
                    $"Authorization {authorization.Id} is in state {authorization.State ?? "UNKNOWN"} " +
                    $"(void state: {authorization.VoidState ?? "UNATTEMPTED"}) and can no longer be captured. " +
                    "Ask the shopper to authorize a new payment or cancel the order.",
                    409);
            }

            var captureAmountInCents = ToCents(order.Total());
            FinixAuthorization captured;
            try
            {
                captured = await _finix.CaptureAuthorizationAsync(
                    order.FinixAuthorizationId,
                    captureAmountInCents,
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var statusCode = ex.StatusCode == 422 ? 409 : 502;
                throw new PaymentOperationException(
                    $"Finix could not capture authorization {order.FinixAuthorizationId}: {ex.Message}. " +
                    "The hold may have expired or already been voided; ask the shopper to pay again or cancel the order.",
                    statusCode);
            }

            var transferId = captured.Transfer ?? authorization.Transfer;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentOperationException(
                    "Finix captured the authorization but did not return a transfer id, so refunds cannot be issued. " +
                    "Contact Finix support with the authorization id.",
                    502);
            }

            var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"Finix reported capture transfer {transfer.Id} as FAILED: {transfer.FailureMessage ?? "no failure message"}. " +
                    "Ask the shopper to authorize a new payment.",
                    409);
            }

            var settlement = await WaitForCaptureSettlementAsync(transfer, cancellationToken);
            transfer = settlement.Transfer;
            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"Finix reported capture transfer {transfer.Id} as {transfer.State}: {transfer.FailureMessage ?? "no failure message"}. " +
                    "Ask the shopper to authorize a new payment.",
                    409);
            }

            var feeInCents = settlement.Fees.Sum(f => f.Amount);
            var capturedAmount = transfer.Amount > 0 ? FromCents(transfer.Amount) : FromCents(captureAmountInCents);
            var feeAmount = feeInCents / 100m;
            var netAmount = capturedAmount - feeAmount;

            order.MarkFulfilled(transferId, capturedAmount, feeAmount, netAmount);
            await _db.SaveChangesAsync(cancellationToken);

            return MapOrder(order, await LoadRefundsAsync(orderId, cancellationToken));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> CancelOrderAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order == null)
            {
                throw new PaymentOperationException("Order not found.", 404);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Canceled)
            {
                return MapOrder(order, await LoadRefundsAsync(orderId, cancellationToken));
            }

            if (order.PaymentStatus is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                throw new PaymentOperationException(
                    "The order has already been fulfilled, so it cannot be canceled. Use the refunds endpoint instead.",
                    409);
            }

            if (order.PaymentStatus == OrderPaymentStatus.Authorized
                && !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            {
                var authorization = await _finix.GetAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
                order.SetAuthorizationState(authorization.State ?? "UNKNOWN");

                if (authorization.IsVoid
                    || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    order.SetAuthorizationState("CANCELED");
                    order.MarkCanceled();
                    await _db.SaveChangesAsync(cancellationToken);
                    return MapOrder(order, Array.Empty<OrderRefund>());
                }

                if (string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        await _finix.VoidAuthorizationAsync(order.FinixAuthorizationId, cancellationToken);
                        order.SetAuthorizationState("CANCELED");
                    }
                    catch (FinixApiException ex)
                    {
                        var statusCode = ex.StatusCode == 422 ? 409 : 502;
                        throw new PaymentOperationException(
                            $"Finix could not release authorization {order.FinixAuthorizationId}: {ex.Message}. " +
                            "If the authorization was already captured, use the refunds endpoint instead.",
                            statusCode);
                    }
                }
            }

            order.MarkCanceled();
            await _db.SaveChangesAsync(cancellationToken);
            return MapOrder(order, Array.Empty<OrderRefund>());
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundDto> RefundOrderAsync(
        string buyerId,
        int orderId,
        decimal amount,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException("An idempotencyKey is required for refunds.", 400);
        }

        var gate = GetOrderLock(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order == null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException("Order not found.", 404);
            }

            if (order.PaymentStatus is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded))
            {
                throw new PaymentOperationException(
                    $"Order {orderId} has payment status {order.PaymentStatus}; only fulfilled orders can be refunded.",
                    409);
            }

            var existingRefund = await _db.OrderRefunds.FirstOrDefaultAsync(
                r => r.OrderId == orderId && r.IdempotencyKey == idempotencyKey,
                cancellationToken);
            if (existingRefund != null)
            {
                return MapRefund(existingRefund);
            }

            if (amount <= 0m)
            {
                throw new PaymentOperationException("Refund amount must be greater than zero.", 400);
            }

            var remaining = order.CapturedAmount - order.RefundedAmount;
            if (amount > remaining)
            {
                throw new PaymentOperationException(
                    $"Refund amount {amount:0.00} exceeds the remaining refundable amount of {remaining:0.00}.",
                    400);
            }

            if (string.IsNullOrWhiteSpace(order.FinixTransferId))
            {
                throw new PaymentOperationException(
                    "The order has no capture transfer, so it cannot be refunded.",
                    409);
            }

            var captureTransfer = await WaitForCaptureTransferAsync(order.FinixTransferId, cancellationToken);
            if (!string.Equals(captureTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"The capture transfer {captureTransfer.Id} is in state {captureTransfer.State ?? "UNKNOWN"} and cannot be refunded yet. " +
                    "Wait for the capture to succeed and retry the refund.",
                    409);
            }

            var finixIdempotencyId = BuildRefundIdempotencyId(order.PaymentReference, idempotencyKey);
            FinixTransfer refund;
            try
            {
                refund = await _finix.CreateRefundAsync(
                    order.FinixTransferId,
                    ToCents(amount),
                    finixIdempotencyId,
                    cancellationToken);
            }
            catch (FinixApiException ex) when (IsIdempotencyConflict(ex))
            {
                refund = await _finix.FindTransferByIdempotencyIdAsync(finixIdempotencyId, cancellationToken)
                    ?? throw new PaymentOperationException(
                        $"Finix rejected the refund: {ex.Message}",
                        409);
            }

            if (string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    $"Finix reported refund {refund.Id} as FAILED: {refund.FailureMessage ?? "no failure message"}.",
                    409);
            }

            var refundEntity = new OrderRefund(
                orderId,
                refund.Id,
                amount,
                idempotencyKey,
                refund.State ?? "UNKNOWN");
            _db.OrderRefunds.Add(refundEntity);
            order.RegisterRefund(amount);
            await _db.SaveChangesAsync(cancellationToken);

            return MapRefund(refundEntity);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
        {
            return Array.Empty<OrderDto>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var refunds = await _db.OrderRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => MapOrder(o, refunds.Where(r => r.OrderId == o.Id).ToList()))
            .ToList();
    }

    public async Task<SavedCardDto> SavePaymentMethodAsync(
        string buyerId,
        CardInput card,
        CancellationToken cancellationToken = default)
    {
        ValidateCard(card);

        var identityId = await GetOrCreateIdentityIdAsync(buyerId, cancellationToken);
        var instrument = await _finix.CreatePaymentInstrumentAsync(
            new FinixCreatePaymentInstrumentRequest
            {
                Identity = identityId,
                Name = card.Name,
                Number = NormalizeCardNumber(card.Number),
                SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                Address = ToFinixAddress(card.Address)
            },
            cancellationToken);

        var savedCard = new PaymentMethod(
            buyerId,
            instrument.Id,
            instrument.Brand,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            card.Name);

        _db.PaymentMethods.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        return MapSavedCard(savedCard);
    }

    public async Task<IReadOnlyList<SavedCardDto>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _db.PaymentMethods
            .Where(p => p.BuyerId == buyerId && !p.IsDeleted)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return cards.Select(MapSavedCard).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var savedCard = await _db.PaymentMethods.FirstOrDefaultAsync(
            p => p.Id == paymentMethodId && p.BuyerId == buyerId && !p.IsDeleted,
            cancellationToken);

        if (savedCard == null || string.IsNullOrWhiteSpace(savedCard.CardId))
        {
            throw new PaymentOperationException("Saved payment method not found.", 404);
        }

        try
        {
            await _finix.UpdatePaymentInstrumentAsync(savedCard.CardId, false, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404)
        {
        }

        savedCard.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentOperationException("The 'to' date must be greater than or equal to 'from'.", 400);
        }

        var authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);

        var orders = await _db.Orders.ToListAsync(cancellationToken);
        var refunds = await _db.OrderRefunds.ToListAsync(cancellationToken);

        var ordersById = orders.ToDictionary(o => o.Id);
        var orderByReference = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.PaymentReference))
            .GroupBy(o => o.PaymentReference)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var orderByAuthorization = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.FinixAuthorizationId))
            .GroupBy(o => o.FinixAuthorizationId!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var orderByTransfer = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.FinixTransferId))
            .GroupBy(o => o.FinixTransferId!)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var orderByRefundTransfer = refunds
            .GroupBy(r => r.FinixRefundTransferId)
            .ToDictionary(g => g.Key, g => g.First().OrderId);

        var transactions = new List<FinixTransactionDto>();

        foreach (var authorization in authorizations)
        {
            var orderId = ResolveOrderId(authorization.Tags, authorization.Id, orderByReference, orderByAuthorization, ordersById);
            transactions.Add(new FinixTransactionDto(
                authorization.Id,
                "AUTHORIZATION",
                authorization.State ?? "UNKNOWN",
                authorization.Amount,
                authorization.Currency,
                authorization.CreatedAt,
                orderId,
                orderId != null));
        }

        foreach (var transfer in transfers)
        {
            var orderId = ResolveOrderId(transfer.Tags, transfer.Id, orderByReference, orderByTransfer, ordersById);
            if (orderId == null && orderByRefundTransfer.TryGetValue(transfer.Id, out var refundOrderId))
            {
                orderId = refundOrderId.ToString();
            }

            transactions.Add(new FinixTransactionDto(
                transfer.Id,
                string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                    ? "REFUND"
                    : "TRANSFER",
                transfer.State ?? "UNKNOWN",
                transfer.Amount,
                transfer.Currency,
                transfer.CreatedAt,
                orderId,
                orderId != null));
        }

        var finixIds = new HashSet<string>(transactions.Select(t => t.FinixId), StringComparer.Ordinal);

        var refundsByOrder = refunds
            .GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var eshopOrders = new List<ReconciliationOrderDto>();
        foreach (var order in orders)
        {
            var orderRefunds = refundsByOrder.TryGetValue(order.Id, out var value)
                ? value
                : new List<OrderRefund>();

            var hasPayment = !string.IsNullOrWhiteSpace(order.FinixAuthorizationId)
                             || !string.IsNullOrWhiteSpace(order.FinixTransferId)
                             || orderRefunds.Count > 0;
            if (!hasPayment)
            {
                continue;
            }

            var inRange = IsInRange(order.OrderDate, from, to)
                           || IsInRange(order.AuthorizedAt, from, to)
                           || IsInRange(order.FulfilledAt, from, to)
                           || orderRefunds.Any(r => IsInRange(r.CreatedAt, from, to));

            if (!inRange)
            {
                continue;
            }

            var matched = (!string.IsNullOrWhiteSpace(order.FinixAuthorizationId)
                           && finixIds.Contains(order.FinixAuthorizationId))
                          || (!string.IsNullOrWhiteSpace(order.FinixTransferId)
                              && finixIds.Contains(order.FinixTransferId))
                          || orderRefunds.Any(r => finixIds.Contains(r.FinixRefundTransferId));

            eshopOrders.Add(new ReconciliationOrderDto(
                order.Id,
                order.BuyerId,
                order.PaymentStatus,
                order.FinixAuthorizationId,
                order.FinixTransferId,
                order.CapturedAmount,
                order.RefundedAmount,
                matched));
        }

        var unmatchedFinix = transactions.Where(t => !t.Matched).ToList();
        var unmatchedEshop = eshopOrders.Where(o => !o.Matched).ToList();

        return new ReconciliationReport(
            from,
            to,
            transactions,
            eshopOrders,
            unmatchedFinix,
            unmatchedEshop,
            new ReconciliationSummaryDto(
                transactions.Count,
                eshopOrders.Count,
                transactions.Count(t => t.Matched),
                unmatchedFinix.Count,
                unmatchedEshop.Count));
    }

    private async Task<FinixTransfer> WaitForCaptureTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static string? ResolveOrderId(
        Dictionary<string, string>? tags,
        string finixId,
        Dictionary<string, int> orderByReference,
        Dictionary<string, int> orderByFinixId,
        Dictionary<int, Order> ordersById)
    {
        if (tags != null
            && tags.TryGetValue("order_ref", out var taggedReference)
            && orderByReference.TryGetValue(taggedReference, out var referencedOrderId))
        {
            return referencedOrderId.ToString();
        }

        if (tags != null
            && tags.TryGetValue("order_id", out var taggedOrderId)
            && int.TryParse(taggedOrderId, out var parsedOrderId)
            && ordersById.ContainsKey(parsedOrderId))
        {
            return parsedOrderId.ToString();
        }

        return orderByFinixId.TryGetValue(finixId, out var orderId)
            ? orderId.ToString()
            : null;
    }

    private static bool IsInRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
    {
        return value.HasValue && value.Value >= from && value.Value <= to;
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
    }

    private async Task<List<OrderRefund>> LoadRefundsAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.OrderRefunds
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    private async Task<string> GetOrCreateIdentityIdAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.ShopperProfiles
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
        if (existing != null)
        {
            return existing.FinixIdentityId;
        }

        var gate = IdentityLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.ShopperProfiles
                .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
            if (existing != null)
            {
                return existing.FinixIdentityId;
            }

            var identity = await _finix.CreateBuyerIdentityAsync(cancellationToken);
            var profile = new ShopperProfile(buyerId, identity.Id);
            _db.ShopperProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);
            return identity.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<(FinixTransfer Transfer, IReadOnlyList<FinixFee> Fees)> WaitForCaptureSettlementAsync(
        FinixTransfer transfer,
        CancellationToken cancellationToken)
    {
        var transferDeadline = DateTimeOffset.UtcNow.AddSeconds(90);

        while (DateTimeOffset.UtcNow < transferDeadline
               && !string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase)
               && !string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(1000, cancellationToken);
            transfer = await _finix.GetTransferAsync(transfer.Id, cancellationToken);
        }

        var feeDeadline = DateTimeOffset.UtcNow.AddSeconds(30);
        IReadOnlyList<FinixFee> fees = Array.Empty<FinixFee>();
        do
        {
            fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
            if (fees.Count > 0 || DateTimeOffset.UtcNow >= feeDeadline)
            {
                break;
            }

            await Task.Delay(500, cancellationToken);
        }
        while (true);

        return (transfer, fees);
    }

    private static SemaphoreSlim GetOrderLock(int orderId)
    {
        return OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
    }

    private static void ValidateCard(CardInput card)
    {
        if (card == null)
        {
            throw new PaymentOperationException("Card details are required.", 400);
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentOperationException("Card number is required.", 400);
        }

        if (card.ExpirationMonth < 1 || card.ExpirationMonth > 12)
        {
            throw new PaymentOperationException("Card expiration month must be between 1 and 12.", 400);
        }

        var currentYear = DateTimeOffset.UtcNow.Year;
        if (card.ExpirationYear < currentYear || card.ExpirationYear > currentYear + 30)
        {
            throw new PaymentOperationException("Card expiration year is not valid.", 400);
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException("Cardholder name is required.", 400);
        }
    }

    private static string NormalizeCardNumber(string number)
    {
        return number.Replace(" ", string.Empty).Replace("-", string.Empty);
    }

    private static FinixAddressRequest? ToFinixAddress(CardAddressInput? address)
    {
        if (address == null)
        {
            return null;
        }

        return new FinixAddressRequest
        {
            Line1 = address.Line1,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = address.Country
        };
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static decimal FromCents(long amount)
    {
        return amount / 100m;
    }

    private static string BuildRefundIdempotencyId(string paymentReference, string idempotencyKey)
    {
        var value = $"eshop-{paymentReference}-refund-{idempotencyKey}";
        if (value.Length <= 200)
        {
            return value;
        }

        var hash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey)));
        return $"eshop-{paymentReference}-refund-{hash[..32]}";
    }

    private static bool IsIdempotencyConflict(FinixApiException exception)
    {
        return exception.StatusCode == 409
               || exception.StatusCode == 422
               || exception.Message.Contains("idempotency", StringComparison.OrdinalIgnoreCase);
    }

    private static OrderDto MapOrder(Order order, IReadOnlyList<OrderRefund> refunds)
    {
        return new OrderDto(
            order.Id,
            order.OrderDate,
            order.BuyerId,
            order.Total(),
            ToCents(order.Total()),
            order.Currency,
            order.PaymentStatus,
            order.FinixAuthorizationId,
            order.FinixAuthorizationState,
            order.FinixTransferId,
            order.FinixPaymentInstrumentId,
            order.CapturedAmount,
            order.FeeAmount,
            order.NetAmount,
            order.RefundedAmount,
            order.PaymentFailureMessage,
            order.AuthorizedAt,
            order.FulfilledAt,
            order.CanceledAt,
            refunds.OrderBy(r => r.CreatedAt).Select(MapRefund).ToList());
    }

    private static RefundDto MapRefund(OrderRefund refund)
    {
        return new RefundDto(
            refund.Id,
            refund.FinixRefundTransferId,
            refund.Amount,
            refund.State,
            refund.IdempotencyKey,
            refund.CreatedAt);
    }

    private static SavedCardDto MapSavedCard(PaymentMethod card)
    {
        return new SavedCardDto(
            card.Id,
            card.Brand ?? "UNKNOWN",
            card.Last4 ?? "****",
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Alias,
            card.CreatedAt);
    }
}
