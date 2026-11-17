using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payment;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private readonly IRepository<Order> _orders;
    private readonly IRepository<CatalogItem> _catalogItems;
    private readonly IRepository<SavedCard> _savedCards;
    private readonly IFinixClient _finix;
    private readonly FinixOptions _options;
    private readonly IOrderOperationLock _orderLock;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orders,
        IRepository<CatalogItem> catalogItems,
        IRepository<SavedCard> savedCards,
        IFinixClient finix,
        FinixOptions options,
        IOrderOperationLock orderLock,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orders = orders;
        _catalogItems = catalogItems;
        _savedCards = savedCards;
        _finix = finix;
        _options = options;
        _orderLock = orderLock;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, CreateOrderCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required to place an order.");
        }

        if (command.Items == null || command.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one order item is required.");
        }

        var requestedItems = command.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CreateOrderItem
            {
                CatalogItemId = g.Key,
                Quantity = g.Sum(i => i.Quantity)
            })
            .ToList();

        if (requestedItems.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Every order item must have a positive catalog item id and quantity.");
        }

        var catalogIds = requestedItems.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItems.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var missing = catalogIds.Except(catalogItems.Select(c => c.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentOperationException(400, $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requested in requestedItems)
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var pictureUri = _uriComposer.ComposePicUri(catalogItem.PictureUri);
            if (string.IsNullOrWhiteSpace(pictureUri))
            {
                pictureUri = "images/products/placeholder.png";
            }

            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, requested.Quantity));
        }

        var address = ToAddress(command.ShippingAddress);
        var totalCents = ToCents(orderItems.Sum(i => i.UnitPrice * i.Units));
        var order = new Order(buyerId, address, orderItems, _options.Currency, totalCents);
        await _orders.AddAsync(order, cancellationToken);

        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} awaiting payment.", order.Id, buyerId);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId, int orderId, PayOrderCommand command, CancellationToken cancellationToken)
    {
        using var handle = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderOrThrowAsync(orderId, cancellationToken);

        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentOperationException(409, $"Order {orderId} was cancelled and can no longer be paid.");
        }

        if (order.Status is not OrderStatus.AwaitingPayment and not OrderStatus.PaymentFailed)
        {
            throw new PaymentOperationException(409, $"Order {orderId} is in status {order.Status} and cannot be paid.");
        }

        if (command.PaymentMethodId.HasValue && command.Card != null)
        {
            throw new PaymentOperationException(400, "Provide either card details or a saved paymentMethodId, not both.");
        }

        string sourceId;
        string? brand;
        string? last4;

        if (command.PaymentMethodId.HasValue)
        {
            var savedCard = await _savedCards.FirstOrDefaultAsync(
                new SavedCardByIdAndBuyerSpecification(command.PaymentMethodId.Value, buyerId),
                cancellationToken);

            if (savedCard == null || savedCard.IsDeleted)
            {
                throw new PaymentOperationException(404, $"Saved payment method {command.PaymentMethodId.Value} was not found.");
            }

            sourceId = savedCard.FinixPaymentInstrumentId;
            brand = savedCard.Brand;
            last4 = savedCard.Last4;
        }
        else
        {
            var card = command.Card ?? throw new PaymentOperationException(400, "Card details or a saved paymentMethodId is required.");
            ValidateCard(card);

            FinixPaymentInstrument instrument;
            try
            {
                var identity = await _finix.CreateIdentityAsync(card, cancellationToken);
                instrument = await _finix.CreatePaymentInstrumentAsync(identity.Id, card, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                order.MarkAuthorizationFailed(ex.Code ?? "PAYMENT_INSTRUMENT_FAILED", ex.Message);
                await _orders.UpdateAsync(order, cancellationToken);
                throw ToPaymentException(ex, "Card tokenization failed");
            }

            sourceId = instrument.Id;
            brand = instrument.Brand;
            last4 = instrument.LastFour;
        }

        var amountCents = order.ResolveTotalAmountCents();
        var currency = order.Currency ?? _options.Currency;
        var idempotencyId = $"eshop-{order.GetOrCreatePaymentIdempotencyKey()}-authorization";
        var tags = new Dictionary<string, string>
        {
            ["order_id"] = order.Id.ToString(),
            ["source"] = "eshoponweb"
        };

        FinixAuthorization authorization;
        try
        {
            authorization = await _finix.CreateAuthorizationAsync(
                sourceId,
                amountCents,
                currency,
                idempotencyId,
                tags,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            order.MarkAuthorizationFailed(ex.Code ?? "AUTHORIZATION_FAILED", ex.Message);
            await _orders.UpdateAsync(order, cancellationToken);
            throw ToPaymentException(ex, "Authorization failed");
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) || authorization.IsVoid)
        {
            var message = authorization.FailureMessage
                ?? $"Finix returned authorization state {authorization.State}.";
            order.MarkAuthorizationFailed(authorization.FailureCode ?? authorization.State, message);
            await _orders.UpdateAsync(order, cancellationToken);
            throw new PaymentOperationException(
                402,
                $"Authorization for order {orderId} did not succeed: {message}",
                authorization.FailureCode ?? authorization.State);
        }

        order.MarkAuthorized(
            sourceId,
            brand,
            last4,
            authorization.Id,
            authorization.State,
            authorization.ExpiresAt,
            DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Authorized order {OrderId} with Finix authorization {AuthorizationId} for {AmountCents} {Currency}.",
            order.Id,
            authorization.Id,
            amountCents,
            currency);

        return order;
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var handle = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderOrThrowAsync(orderId, cancellationToken);

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            return order;
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentOperationException(409, $"Order {orderId} was cancelled and cannot be fulfilled.");
        }

        if (order.Status == OrderStatus.AwaitingPayment)
        {
            throw new PaymentOperationException(409, $"Order {orderId} has not been paid yet. Authorize payment before fulfilment.");
        }

        if (order.Status == OrderStatus.PaymentFailed)
        {
            throw new PaymentOperationException(
                409,
                $"Order {orderId} has a failed payment ({order.PaymentFailureCode}: {order.PaymentFailureMessage}). Cancel it and ask the shopper to pay again.");
        }

        if (string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            throw new PaymentOperationException(409, $"Order {orderId} has no Finix authorization to capture.");
        }

        FinixAuthorization authorization;
        try
        {
            authorization = await _finix.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw ToPaymentException(ex, "Unable to retrieve the authorization from Finix");
        }

        if (string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            var expiresAt = authorization.ExpiresAt;
            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || authorization.IsVoid
                || (expiresAt.HasValue && expiresAt.Value <= DateTimeOffset.UtcNow))
            {
                var reason = authorization.FailureMessage
                    ?? $"authorization state is {authorization.State}, voided={authorization.IsVoid}, expires_at={expiresAt:O}";
                var message = $"Authorization {authorization.Id} for order {orderId} can no longer be captured ({reason}). "
                    + "Cancel the order and ask the shopper to pay again.";
                order.MarkCaptureFailed("AUTHORIZATION_NOT_CAPTURABLE", message);
                await _orders.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(409, message, "AUTHORIZATION_NOT_CAPTURABLE");
            }

            try
            {
                authorization = await _finix.CaptureAuthorizationAsync(
                    authorization.Id,
                    order.ResolveTotalAmountCents(),
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                var latest = await TryGetAuthorizationAsync(order.AuthorizationId, cancellationToken);
                if (latest != null && !string.IsNullOrWhiteSpace(latest.Transfer))
                {
                    authorization = latest;
                }
                else
                {
                    var message = $"Capture for order {orderId} failed: {ex.Message}. "
                        + "The authorization may have expired or been voided; cancel the order and ask the shopper to pay again.";
                    order.MarkCaptureFailed(ex.Code ?? "CAPTURE_FAILED", message);
                    await _orders.UpdateAsync(order, cancellationToken);
                    throw new PaymentOperationException(409, message, ex.Code ?? "CAPTURE_FAILED");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(authorization.Transfer))
        {
            var message = $"Finix did not return a capture transfer for authorization {authorization.Id}.";
            order.MarkCaptureFailed("CAPTURE_TRANSFER_MISSING", message);
            await _orders.UpdateAsync(order, cancellationToken);
            throw new PaymentOperationException(409, message, "CAPTURE_TRANSFER_MISSING");
        }

        var transfer = await _finix.GetTransferAsync(authorization.Transfer, cancellationToken);
        var fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
        var feeCents = fees.Sum(f => f.Amount);
        var capturedCents = transfer.Amount;
        var netCents = capturedCents - feeCents;

        order.MarkCaptured(
            transfer.Id,
            transfer.State,
            capturedCents,
            feeCents,
            netCents,
            DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Captured order {OrderId} with Finix transfer {TransferId}, amount {AmountCents}, fee {FeeCents}, net {NetCents}.",
            order.Id,
            transfer.Id,
            capturedCents,
            feeCents,
            netCents);

        return order;
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var handle = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderOrThrowAsync(orderId, cancellationToken);

        if (order.Status == OrderStatus.Cancelled)
        {
            return order;
        }

        if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
        {
            throw new PaymentOperationException(409, $"Order {orderId} has already been fulfilled and cannot be cancelled. Use a refund instead.");
        }

        if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            try
            {
                var authorization = await _finix.VoidAuthorizationAsync(order.AuthorizationId, cancellationToken);
                order.MarkAuthorized(
                    order.PaymentSourceId ?? string.Empty,
                    order.PaymentCardBrand,
                    order.PaymentCardLast4,
                    authorization.Id,
                    authorization.State,
                    authorization.ExpiresAt,
                    order.PaidAt ?? DateTimeOffset.UtcNow);
            }
            catch (FinixApiException ex)
            {
                if (ex.Message.Contains("captured", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PaymentOperationException(
                        409,
                        $"Order {orderId} has already been captured at Finix and cannot be cancelled. Use a refund instead.",
                        ex.Code);
                }

                throw ToPaymentException(ex, "Unable to void the authorization at Finix");
            }
        }

        order.MarkCancelled(DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId}; any held funds were released.", order.Id);
        return order;
    }

    public async Task<Refund> RefundAsync(int orderId, RefundCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "An idempotencyKey is required for refunds.");
        }

        using var handle = await _orderLock.AcquireAsync(orderId, cancellationToken);
        var order = await GetOrderOrThrowAsync(orderId, cancellationToken);

        var existing = order.FindRefund(command.IdempotencyKey);
        if (existing != null)
        {
            return existing;
        }

        if (order.Status is not OrderStatus.Fulfilled and not OrderStatus.PartiallyRefunded and not OrderStatus.Refunded)
        {
            throw new PaymentOperationException(409, $"Order {orderId} has not been fulfilled; only captured payments can be refunded.");
        }

        if (string.IsNullOrWhiteSpace(order.CaptureTransferId))
        {
            throw new PaymentOperationException(409, $"Order {orderId} has no captured Finix transfer to refund.");
        }

        var transfer = await _finix.GetTransferAsync(order.CaptureTransferId, cancellationToken);
        var fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
        var feeCents = fees.Sum(f => f.Amount);
        var capturedCents = order.CapturedAmountCents ?? transfer.Amount;
        order.UpdateCaptureDetails(transfer.State, capturedCents, feeCents, capturedCents - feeCents);

        var remainingCents = order.RemainingRefundableCents();
        if (remainingCents <= 0)
        {
            await _orders.UpdateAsync(order, cancellationToken);
            throw new PaymentOperationException(409, $"Order {orderId} has already been fully refunded.");
        }

        var amountCents = command.Amount.HasValue ? ToCents(command.Amount.Value) : remainingCents;
        if (amountCents <= 0)
        {
            throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
        }

        if (amountCents > remainingCents)
        {
            throw new PaymentOperationException(
                409,
                $"Refund amount {amountCents} cents exceeds the remaining refundable amount {remainingCents} cents for order {orderId}.");
        }

        if (amountCents < capturedCents && string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            transfer = await PollTransferAsync(transfer.Id, TimeSpan.FromSeconds(30), cancellationToken);
            if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    409,
                    $"Capture transfer {transfer.Id} is still PENDING at Finix. Partial refunds become available once Finix marks the transfer SUCCEEDED; retry in a minute.",
                    "CAPTURE_PENDING");
            }
        }

        FinixTransfer refundTransfer;
        try
        {
            refundTransfer = await _finix.CreateRefundAsync(
                transfer.Id,
                amountCents,
                $"eshop-{order.GetOrCreatePaymentIdempotencyKey()}-refund-{command.IdempotencyKey}",
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            if (ex.Message.Contains("PENDING", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(
                    409,
                    $"Finix cannot refund this capture yet: {ex.Message} Retry once the capture transfer has succeeded.",
                    "CAPTURE_PENDING");
            }

            if (ex.Message.Contains("greater than", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("exceed", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409, $"Finix rejected the refund amount: {ex.Message}", ex.Code);
            }

            throw ToPaymentException(ex, "Refund failed");
        }

        if (string.Equals(refundTransfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentOperationException(
                402,
                $"Finix reported the refund as FAILED: {refundTransfer.FailureMessage ?? "no failure message"}",
                refundTransfer.FailureCode ?? "REFUND_FAILED");
        }

        var refund = order.AddRefund(
            refundTransfer.Id,
            amountCents,
            refundTransfer.State,
            command.IdempotencyKey,
            DateTimeOffset.UtcNow);
        await _orders.UpdateAsync(order, cancellationToken);

        _logger.LogInformation(
            "Refunded order {OrderId} with Finix refund {RefundId} for {AmountCents} cents; order status is {Status}.",
            order.Id,
            refundTransfer.Id,
            amountCents,
            order.Status);

        return refund;
    }

    public async Task<IReadOnlyList<Order>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.CaptureTransferId)
                && string.Equals(order.CaptureStatus, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var transfer = await _finix.GetTransferAsync(order.CaptureTransferId, cancellationToken);
                    var fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
                    var feeCents = fees.Sum(f => f.Amount);
                    order.UpdateCaptureDetails(transfer.State, transfer.Amount, feeCents, transfer.Amount - feeCents);
                    await _orders.UpdateAsync(order, cancellationToken);
                }
                catch (FinixApiException ex)
                {
                    _logger.LogWarning(
                        "Could not refresh Finix capture {TransferId} for order {OrderId}: {Message}",
                        order.CaptureTransferId,
                        order.Id,
                        ex.Message);
                }
            }
        }

        return orders;
    }

    public async Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orders.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "The 'from' date-time must be before or equal to the 'to' date-time.");
        }

        var transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
        var orders = await _orders.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        var finixIds = transfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var entries = new List<ReconciliationEntry>();
        var eshopTransactionCount = 0;

        foreach (var transfer in transfers)
        {
            var order = orders.FirstOrDefault(o => string.Equals(o.CaptureTransferId, transfer.Id, StringComparison.Ordinal));
            var refundMatch = orders
                .SelectMany(o => o.Refunds.Select(r => new { Order = o, Refund = r }))
                .FirstOrDefault(x => string.Equals(x.Refund.FinixRefundId, transfer.Id, StringComparison.Ordinal));

            entries.Add(new ReconciliationEntry
            {
                Source = "FINIX",
                MatchStatus = order != null || refundMatch != null ? "MATCHED" : "FINIX_ONLY",
                FinixTransferId = transfer.Id,
                Type = transfer.Type,
                State = transfer.State,
                Currency = transfer.Currency,
                AmountCents = transfer.Amount,
                CreatedAt = transfer.CreatedAt,
                ParentTransferId = transfer.ParentTransfer,
                OrderId = order?.Id ?? refundMatch?.Order.Id,
                RefundId = refundMatch?.Refund.Id,
                FinixRefundId = refundMatch?.Refund.FinixRefundId,
                Note = order == null && refundMatch == null
                    ? "Finix has this transaction but eShop has no matching order or refund."
                    : null
            });
        }

        foreach (var order in orders)
        {
            if (!string.IsNullOrWhiteSpace(order.CaptureTransferId)
                && order.FulfilledAt.HasValue
                && order.FulfilledAt.Value >= from
                && order.FulfilledAt.Value <= to)
            {
                eshopTransactionCount++;

                if (!finixIds.Contains(order.CaptureTransferId))
                {
                    entries.Add(new ReconciliationEntry
                    {
                        Source = "ESHOP",
                        MatchStatus = "ESHOP_ONLY",
                        FinixTransferId = order.CaptureTransferId,
                        Type = "CAPTURE",
                        State = order.CaptureStatus,
                        Currency = order.Currency,
                        AmountCents = order.CapturedAmountCents,
                        CreatedAt = order.FulfilledAt,
                        OrderId = order.Id,
                        Note = "eShop recorded a capture that Finix did not return for this range."
                    });
                }
            }

            foreach (var refund in order.Refunds)
            {
                if (refund.CreatedAt >= from && refund.CreatedAt <= to)
                {
                    eshopTransactionCount++;

                    if (!finixIds.Contains(refund.FinixRefundId))
                    {
                        entries.Add(new ReconciliationEntry
                        {
                            Source = "ESHOP",
                            MatchStatus = "ESHOP_ONLY",
                            FinixTransferId = refund.FinixRefundId,
                            Type = "REVERSAL",
                            State = refund.Status,
                            Currency = order.Currency,
                            AmountCents = refund.AmountCents,
                            CreatedAt = refund.CreatedAt,
                            OrderId = order.Id,
                            RefundId = refund.Id,
                            FinixRefundId = refund.FinixRefundId,
                            Note = "eShop recorded a refund that Finix did not return for this range."
                        });
                    }
                }
            }
        }

        entries = entries
            .OrderBy(e => e.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            FinixTransactionCount = transfers.Count,
            EshopTransactionCount = eshopTransactionCount,
            MatchedCount = entries.Count(e => string.Equals(e.MatchStatus, "MATCHED", StringComparison.Ordinal)),
            FinixOnlyCount = entries.Count(e => string.Equals(e.MatchStatus, "FINIX_ONLY", StringComparison.Ordinal)),
            EshopOnlyCount = entries.Count(e => string.Equals(e.MatchStatus, "ESHOP_ONLY", StringComparison.Ordinal)),
            Entries = entries
        };
    }

    private async Task<Order> GetOrderOrThrowAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<FinixAuthorization?> TryGetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixApiException)
        {
            return null;
        }
    }

    private async Task<FinixTransfer> PollTransferAsync(string transferId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static PaymentOperationException ToPaymentException(FinixApiException ex, string prefix)
    {
        var statusCode = ex.StatusCode >= 500 ? 502 : 402;
        return new PaymentOperationException(
            statusCode,
            $"{prefix}: {ex.Message}",
            ex.Code,
            ex.LogRef);
    }

    private static Address ToAddress(PaymentAddress? address)
    {
        address ??= new PaymentAddress();
        return new Address(
            string.IsNullOrWhiteSpace(address.Line1) ? "123 Main St" : address.Line1,
            string.IsNullOrWhiteSpace(address.City) ? "San Francisco" : address.City,
            string.IsNullOrWhiteSpace(address.Region) ? "CA" : address.Region,
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country,
            string.IsNullOrWhiteSpace(address.PostalCode) ? "94105" : address.PostalCode);
    }

    private static long ToCents(decimal amount)
    {
        return checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static void ValidateCard(CardPaymentDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Count(char.IsDigit) < 12)
        {
            throw new PaymentOperationException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth < 1 || card.ExpirationMonth > 12)
        {
            throw new PaymentOperationException(400, "Card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentOperationException(400, "Card expiration year must not be in the past.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }
    }
}
