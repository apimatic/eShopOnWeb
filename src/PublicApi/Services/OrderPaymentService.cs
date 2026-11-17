using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.Exceptions;
using Microsoft.eShopWeb.PublicApi.Finix;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using Microsoft.eShopWeb.PublicApi.PaymentMethodEndpoints;
using Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public class OrderPaymentService
{
    private readonly CatalogContext _db;
    private readonly IFinixClient _finix;
    private readonly FinixSettings _settings;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        CatalogContext db,
        IFinixClient finix,
        IOptions<FinixSettings> settings,
        ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY", "The access token does not contain a user identity.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "EMPTY_ORDER", "At least one catalog item is required.");
        }

        foreach (var line in request.Items)
        {
            if (line.CatalogItemId <= 0 || line.Quantity <= 0)
            {
                throw new PaymentOperationException(400, "INVALID_ITEM", "Each item needs a positive catalogItemId and quantity.");
            }
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => ids.Contains(i.Id))
            .ToListAsync(cancellationToken);
        var byId = catalogItems.ToDictionary(i => i.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentOperationException(400, "UNKNOWN_CATALOG_ITEM",
                    $"Catalog item {line.CatalogItemId} does not exist.");
            }

            var snapshot = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(snapshot, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, ToOrderAddress(request.ShippingAddress), orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} with total {Total} {Currency}",
            order.Id, buyerId, order.Total(), _settings.Currency ?? string.Empty);

        return order;
    }

    public async Task<Payment> PayOrderAsync(
        string buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken)
    {
        using var orderLock = await KeyedLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await GetOrderForBuyerAsync(buyerId, orderId, allowAdmin: false, cancellationToken);
        var payment = await GetOrCreatePaymentAsync(order, buyerId, cancellationToken);

        if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
            or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (order.Status is OrderStatus.Cancelled or OrderStatus.Fulfilled
            or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentOperationException(409, "ORDER_NOT_PAYABLE",
                $"Order {orderId} is {order.Status} and can no longer be paid.");
        }

        if (request.PaymentMethodId.HasValue == (request.Card is not null))
        {
            throw new PaymentOperationException(400, "INVALID_PAYMENT_SOURCE",
                "Provide exactly one payment source: paymentMethodId for a saved card, or card for a one-off payment.");
        }

        var currency = RequireCurrency();
        var amountCents = ToCents(order.Total());

        string paymentInstrumentId;
        if (request.PaymentMethodId.HasValue)
        {
            var savedCard = await _db.SavedCards
                .FirstOrDefaultAsync(c => c.Id == request.PaymentMethodId.Value, cancellationToken);

            if (savedCard is null || savedCard.IsDeleted ||
                !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND",
                    $"Saved payment method {request.PaymentMethodId.Value} was not found for this shopper.");
            }

            paymentInstrumentId = savedCard.FinixPaymentInstrumentId;
        }
        else
        {
            var card = ValidateCard(request.Card!);
            var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, card, cancellationToken);
            var instrument = await CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
            paymentInstrumentId = instrument.Id;
        }

        FinixAuthorizationResponse authorization;
        try
        {
            authorization = await _finix.CreateAuthorizationAsync(
                amountCents, currency, paymentInstrumentId,
                payment.AuthorizationIdempotencyId ?? Guid.NewGuid().ToString("N"),
                order.Id, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.IsIdempotencyConflict)
        {
            var existing = await _finix.FindAuthorizationByIdempotencyIdAsync(
                payment.AuthorizationIdempotencyId!, cancellationToken);

            if (existing is null)
            {
                throw WrapFinix(ex, "Authorizing the order");
            }

            authorization = existing;
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Authorizing the order",
                "No money was captured. Check the card details or try another payment method.");
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage, authorization.State ?? "FAILED");
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(402, "AUTHORIZATION_DECLINED",
                $"Finix did not authorize order {orderId}: {authorization.FailureMessage ?? "the card was declined."}" +
                " No funds were taken; the shopper can retry with another card.");
        }

        if (authorization.Amount.HasValue && authorization.Amount.Value != amountCents)
        {
            // Partial authorizations must never silently under-fund an order.
            try
            {
                await _finix.VoidAuthorizationAsync(authorization.Id, cancellationToken);
            }
            catch (FinixApiException)
            {
                // Best effort; the original mismatch is the actionable error.
            }

            payment.MarkAuthorizationFailed("AMOUNT_MISMATCH",
                $"Finix authorized {authorization.Amount.Value} cents but the order total is {amountCents} cents.",
                authorization.State ?? "SUCCEEDED");
            order.MarkPaymentFailed();
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(502, "AUTHORIZATION_AMOUNT_MISMATCH",
                $"Finix authorized {authorization.Amount.Value} cents but order {orderId} requires {amountCents} cents." +
                " The hold was released. Retry the payment or use another card.");
        }

        payment.MarkAuthorized(paymentInstrumentId, authorization.Id,
            authorization.State ?? "SUCCEEDED", amountCents);
        order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Authorized order {OrderId} with Finix authorization {AuthorizationId} for {AmountCents} {Currency}",
            orderId, authorization.Id, amountCents, currency);

        return payment;
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await KeyedLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await GetOrderByIdAsync(orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment is null)
        {
            throw new PaymentOperationException(409, "NO_PAYMENT",
                $"Order {orderId} has no payment. Ask the shopper to pay before fulfilment.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return payment;
        }

        if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentOperationException(409, "NOT_AUTHORIZED",
                $"Order {orderId} is {payment.Status} and has no capturable authorization." +
                " Ask the shopper to pay first, or cancel the order.");
        }

        FinixAuthorizationResponse capture;
        try
        {
            capture = await _finix.CaptureAuthorizationAsync(
                payment.FinixAuthorizationId, payment.AuthorizedAmountCents, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Capturing the authorization",
                $"The hold on order {orderId} can no longer be captured. Ask the shopper to pay again " +
                $"with POST /api/orders/{orderId}/pay, or cancel the order with POST /api/orders/{orderId}/cancel.");
        }

        if (string.IsNullOrWhiteSpace(capture.Transfer))
        {
            throw new PaymentOperationException(502, "CAPTURE_NO_TRANSFER",
                $"Finix accepted the capture for order {orderId} but did not return a capture transfer id." +
                " Reconcile this order with Finix before retrying.");
        }

        FinixTransferResponse transfer;
        try
        {
            transfer = await _finix.GetTransferAsync(capture.Transfer, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Reading the capture transfer",
                $"The capture transfer {capture.Transfer} was created; reconcile it before retrying fulfilment.");
        }

        if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkAuthorizationFailed("CAPTURE_FAILED",
                $"Finix capture transfer {transfer.Id} is {transfer.State}.", capture.State ?? "FAILED");
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(502, "CAPTURE_FAILED",
                $"Finix could not capture order {orderId} (transfer {transfer.Id} is {transfer.State})." +
                " The order was not marked fulfilled. Ask the shopper to pay again or cancel the order.");
        }

        payment.MarkCaptured(transfer.Id, transfer.State ?? "PENDING",
            capture.Amount ?? payment.AuthorizedAmountCents, transfer.Fee ?? 0);
        order.MarkFulfilled();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Fulfilled order {OrderId}; Finix capture transfer {TransferId} state {State}, fee {FeeCents}",
            orderId, transfer.Id, transfer.State, transfer.Fee ?? 0);

        return payment;
    }

    public async Task<Payment?> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        using var orderLock = await KeyedLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await GetOrderByIdAsync(orderId, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled || payment?.Status == PaymentStatus.Cancelled)
        {
            return payment;
        }

        if (payment?.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(409, "ALREADY_CAPTURED",
                $"Order {orderId} has already been captured. Use POST /api/orders/{orderId}/refunds to return the money.");
        }

        if (payment?.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            FinixAuthorizationResponse voided;
            try
            {
                voided = await _finix.VoidAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw WrapFinix(ex, "Voiding the authorization",
                    $"The hold on order {orderId} could not be released. Reconcile authorization " +
                    $"{payment.FinixAuthorizationId} with Finix before retrying cancellation.");
            }

            var cancelledStatus =
                voided.IsVoid == true ||
                string.Equals(voided.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(voided.State, "CANCELED", StringComparison.OrdinalIgnoreCase)
                    ? "CANCELED"
                    : voided.State ?? "CANCELED";
            payment.MarkCancelled(cancelledStatus);
        }
        else
        {
            payment?.MarkCancelled();
        }

        order.MarkCancelled();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId}; Finix hold released: {Released}", orderId,
            payment?.FinixAuthorizationId is not null);

        return payment;
    }

    public async Task<PaymentRefund> RefundOrderAsync(
        string buyerId, bool isAdmin, int orderId, RefundOrderRequest request, CancellationToken cancellationToken)
    {
        using var orderLock = await KeyedLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await GetOrderForBuyerAsync(buyerId, orderId, allowAdmin: isAdmin, cancellationToken);
        var payment = await GetPaymentAsync(orderId, cancellationToken);

        if (payment is null ||
            payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(409, "NOT_CAPTURED",
                $"Order {orderId} has no captured payment to refund. Fulfil the order before refunding it.");
        }

        var idempotencyKey = request.ResolveIdempotencyKey();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentOperationException(400, "MISSING_IDEMPOTENCY_KEY",
                "A caller-supplied idempotencyKey is required for refunds.");
        }

        var amountCents = ToCents(request.Amount);
        var existing = payment.FindRefundByKey(idempotencyKey.Trim());
        if (existing is not null)
        {
            if (existing.AmountCents != amountCents)
            {
                throw new PaymentOperationException(409, "IDEMPOTENCY_KEY_REUSED",
                    $"Idempotency key '{idempotencyKey}' was already used for a refund of " +
                    $"{FromCents(existing.AmountCents)} {payment.Currency}. It cannot be reused for a different amount.");
            }

            return existing;
        }

        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            throw new PaymentOperationException(409, "NO_CAPTURE",
                $"Order {orderId} has no Finix capture transfer to refund.");
        }

        if (amountCents > payment.RefundableAmountCents)
        {
            throw new PaymentOperationException(409, "REFUND_EXCEEDS_CAPTURE",
                $"Refund of {FromCents(amountCents)} {payment.Currency} exceeds the remaining refundable amount " +
                $"of {FromCents(payment.RefundableAmountCents)} {payment.Currency} for order {orderId}.");
        }

        await WaitForCaptureToSettleAsync(payment, amountCents, cancellationToken);

        FinixTransferResponse refund;
        try
        {
            // Scope the Finix idempotency id to this capture so the same caller key on another
            // order/capture is legitimate, while a retry of this request is still deduplicated.
            var finixIdempotencyId = ScopedIdempotencyId(
                $"refund-{payment.FinixCaptureTransferId}", idempotencyKey.Trim());
            refund = await _finix.CreateRefundAsync(
                payment.FinixCaptureTransferId, amountCents, finixIdempotencyId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Refunding the capture",
                "No refund was recorded. Retry with the same idempotencyKey once Finix accepts the request.");
        }

        if (string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(refund.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            payment.MarkRefundFailed("REFUND_FAILED", $"Finix refund transfer {refund.Id} is {refund.State}.");
            await _db.SaveChangesAsync(cancellationToken);

            throw new PaymentOperationException(502, "REFUND_FAILED",
                $"Finix could not refund order {orderId} (refund transfer {refund.Id} is {refund.State})." +
                " No refund was recorded. Retry with the same idempotencyKey or reconcile with Finix.");
        }

        var paymentRefund = payment.AddRefund(
            refund.Amount ?? amountCents, idempotencyKey.Trim(), refund.Id, refund.State ?? "PENDING");
        order.MarkRefunded(payment.Status == PaymentStatus.PartiallyRefunded);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Refunded order {OrderId}; Finix refund transfer {RefundId} for {AmountCents}",
            orderId, refund.Id, refund.Amount ?? amountCents);

        return paymentRefund;
    }

    public async Task<IReadOnlyList<MyOrderDto>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.Payments
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(cancellationToken);

        var changed = false;
        foreach (var payment in payments.Where(p =>
                     string.Equals(p.CaptureStatus, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                     !string.IsNullOrWhiteSpace(p.FinixCaptureTransferId)))
        {
            try
            {
                changed |= await RefreshCaptureAsync(payment, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not refresh Finix capture {TransferId}: {Message}",
                    payment.FinixCaptureTransferId, ex.Message);
            }
        }

        foreach (var refund in payments
                     .SelectMany(p => p.Refunds)
                     .Where(r => string.Equals(r.Status, "PENDING", StringComparison.OrdinalIgnoreCase) &&
                                 !string.IsNullOrWhiteSpace(r.FinixRefundId)))
        {
            try
            {
                var transfer = await _finix.GetTransferAsync(refund.FinixRefundId!, cancellationToken);
                if (!string.IsNullOrWhiteSpace(transfer.State) &&
                    !string.Equals(transfer.State, refund.Status, StringComparison.Ordinal))
                {
                    refund.UpdateFromFinix(transfer.Id, transfer.State);
                    changed = true;
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not refresh Finix refund {RefundId}: {Message}", refund.FinixRefundId, ex.Message);
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var byOrder = payments.ToDictionary(p => p.OrderId);

        return orders.Select(order =>
        {
            byOrder.TryGetValue(order.Id, out var payment);
            var dto = new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                Currency = payment?.Currency ?? _settings.Currency ?? string.Empty,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList()
            };

            if (payment is not null)
            {
                dto.Payment = ToPaymentDto(payment);
            }

            return dto;
        }).ToList();
    }

    public string Currency => _settings.Currency ?? string.Empty;

    public Task<Payment?> GetPaymentSummaryAsync(int orderId, CancellationToken cancellationToken)
    {
        return GetPaymentAsync(orderId, cancellationToken);
    }

    private static PaymentDto ToPaymentDto(Payment payment)
    {
        return new PaymentDto
        {
            PaymentId = payment.Id,
            Status = payment.Status.ToString(),
            Currency = payment.Currency,
            AuthorizedAmount = FromCents(payment.AuthorizedAmountCents),
            CapturedAmount = FromCents(payment.CapturedAmountCents),
            Fee = FromCents(payment.FeeCents),
            Net = FromCents(payment.NetCents),
            RefundedAmount = FromCents(payment.RefundedAmountCents),
            RefundableAmount = FromCents(payment.RefundableAmountCents),
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationStatus = payment.AuthorizationStatus,
            CaptureId = payment.FinixCaptureTransferId,
            CaptureStatus = payment.CaptureStatus,
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage,
            Refunds = payment.Refunds.Select(r => new PaymentRefundDto
            {
                RefundId = r.Id,
                FinixRefundId = r.FinixRefundId,
                Amount = FromCents(r.AmountCents),
                Status = r.Status,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        };
    }

    public async Task<SavedCard> SaveCardAsync(
        string buyerId, SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "NO_IDENTITY", "The access token does not contain a user identity.");
        }

        var card = ValidateCard(request);
        var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, card, cancellationToken);
        var instrument = await CreatePaymentInstrumentAsync(identityId, card, cancellationToken);

        if (instrument.Enabled == false)
        {
            throw new PaymentOperationException(422, "CARD_DISABLED",
                $"Finix saved the card but disabled it: {instrument.DisabledMessage ?? instrument.DisabledCode ?? "unknown reason"}.");
        }

        var savedCard = new SavedCard(
            buyerId,
            identityId,
            instrument.Id,
            instrument.Brand ?? "UNKNOWN",
            instrument.LastFour ?? "0000",
            instrument.ExpirationMonth ?? card.ExpirationMonth,
            instrument.ExpirationYear ?? card.ExpirationYear,
            instrument.CardType);

        _db.SavedCards.Add(savedCard);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved card {PaymentMethodId} for buyer {BuyerId}: {Brand} ending {Last4}",
            savedCard.Id, buyerId, savedCard.Brand, savedCard.Last4);

        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> ListCardsAsync(string buyerId, CancellationToken cancellationToken)
    {
        return await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.BuyerId == buyerId && !c.IsDeleted)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<SavedCard> DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var savedCard = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.Id == paymentMethodId, cancellationToken);

        if (savedCard is null || !string.Equals(savedCard.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND",
                $"Saved payment method {paymentMethodId} was not found for this shopper.");
        }

        if (savedCard.IsDeleted)
        {
            return savedCard;
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Disabling the saved card",
                "The card was not removed locally. Retry the delete.");
        }

        savedCard.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted saved card {PaymentMethodId} for buyer {BuyerId}", savedCard.Id, buyerId);

        return savedCard;
    }

    public async Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "INVALID_DATE_RANGE", "'from' must be earlier than or equal to 'to'.");
        }

        IReadOnlyList<FinixTransferResponse> transfers;
        IReadOnlyList<FinixAuthorizationResponse> authorizations;
        try
        {
            transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
            authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Reading Finix transactions for reconciliation");
        }

        var payments = await _db.Payments
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var captureByFinixId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixCaptureTransferId))
            .GroupBy(p => p.FinixCaptureTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var refundByFinixId = payments
            .SelectMany(p => p.Refunds.Select(r => new { Payment = p, Refund = r }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Refund.FinixRefundId))
            .GroupBy(x => x.Refund.FinixRefundId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var authorizationByFinixId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .GroupBy(p => p.FinixAuthorizationId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var paymentByOrderId = payments
            .GroupBy(p => p.OrderId)
            .ToDictionary(g => g.Key, g => g.First());

        var response = new ReconciliationResponse { From = from, To = to };
        var finixTransferIds = new HashSet<string>(transfers.Select(t => t.Id), StringComparer.Ordinal);
        var finixAuthorizationIds = new HashSet<string>(authorizations.Select(a => a.Id), StringComparer.Ordinal);

        foreach (var transfer in transfers)
        {
            var dto = new ReconciliationTransactionDto
            {
                FinixId = transfer.Id,
                Kind = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase) ? "refund" : "transfer",
                Type = transfer.Type,
                OperationKey = transfer.OperationKey,
                State = transfer.State,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency,
                CreatedAt = transfer.CreatedAt
            };

            if (captureByFinixId.TryGetValue(transfer.Id, out var capturedPayment))
            {
                dto.MatchedOrderId = capturedPayment.OrderId;
                dto.MatchedLocalType = "capture";
                dto.MatchStatus = "MATCHED";
            }
            else if (refundByFinixId.TryGetValue(transfer.Id, out var refundMatch))
            {
                dto.MatchedOrderId = refundMatch.Payment.OrderId;
                dto.MatchedLocalType = "refund";
                dto.MatchStatus = "MATCHED";
            }
            else if (TryGetTaggedOrderId(transfer.Tags, out var taggedOrderId) &&
                     paymentByOrderId.ContainsKey(taggedOrderId))
            {
                dto.MatchedOrderId = taggedOrderId;
                dto.MatchedLocalType = "tagged";
                dto.MatchStatus = "MATCHED_BY_TAG";
            }
            else
            {
                dto.MatchStatus = "FINIX_ONLY";
                response.Discrepancies.Add(new ReconciliationDiscrepancyDto
                {
                    Side = "FINIX",
                    Kind = dto.Kind,
                    FinixId = transfer.Id,
                    AmountCents = transfer.Amount,
                    State = transfer.State,
                    Message = $"Finix has a {dto.Kind} transfer that eShop cannot match to an order."
                });
            }

            response.FinixTransactions.Add(dto);
        }

        foreach (var authorization in authorizations)
        {
            var dto = new ReconciliationTransactionDto
            {
                FinixId = authorization.Id,
                Kind = "authorization",
                Type = "AUTHORIZATION",
                OperationKey = null,
                State = authorization.State,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt
            };

            if (authorizationByFinixId.TryGetValue(authorization.Id, out var authorizedPayment))
            {
                dto.MatchedOrderId = authorizedPayment.OrderId;
                dto.MatchedLocalType = "authorization";
                dto.MatchStatus = "MATCHED";
            }
            else if (TryGetTaggedOrderId(authorization.Tags, out var taggedOrderId) &&
                     paymentByOrderId.ContainsKey(taggedOrderId))
            {
                dto.MatchedOrderId = taggedOrderId;
                dto.MatchedLocalType = "tagged";
                dto.MatchStatus = "MATCHED_BY_TAG";
            }
            else
            {
                dto.MatchStatus = "FINIX_ONLY";
                response.Discrepancies.Add(new ReconciliationDiscrepancyDto
                {
                    Side = "FINIX",
                    Kind = dto.Kind,
                    FinixId = authorization.Id,
                    AmountCents = authorization.Amount,
                    State = authorization.State,
                    Message = "Finix has an authorization that eShop cannot match to an order."
                });
            }

            response.FinixTransactions.Add(dto);
        }

        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId) &&
                !finixAuthorizationIds.Contains(payment.FinixAuthorizationId) &&
                InRange(payment.AuthorizedAt ?? payment.CreatedAt, from, to))
            {
                response.Discrepancies.Add(new ReconciliationDiscrepancyDto
                {
                    Side = "ESHOP",
                    Kind = "authorization",
                    FinixId = payment.FinixAuthorizationId,
                    OrderId = payment.OrderId,
                    LocalId = payment.Id,
                    AmountCents = payment.AuthorizedAmountCents,
                    State = payment.AuthorizationStatus,
                    Message = "eShop has an authorization that Finix did not return for this range."
                });
            }

            if (!string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId) &&
                !finixTransferIds.Contains(payment.FinixCaptureTransferId) &&
                InRange(payment.CapturedAt ?? payment.CreatedAt, from, to))
            {
                response.Discrepancies.Add(new ReconciliationDiscrepancyDto
                {
                    Side = "ESHOP",
                    Kind = "capture",
                    FinixId = payment.FinixCaptureTransferId,
                    OrderId = payment.OrderId,
                    LocalId = payment.Id,
                    AmountCents = payment.CapturedAmountCents,
                    State = payment.CaptureStatus,
                    Message = "eShop has a capture that Finix did not return for this range."
                });
            }

            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixRefundId) &&
                    !finixTransferIds.Contains(refund.FinixRefundId) &&
                    InRange(refund.CreatedAt, from, to))
                {
                    response.Discrepancies.Add(new ReconciliationDiscrepancyDto
                    {
                        Side = "ESHOP",
                        Kind = "refund",
                        FinixId = refund.FinixRefundId,
                        OrderId = payment.OrderId,
                        LocalId = refund.Id,
                        AmountCents = refund.AmountCents,
                        State = refund.Status,
                        Message = "eShop has a refund that Finix did not return for this range."
                    });
                }
            }
        }

        response.FinixTransactionCount = response.FinixTransactions.Count;
        response.MatchedCount = response.FinixTransactions.Count(t =>
            t.MatchStatus is "MATCHED" or "MATCHED_BY_TAG");
        response.FinixOnlyCount = response.Discrepancies.Count(d => d.Side == "FINIX");
        response.EshopOnlyCount = response.Discrepancies.Count(d => d.Side == "ESHOP");

        return response;
    }

    private async Task<Order> GetOrderForBuyerAsync(
        string buyerId, int orderId, bool allowAdmin, CancellationToken cancellationToken)
    {
        var order = await GetOrderByIdAsync(orderId, cancellationToken);

        if (!allowAdmin && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Return 404 rather than 403 so one shopper cannot probe another shopper's order ids.
            throw new PaymentOperationException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderByIdAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

        if (order is null)
        {
            throw new PaymentOperationException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Payment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _db.Payments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
    }

    private async Task<Payment> GetOrCreatePaymentAsync(
        Order order, string buyerId, CancellationToken cancellationToken)
    {
        var payment = await GetPaymentAsync(order.Id, cancellationToken);
        if (payment is not null)
        {
            return payment;
        }

        payment = new Payment(order.Id, buyerId, RequireCurrency());
        _db.Payments.Add(payment);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Another request may have created the row concurrently; use the winner.
            payment = await GetPaymentAsync(order.Id, cancellationToken);
            if (payment is null)
            {
                throw;
            }
        }

        return payment;
    }

    private async Task<string> GetOrCreateBuyerIdentityAsync(
        string buyerId, FinixCardInput card, CancellationToken cancellationToken)
    {
        using var identityLock = await KeyedLock.AcquireAsync($"identity:{buyerId}", cancellationToken);

        var existing = await _db.BuyerFinixIdentities
            .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);

        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var (firstName, lastName) = SplitName(card.Name);
        var email = buyerId.Contains('@') && buyerId.Length <= 100
            ? buyerId
            : "shopper@example.com";

        FinixIdentityResponse identity;
        try
        {
            identity = await _finix.CreateBuyerIdentityAsync(
                email, firstName, lastName, card.Address, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Creating the Finix buyer identity");
        }

        var mapping = new BuyerFinixIdentity(buyerId, identity.Id);
        _db.BuyerFinixIdentities.Add(mapping);
        await _db.SaveChangesAsync(cancellationToken);

        return identity.Id;
    }

    private async Task<FinixPaymentInstrumentResponse> CreatePaymentInstrumentAsync(
        string identityId, FinixCardInput card, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw WrapFinix(ex, "Saving the card with Finix",
                "Check the card number, expiry, security code and billing address.");
        }
    }

    private async Task WaitForCaptureToSettleAsync(
        Payment payment, long requestedRefundCents, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            return;
        }

        // Full reversals can be submitted while a capture is pending. Partial refunds need a settled capture.
        var partial = requestedRefundCents < payment.RefundableAmountCents;

        for (var attempt = 0; attempt < 60; attempt++)
        {
            var transfer = await _finix.GetTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
            payment.UpdateCaptureDetails(transfer.State ?? "UNKNOWN", transfer.Fee ?? 0);

            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!partial)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        if (partial)
        {
            throw new PaymentOperationException(409, "CAPTURE_STILL_PENDING",
                "The capture is still settling at Finix. Partial refunds require a settled capture; " +
                "retry the refund in a few seconds with the same idempotencyKey.");
        }
    }

    private async Task<bool> RefreshCaptureAsync(Payment payment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            return false;
        }

        var transfer = await _finix.GetTransferAsync(payment.FinixCaptureTransferId, cancellationToken);
        var newState = transfer.State ?? payment.CaptureStatus ?? "UNKNOWN";
        var newFee = transfer.Fee ?? payment.FeeCents;

        if (string.Equals(newState, payment.CaptureStatus, StringComparison.Ordinal) && newFee == payment.FeeCents)
        {
            return false;
        }

        payment.UpdateCaptureDetails(newState, newFee);
        return true;
    }

    private string RequireCurrency()
    {
        if (string.IsNullOrWhiteSpace(_settings.Currency))
        {
            throw new PaymentOperationException(500, "CURRENCY_NOT_CONFIGURED",
                "Finix:Currency is not configured.");
        }

        return _settings.Currency!;
    }

    private static FinixCardInput ValidateCard(CardPaymentRequest request)
    {
        if (request is null)
        {
            throw new PaymentOperationException(400, "MISSING_CARD", "Card details are required.");
        }

        var number = new string((request.Number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (number.Length is < 13 or > 19)
        {
            throw new PaymentOperationException(400, "INVALID_CARD_NUMBER",
                "The card number must contain 13 to 19 digits.");
        }

        if (request.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "INVALID_EXPIRY", "Expiration month must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (request.ExpirationYear < now.Year ||
            (request.ExpirationYear == now.Year && request.ExpirationMonth < now.Month))
        {
            throw new PaymentOperationException(400, "EXPIRED_CARD", "The card has expired.");
        }

        var securityCode = new string((request.SecurityCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (securityCode.Length is < 3 or > 4)
        {
            throw new PaymentOperationException(400, "INVALID_SECURITY_CODE",
                "The security code must contain 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new PaymentOperationException(400, "MISSING_CARDHOLDER_NAME",
                "The cardholder name is required.");
        }

        return new FinixCardInput(
            number,
            request.ExpirationMonth,
            request.ExpirationYear,
            securityCode,
            request.Name.Trim(),
            ToFinixAddress(request.Address));
    }

    private static Address ToOrderAddress(AddressRequest? address)
    {
        return new Address(
            Truncate(FirstNonEmpty(address?.Line1, address?.Street, "1 eShop Way"), 180),
            Truncate(FirstNonEmpty(address?.City, "Redmond"), 100),
            Truncate(FirstNonEmpty(address?.Region, address?.State, "WA"), 60),
            Truncate(FirstNonEmpty(address?.Country, "USA"), 90),
            Truncate(FirstNonEmpty(address?.PostalCode, address?.ZipCode, "98052"), 18));
    }

    private static FinixAddress ToFinixAddress(AddressRequest? address)
    {
        return new FinixAddress(
            Truncate(FirstNonEmpty(address?.Line1, address?.Street, "1 eShop Way"), 35),
            Truncate(FirstNonEmpty(address?.City, "San Francisco"), 20),
            Truncate(FirstNonEmpty(address?.Region, address?.State, "CA"), 2),
            Truncate(FirstNonEmpty(address?.PostalCode, address?.ZipCode, "94105"), 7),
            Truncate(FirstNonEmpty(address?.Country, "USA"), 3),
            address?.Line2 is { } line2 ? Truncate(line2, 35) : null);
    }

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        var parts = (name ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return parts.Length switch
        {
            0 => ("eShop", "Shopper"),
            1 => (parts[0], "Shopper"),
            _ => (parts[0], parts[^1])
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string Truncate(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static long ToCents(decimal amount)
    {
        if (amount <= 0)
        {
            throw new PaymentOperationException(400, "INVALID_AMOUNT", "Amount must be greater than zero.");
        }

        var cents = decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);
        if (cents > long.MaxValue)
        {
            throw new PaymentOperationException(400, "INVALID_AMOUNT", "Amount is too large.");
        }

        return (long)cents;
    }

    private static decimal FromCents(long cents) => cents / 100m;

    private static string ScopedIdempotencyId(string scope, string callerKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(callerKey))).ToLowerInvariant();
        return $"eshop-{scope}-{hash[..32]}";
    }

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to)
    {
        return value >= from && value <= to;
    }

    private static bool TryGetTaggedOrderId(Dictionary<string, string>? tags, out int orderId)
    {
        orderId = 0;
        return tags is not null &&
               tags.TryGetValue("order_id", out var value) &&
               int.TryParse(value, out orderId);
    }

    private static PaymentOperationException WrapFinix(FinixApiException ex, string action, string? hint = null)
    {
        var status = ex.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? 502
            : (int)ex.StatusCode;

        if (status is < 400 or > 599)
        {
            status = 502;
        }

        var details = string.Empty;
        if (!string.IsNullOrWhiteSpace(ex.Code) || !string.IsNullOrWhiteSpace(ex.LogRef))
        {
            details = $" (Finix code: {ex.Code ?? "UNKNOWN"}; logref: {ex.LogRef ?? "n/a"})";
        }

        var message = $"{action} failed: {ex.Message}{details}.";
        if (!string.IsNullOrWhiteSpace(hint))
        {
            message += " " + hint;
        }

        return new PaymentOperationException(status, ex.Code ?? "FINIX_ERROR", message);
    }
}
