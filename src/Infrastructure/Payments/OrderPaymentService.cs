using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<OrderPayment> _payments;
    private readonly IRepository<SavedPaymentMethod> _savedMethods;
    private readonly IRepository<CustomerPaymentProfile> _profiles;
    private readonly IFinixPaymentGateway _finix;
    private readonly FinixSettings _settings;
    private readonly IUriComposer _uriComposer;
    private readonly OrderLock _orderLock;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<OrderPayment> payments,
        IRepository<SavedPaymentMethod> savedMethods,
        IRepository<CustomerPaymentProfile> profiles,
        IFinixPaymentGateway finix,
        IOptions<FinixSettings> settings,
        IUriComposer uriComposer,
        OrderLock orderLock,
        ILogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _payments = payments;
        _savedMethods = savedMethods;
        _profiles = profiles;
        _finix = finix;
        _settings = settings.Value;
        _uriComposer = uriComposer;
        _orderLock = orderLock;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(
        string buyerId,
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentForbiddenException("A signed-in shopper is required to place an order.");
        }

        EnsureFinixConfigured();

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        foreach (var line in request.Items)
        {
            if (line.CatalogItemId <= 0)
            {
                throw new PaymentValidationException("CatalogItemId must be a positive integer.");
            }

            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException("Quantity must be greater than zero.");
            }
        }

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var byId = catalogItems.ToDictionary(c => c.Id);

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (!byId.TryGetValue(line.CatalogItemId, out var catalogItem))
            {
                throw new PaymentValidationException($"Catalog item {line.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));

            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(buyerId, BuildAddress(request.ShippingAddress), orderItems);
        order = await _orders.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, _settings.Currency);
        await _payments.AddAsync(payment, cancellationToken);

        _logger.LogInformation(
            "Created order {OrderId} for buyer {BuyerId} with total {Total} {Currency}.",
            order.Id,
            buyerId,
            order.Total(),
            _settings.Currency);

        return ToOrderDto(order, payment);
    }

    public async Task<OrderDto> AuthorizeOrderAsync(
        string buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureFinixConfigured();

        using var orderLock = await _orderLock.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadOrCreatePaymentAsync(order, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentConflictException("This order was cancelled and can no longer be paid.");
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException("This order has already been fulfilled and paid.");
        }

        if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
            string.Equals(payment.AuthorizationState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            if (order.Status == OrderStatus.AwaitingPayment)
            {
                order.MarkAuthorized();
                await _orders.UpdateAsync(order, cancellationToken);
            }

            return ToOrderDto(order, payment);
        }

        if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            var existing = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            if (existing is not null)
            {
                payment.RecordVoid(existing.State);

                if (string.Equals(existing.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    payment.RecordAuthorization(
                        payment.FinixIdentityId,
                        existing.Source ?? payment.PaymentInstrumentId ?? string.Empty,
                        existing.Id,
                        existing.State,
                        existing.Amount,
                        existing.ExpiresAt);

                    order.MarkAuthorized();
                    await _payments.UpdateAsync(payment, cancellationToken);
                    await _orders.UpdateAsync(order, cancellationToken);
                    return ToOrderDto(order, payment);
                }

                if (string.Equals(existing.State, "PENDING", StringComparison.OrdinalIgnoreCase))
                {
                    await _payments.UpdateAsync(payment, cancellationToken);
                    throw new PaymentConflictException(
                        $"The authorization {existing.Id} for this order is still pending at Finix. " +
                        "Wait for it to finish before trying to pay again.",
                        "AUTHORIZATION_PENDING");
                }

                payment.ClearAuthorizationIdempotencyId();
                await _payments.UpdateAsync(payment, cancellationToken);
            }
        }

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The order total must be greater than zero.");
        }

        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var sourcePaymentInstrumentId = await ResolveSourcePaymentInstrumentAsync(
            buyerId,
            request,
            identityId,
            cancellationToken);

        var idempotencyId = $"eshop-order-{orderId}-authorize-{Guid.NewGuid():N}";
        payment.EnsureAuthorizationIdempotencyId(idempotencyId);
        await _payments.UpdateAsync(payment, cancellationToken);

        FinixAuthorizationResponse authorization;
        try
        {
            authorization = await _finix.CreateAuthorizationAsync(
                sourcePaymentInstrumentId,
                amountCents,
                payment.AuthorizationIdempotencyId!,
                orderId,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            var recovered = await TryFindAuthorizationByIdempotencyAsync(
                payment.AuthorizationIdempotencyId!,
                cancellationToken);

            if (recovered is null)
            {
                payment.RecordAuthorizationFailure(ex.FinixCode, ex.Message);

                var idempotencyConflict =
                    (ex.FinixCode?.Contains("IDEMPOT", StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (ex.Message?.Contains("idempotency", StringComparison.OrdinalIgnoreCase) ?? false);

                if (!idempotencyConflict && ex.FinixStatusCode is 402 or 422)
                {
                    payment.ClearAuthorizationIdempotencyId();
                }

                await _payments.UpdateAsync(payment, cancellationToken);
                throw TranslateAuthorizationError(ex);
            }

            authorization = recovered;
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.RecordAuthorizationFailure(authorization.FailureCode, authorization.FailureMessage);
            payment.ClearAuthorizationIdempotencyId();
            await _payments.UpdateAsync(payment, cancellationToken);

            var message = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                ? $"Finix did not authorize the payment (state: {authorization.State})."
                : authorization.FailureMessage!;

            throw new PaymentOperationException(402, message, authorization.FailureCode ?? "AUTHORIZATION_DECLINED");
        }

        payment.RecordAuthorization(
            identityId,
            sourcePaymentInstrumentId,
            authorization.Id,
            authorization.State,
            authorization.Amount > 0 ? authorization.Amount : amountCents,
            authorization.ExpiresAt);

        order.MarkAuthorized();
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Authorized order {OrderId} for {AmountCents} cents with Finix authorization {AuthorizationId}.",
            orderId,
            amountCents,
            authorization.Id);

        return ToOrderDto(order, payment);
    }

    public async Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        EnsureFinixConfigured();

        using var orderLock = await _orderLock.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await LoadOrCreatePaymentAsync(order, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return ToOrderDto(order, payment);
        }

        if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            throw new PaymentConflictException(
                "This order has no active authorization. Pay the order before fulfilling it.");
        }

        if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            var existingTransfer = await WaitForTransferAsync(payment.CaptureTransferId!, cancellationToken);

            if (string.Equals(existingTransfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.RefreshCapture(existingTransfer.State, existingTransfer.Amount, existingTransfer.Fee);
                order.MarkFulfilled();
                await _payments.UpdateAsync(payment, cancellationToken);
                await _orders.UpdateAsync(order, cancellationToken);
                return ToOrderDto(order, payment);
            }

            if (string.Equals(existingTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                payment.RefreshCapture(existingTransfer.State, existingTransfer.Amount, existingTransfer.Fee);
                await _payments.UpdateAsync(payment, cancellationToken);
                throw new PaymentConflictException(
                    $"Finix capture {existingTransfer.Id} failed: {existingTransfer.FailureMessage ?? "unknown failure"}. " +
                    "Ask the shopper to pay again or cancel the order.",
                    "CAPTURE_FAILED");
            }

            payment.RefreshCapture(existingTransfer.State, existingTransfer.Amount, existingTransfer.Fee);
            await _payments.UpdateAsync(payment, cancellationToken);
            return ToOrderDto(order, payment);
        }

        var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
        payment.RecordVoid(authorization.State);

        if (authorization.IsVoid ||
            string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"The authorization hold {authorization.Id} was voided or released and can no longer be captured. " +
                "Ask the shopper to pay again (creating a new authorization) or cancel the order.",
                "AUTHORIZATION_VOIDED");
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"The authorization hold {authorization.Id} expired at {authorization.ExpiresAt.Value:O} and can no longer be captured. " +
                "Ask the shopper to pay again (creating a new authorization) or cancel the order.",
                "AUTHORIZATION_EXPIRED");
        }

        if (!string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            return await RecordCaptureFromTransferAsync(order, payment, authorization.Transfer!, cancellationToken);
        }

        var captureAmount = payment.AuthorizedAmountCents > 0
            ? payment.AuthorizedAmountCents
            : ToCents(order.Total());

        FinixAuthorizationResponse captured;
        try
        {
            captured = await _finix.CaptureAuthorizationAsync(
                payment.AuthorizationId!,
                captureAmount,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            var refreshed = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            if (refreshed is not null && !string.IsNullOrWhiteSpace(refreshed.Transfer))
            {
                return await RecordCaptureFromTransferAsync(order, payment, refreshed.Transfer!, cancellationToken);
            }

            throw new PaymentConflictException(
                $"Finix could not capture authorization {payment.AuthorizationId}: {ex.Message}. " +
                "The hold may have expired or been released. Ask the shopper to pay again or cancel the order.",
                "CAPTURE_FAILED");
        }

        var transferId = captured.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            var refreshed = await TryGetAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
            transferId = refreshed?.Transfer;
        }

        if (string.IsNullOrWhiteSpace(transferId))
        {
            throw new PaymentConflictException(
                $"Finix captured authorization {payment.AuthorizationId} but did not return a capture transfer id. " +
                "Check the authorization in the Finix dashboard before retrying fulfilment.",
                "CAPTURE_TRANSFER_MISSING");
        }

        return await RecordCaptureFromTransferAsync(order, payment, transferId!, cancellationToken);
    }

    public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        EnsureFinixConfigured();

        using var orderLock = await _orderLock.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOrderAsync(orderId, cancellationToken);
        var payment = await LoadOrCreatePaymentAsync(order, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return ToOrderDto(order, payment);
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentConflictException(
                "This order has already been captured. Refund the payment instead of cancelling the order.");
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            try
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId!, cancellationToken);
                payment.RecordVoid(voided.State);
            }
            catch (FinixApiException ex)
            {
                throw new PaymentConflictException(
                    $"Finix could not void authorization {payment.AuthorizationId}: {ex.Message}. " +
                    "If the hold was already captured, refund the payment instead.",
                    "VOID_FAILED");
            }
        }

        order.MarkCancelled();
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId}; any authorization hold was released.", orderId);
        return ToOrderDto(order, payment);
    }

    public async Task<RefundDto> RefundOrderAsync(
        string buyerId,
        int orderId,
        RefundOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for refunds.");
        }

        EnsureFinixConfigured();

        using var orderLock = await _orderLock.AcquireAsync(orderId, cancellationToken);

        var order = await LoadOwnedOrderAsync(buyerId, orderId, cancellationToken);
        var payment = await LoadOrCreatePaymentAsync(order, cancellationToken);

        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentConflictException(
                "This order has not been captured yet. Cancel the order to release the hold instead of refunding.");
        }

        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            throw new PaymentConflictException(
                "Only a fulfilled order can be refunded. Cancel the order before fulfilment to release the hold.");
        }

        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, request.IdempotencyKey, StringComparison.Ordinal));
        if (existing is not null)
        {
            return ToRefundDto(existing);
        }

        var remaining = payment.RefundableAmountCents;
        var amountCents = request.Amount.HasValue ? ToCents(request.Amount.Value) : remaining;

        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The refund amount must be greater than zero.");
        }

        if (remaining <= 0)
        {
            throw new PaymentConflictException(
                "The captured payment has already been fully refunded; no refundable amount remains.",
                "REFUND_EXCEEDS_CAPTURED");
        }

        if (amountCents > remaining)
        {
            throw new PaymentConflictException(
                $"Refund of {FromCents(amountCents)} {payment.Currency} exceeds the remaining captured amount of " +
                $"{FromCents(remaining)} {payment.Currency}. Refunds can never exceed the captured amount.",
                "REFUND_EXCEEDS_CAPTURED");
        }

        // Finix only allows partial refunds once the capture Transfer has
        // settled. A capture created moments ago can still be PENDING, so wait
        // briefly for the authoritative state before attempting the refund.
        var capture = await WaitForTransferAsync(payment.CaptureTransferId!, cancellationToken);
        if (!string.Equals(capture.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.RefreshCapture(capture.State, capture.Amount, capture.Fee);
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"The capture transfer {capture.Id} is still {capture.State} at Finix. " +
                "Wait for the capture to settle, then retry the refund.",
                "CAPTURE_NOT_SETTLED");
        }

        if (payment.CaptureState != capture.State ||
            payment.CapturedAmountCents != capture.Amount ||
            payment.FeeCents != capture.Fee)
        {
            payment.RefreshCapture(capture.State, capture.Amount, capture.Fee);
            await _payments.UpdateAsync(payment, cancellationToken);
        }

        FinixTransferResponse refund;
        try
        {
            refund = await _finix.CreateRefundAsync(
                payment.CaptureTransferId!,
                amountCents,
                request.IdempotencyKey,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            var recovered = await TryFindTransferByIdempotencyAsync(request.IdempotencyKey, cancellationToken);
            if (recovered is null)
            {
                throw new PaymentOperationException(
                    502,
                    $"Finix could not refund transfer {payment.CaptureTransferId}: {ex.Message}",
                    ex.FinixCode ?? "REFUND_FAILED");
            }

            refund = recovered;
        }

        var record = payment.AddRefund(request.IdempotencyKey, amountCents);
        record.RecordFinixResult(refund.Id, refund.State);

        if (string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            record.RecordFailure(refund.FailureMessage ?? "Finix reported the refund as failed.");
            await _payments.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"Finix rejected refund {refund.Id}: {refund.FailureMessage ?? "unknown failure"}",
                "REFUND_FAILED");
        }

        var fullyRefunded = payment.RefundedAmountCents >= payment.CapturedAmountCents;
        order.MarkRefunded(fullyRefunded);
        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Refunded {AmountCents} cents on order {OrderId} via Finix refund {RefundId} (state {State}).",
            amountCents,
            orderId,
            refund.Id,
            refund.State);

        return ToRefundDto(record);
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId),
            cancellationToken);

        var result = new List<OrderDto>();
        foreach (var order in orders.OrderByDescending(o => o.OrderDate))
        {
            var payment = await _payments.FirstOrDefaultAsync(
                new OrderPaymentByOrderIdSpec(order.Id),
                cancellationToken);
            result.Add(ToOrderDto(order, payment));
        }

        return result;
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new PaymentValidationException("'to' must be greater than or equal to 'from'.");
        }

        EnsureFinixConfigured();

        var finixTransfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        var payments = await _payments.ListAsync(new AllOrderPaymentsWithRefundsSpec(), cancellationToken);
        var orders = await _orders.ListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var eshopReferences = new List<EshopReference>();
        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                var at = payment.CapturedAt ?? payment.UpdatedAt;
                if (at >= from && at <= to)
                {
                    eshopReferences.Add(new EshopReference(
                        payment.CaptureTransferId!,
                        payment.OrderId,
                        "CAPTURE",
                        payment.CapturedAmountCents,
                        at));
                }
            }

            foreach (var refund in payment.Refunds)
            {
                if (string.IsNullOrWhiteSpace(refund.FinixRefundId))
                {
                    continue;
                }

                if (refund.CreatedAt >= from && refund.CreatedAt <= to)
                {
                    eshopReferences.Add(new EshopReference(
                        refund.FinixRefundId!,
                        payment.OrderId,
                        "REFUND",
                        refund.AmountCents,
                        refund.CreatedAt));
                }
            }
        }

        var eshopByTransferId = eshopReferences
            .GroupBy(r => r.TransferId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            Currency = _settings.Currency
        };

        var matchedTransferIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transfer in finixTransfers
                     .Where(t => string.IsNullOrWhiteSpace(t.Merchant) ||
                                 string.Equals(t.Merchant, _settings.MerchantId, StringComparison.Ordinal))
                     .OrderBy(t => t.CreatedAt))
        {
            if (eshopByTransferId.TryGetValue(transfer.Id, out var reference))
            {
                matchedTransferIds.Add(transfer.Id);
                report.MatchedCount++;
                report.Entries.Add(new ReconciliationEntry
                {
                    Status = "MATCHED",
                    FinixTransferId = transfer.Id,
                    FinixType = transfer.Type,
                    FinixOperationKey = transfer.OperationKey,
                    FinixState = transfer.State,
                    FinixAmount = FromCents(transfer.Amount),
                    OrderId = reference.OrderId,
                    OrderStatus = ordersById.TryGetValue(reference.OrderId, out var order)
                        ? order.Status.ToString()
                        : null,
                    EshopReferenceType = reference.ReferenceType,
                    EshopAmount = FromCents(reference.AmountCents),
                    Note = "Finix transaction is linked to an eShop order."
                });
            }
            else
            {
                report.FinixOnlyCount++;
                report.Entries.Add(new ReconciliationEntry
                {
                    Status = "FINIX_ONLY",
                    FinixTransferId = transfer.Id,
                    FinixType = transfer.Type,
                    FinixOperationKey = transfer.OperationKey,
                    FinixState = transfer.State,
                    FinixAmount = FromCents(transfer.Amount),
                    Note = "Finix has a transaction that eShop has not recorded."
                });
            }
        }

        foreach (var reference in eshopReferences.Where(r => !matchedTransferIds.Contains(r.TransferId)))
        {
            report.EshopOnlyCount++;
            report.Entries.Add(new ReconciliationEntry
            {
                Status = "ESHOP_ONLY",
                FinixTransferId = reference.TransferId,
                OrderId = reference.OrderId,
                OrderStatus = ordersById.TryGetValue(reference.OrderId, out var order)
                    ? order.Status.ToString()
                    : null,
                EshopReferenceType = reference.ReferenceType,
                EshopAmount = FromCents(reference.AmountCents),
                Note = "eShop recorded a transaction that was not returned by Finix for this range."
            });
        }

        report.Entries = report.Entries
            .OrderBy(e => e.Status)
            .ThenBy(e => e.OrderId)
            .ThenBy(e => e.FinixTransferId)
            .ToList();

        return report;
    }

    public async Task<SavedPaymentMethodDto> SavePaymentMethodAsync(
        string ownerId,
        SavePaymentMethodRequest request,
        CancellationToken cancellationToken)
    {
        EnsureFinixConfigured();
        ValidateCard(request.Card);

        var identityId = await GetOrCreateIdentityAsync(ownerId, cancellationToken);
        FinixPaymentInstrumentResponse instrument;
        try
        {
            instrument = await _finix.CreatePaymentInstrumentAsync(
                identityId,
                request.Card,
                ownerId,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentValidationException(
                $"Finix could not save this card: {ex.Message}",
                ex.FinixCode ?? "CARD_REJECTED");
        }

        var last4 = !string.IsNullOrWhiteSpace(instrument.LastFour)
            ? instrument.LastFour!
            : NormalizeCardNumber(request.Card.Number)[^4..];

        var method = new SavedPaymentMethod(
            ownerId,
            instrument.Id,
            instrument.Brand,
            last4,
            request.Card.ExpirationMonth,
            request.Card.ExpirationYear,
            request.Card.Name,
            request.Alias);

        method = await _savedMethods.AddAsync(method, cancellationToken);

        _logger.LogInformation(
            "Saved Finix payment instrument {PaymentInstrumentId} for buyer {BuyerId} (last4 {Last4}).",
            instrument.Id,
            ownerId,
            last4);

        return ToSavedPaymentMethodDto(method);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(
        string ownerId,
        CancellationToken cancellationToken)
    {
        var methods = await _savedMethods.ListAsync(
            new SavedPaymentMethodsByOwnerSpec(ownerId),
            cancellationToken);

        return methods.Select(ToSavedPaymentMethodDto).ToList();
    }

    public async Task DeleteSavedPaymentMethodAsync(
        string ownerId,
        int paymentMethodId,
        CancellationToken cancellationToken)
    {
        EnsureFinixConfigured();

        var method = await _savedMethods.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndOwnerSpec(paymentMethodId, ownerId),
            cancellationToken);

        if (method is null)
        {
            throw new PaymentNotFoundException("Saved payment method not found.");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(method.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.FinixStatusCode == 404)
        {
            _logger.LogWarning(
                "Finix payment instrument {PaymentInstrumentId} was already absent while deleting saved card {PaymentMethodId}.",
                method.FinixPaymentInstrumentId,
                method.Id);
        }
        catch (Exception ex)
        {
            // Remove the local reference regardless: it must no longer appear or
            // be usable through eShop. The Finix instrument is disabled on a
            // best-effort basis and the failure is recorded for operators.
            _logger.LogError(
                ex,
                "Failed to disable Finix payment instrument {PaymentInstrumentId} while deleting saved card {PaymentMethodId}.",
                method.FinixPaymentInstrumentId,
                method.Id);
        }

        await _savedMethods.DeleteAsync(method, cancellationToken);
    }

    private void EnsureFinixConfigured()
    {
        if (!_settings.IsConfigured)
        {
            throw new PaymentOperationException(
                503,
                "Finix is not configured. Set Finix:Username, Finix:Password and Finix:MerchantId " +
                "(or the FINIX_USERNAME, FINIX_PASSWORD and FINIX_MERCHANT_ID environment variables).",
                "FINIX_NOT_CONFIGURED");
        }

        if (string.IsNullOrWhiteSpace(_settings.Currency))
        {
            throw new PaymentOperationException(
                503,
                "Finix:Currency is not configured. Set Finix:Currency (or FINIX_CURRENCY).",
                "FINIX_CURRENCY_MISSING");
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> LoadOwnedOrderAsync(
        string buyerId,
        int orderId,
        CancellationToken cancellationToken)
    {
        var order = await LoadOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            // Do not reveal that another shopper's order exists.
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<OrderPayment> LoadOrCreatePaymentAsync(Order order, CancellationToken cancellationToken)
    {
        var payment = await _payments.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpec(order.Id),
            cancellationToken);

        if (payment is not null)
        {
            return payment;
        }

        payment = new OrderPayment(order.Id, _settings.Currency);
        return await _payments.AddAsync(payment, cancellationToken);
    }

    private async Task<string> GetOrCreateIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.FirstOrDefaultAsync(
            new CustomerPaymentProfileByOwnerSpec(ownerId),
            cancellationToken);

        if (profile is not null)
        {
            return profile.FinixIdentityId;
        }

        var identity = await _finix.CreateBuyerIdentityAsync(cancellationToken);
        profile = new CustomerPaymentProfile(ownerId, identity.Id);
        await _profiles.AddAsync(profile, cancellationToken);

        _logger.LogInformation(
            "Created Finix buyer identity {IdentityId} for buyer {BuyerId}.",
            identity.Id,
            ownerId);

        return identity.Id;
    }

    private async Task<string> ResolveSourcePaymentInstrumentAsync(
        string buyerId,
        PayOrderRequest request,
        string identityId,
        CancellationToken cancellationToken)
    {
        if (request.PaymentMethodId.HasValue)
        {
            var method = await _savedMethods.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndOwnerSpec(request.PaymentMethodId.Value, buyerId),
                cancellationToken);

            if (method is null)
            {
                throw new PaymentNotFoundException("Saved payment method not found.");
            }

            try
            {
                var instrument = await _finix.GetPaymentInstrumentAsync(
                    method.FinixPaymentInstrumentId,
                    cancellationToken);

                if (!instrument.Enabled)
                {
                    throw new PaymentConflictException(
                        "This saved card is disabled at Finix and can no longer be used. Save a new card instead.",
                        "PAYMENT_INSTRUMENT_DISABLED");
                }
            }
            catch (FinixApiException ex) when (ex.FinixStatusCode == 404)
            {
                throw new PaymentConflictException(
                    "This saved card no longer exists at Finix. Remove it and save a new card.",
                    "PAYMENT_INSTRUMENT_MISSING");
            }

            return method.FinixPaymentInstrumentId;
        }

        if (request.Card is null)
        {
            throw new PaymentValidationException(
                "Provide either card details or the id of one of your saved payment methods.");
        }

        ValidateCard(request.Card);

        try
        {
            var instrument = await _finix.CreatePaymentInstrumentAsync(
                identityId,
                request.Card,
                buyerId,
                cancellationToken);

            return instrument.Id;
        }
        catch (FinixApiException ex)
        {
            throw new PaymentValidationException(
                $"Finix could not use this card: {ex.Message}",
                ex.FinixCode ?? "CARD_REJECTED");
        }
    }

    private async Task<OrderDto> RecordCaptureFromTransferAsync(
        Order order,
        OrderPayment payment,
        string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await WaitForTransferAsync(transferId, cancellationToken);

        if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            payment.RecordCapture(transfer.Id, transfer.State, 0, transfer.Fee, 0);
            await _payments.UpdateAsync(payment, cancellationToken);

            throw new PaymentConflictException(
                $"Finix capture {transfer.Id} failed: {transfer.FailureMessage ?? "unknown failure"}. " +
                "Ask the shopper to pay again or cancel the order.",
                "CAPTURE_FAILED");
        }

        var captured = transfer.Amount;
        var fee = transfer.Fee;
        var net = captured - fee;

        payment.RecordCapture(transfer.Id, transfer.State, captured, fee, net);
        order.MarkFulfilled();

        await _payments.UpdateAsync(payment, cancellationToken);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId}: transfer {TransferId}, amount {CapturedCents} cents, fee {FeeCents} cents, net {NetCents} cents.",
            order.Id,
            transfer.Id,
            captured,
            fee,
            net);

        return ToOrderDto(order, payment);
    }

    private async Task<FinixTransferResponse> WaitForTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<FinixAuthorizationResponse?> TryFindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.FindAuthorizationByIdempotencyIdAsync(idempotencyId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up Finix authorization by idempotency id.");
            return null;
        }
    }

    private async Task<FinixAuthorizationResponse?> TryGetAuthorizationAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not refresh Finix authorization {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<FinixTransferResponse?> TryFindTransferByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.FindTransferByIdempotencyIdAsync(idempotencyId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up Finix transfer by idempotency id.");
            return null;
        }
    }

    private static PaymentOperationException TranslateAuthorizationError(FinixApiException ex)
    {
        var status = ex.FinixStatusCode is 402 or 422 ? 402 : 502;
        return new PaymentOperationException(
            status,
            $"Finix could not authorize the payment: {ex.Message}",
            ex.FinixCode ?? "AUTHORIZATION_FAILED");
    }

    private static void ValidateCard(CardRequest card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }

        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 12 or > 19 || !number.All(char.IsDigit))
        {
            throw new PaymentValidationException("A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("ExpirationMonth must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year ||
            (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentValidationException("The card expiration date is in the past.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode) &&
            (card.SecurityCode.Length is < 3 or > 4 || !card.SecurityCode.All(char.IsDigit)))
        {
            throw new PaymentValidationException("SecurityCode must be 3 or 4 digits.");
        }
    }

    private static string NormalizeCardNumber(string number) =>
        (number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty).Trim();

    private static Address BuildAddress(ShippingAddressRequest? request)
    {
        return new Address(
            TrimTo(request?.Street, 180, "N/A"),
            TrimTo(request?.City, 100, "N/A"),
            TrimTo(request?.State, 60, "N/A"),
            TrimTo(request?.Country, 90, "USA"),
            TrimTo(request?.ZipCode, 18, "00000"));
    }

    private static string TrimTo(string? value, int maxLength, string fallback)
    {
        var result = string.IsNullOrWhiteSpace(value) ? fallback : value!.Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }

    private static int ToCents(decimal amount) =>
        (int)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal FromCents(int cents) => cents / 100m;

    private static OrderDto ToOrderDto(Order order, OrderPayment? payment)
    {
        return new OrderDto
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency ?? string.Empty,
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList(),
            Payment = payment is null ? null : ToPaymentDto(payment)
        };
    }

    private static PaymentDto ToPaymentDto(OrderPayment payment)
    {
        return new PaymentDto
        {
            AuthorizationId = payment.AuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            CaptureTransferId = payment.CaptureTransferId,
            CaptureState = payment.CaptureState,
            AuthorizedAmount = FromCents(payment.AuthorizedAmountCents),
            CapturedAmount = FromCents(payment.CapturedAmountCents),
            Fee = FromCents(payment.FeeCents),
            Net = FromCents(payment.NetCents),
            RefundedAmount = FromCents(payment.RefundedAmountCents),
            RefundableAmount = FromCents(payment.RefundableAmountCents),
            Currency = payment.Currency,
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage,
            Refunds = payment.Refunds
                .OrderBy(r => r.CreatedAt)
                .Select(ToRefundDto)
                .ToList()
        };
    }

    private static RefundDto ToRefundDto(RefundRecord refund)
    {
        return new RefundDto
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixRefundId,
            State = refund.State,
            Amount = FromCents(refund.AmountCents),
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt,
            FailureMessage = refund.FailureMessage
        };
    }

    private static SavedPaymentMethodDto ToSavedPaymentMethodDto(SavedPaymentMethod method)
    {
        return new SavedPaymentMethodDto
        {
            PaymentMethodId = method.Id,
            Brand = method.Brand,
            Last4 = method.Last4,
            ExpirationMonth = method.ExpirationMonth,
            ExpirationYear = method.ExpirationYear,
            CardholderName = method.CardholderName,
            Alias = method.Alias,
            CreatedAt = method.CreatedAt
        };
    }

    private sealed record EshopReference(
        string TransferId,
        int OrderId,
        string ReferenceType,
        int AmountCents,
        DateTimeOffset At);
}
