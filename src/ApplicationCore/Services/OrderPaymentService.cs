using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<SavedCard> _savedCardRepository;
    private readonly IRepository<FinixCustomer> _finixCustomerRepository;
    private readonly IFinixGateway _finix;
    private readonly FinixOptions _finixOptions;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<SavedCard> savedCardRepository,
        IRepository<FinixCustomer> finixCustomerRepository,
        IFinixGateway finix,
        FinixOptions finixOptions,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _savedCardRepository = savedCardRepository;
        _finixCustomerRepository = finixCustomerRepository;
        _finix = finix;
        _finixOptions = finixOptions;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        ShippingAddressInput shippingAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The caller could not be identified from the token.", "UNAUTHENTICATED");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one catalog item is required to place an order.", "EMPTY_ORDER");
        }

        var normalizedItems = items
            .Where(item => item is not null)
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new OrderLineInput
            {
                CatalogItemId = group.Key,
                Quantity = group.Sum(item => item.Quantity)
            })
            .ToList();

        if (normalizedItems.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Every order line needs a positive catalog item id and quantity.", "INVALID_ORDER_LINE");
        }

        var catalogItemIds = normalizedItems.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogRepository.ListAsync(new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogItemsById = catalogItems.ToDictionary(item => item.Id);

        var missingIds = catalogItemIds.Where(id => !catalogItemsById.ContainsKey(id)).ToArray();
        if (missingIds.Length > 0)
        {
            throw new PaymentOperationException(400,
                $"Catalog item(s) not found: {string.Join(", ", missingIds)}.", "CATALOG_ITEM_NOT_FOUND");
        }

        var orderItems = normalizedItems.Select(line =>
        {
            var catalogItem = catalogItemsById[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = ToAddress(shippingAddress);
        var order = new Order(buyerId, address, orderItems);
        order.SetCurrency(NormalizeCurrency(_finixOptions.Currency));

        await _orderRepository.AddAsync(order, cancellationToken);
        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} awaiting payment.", order.Id, buyerId);

        return order;
    }

    public async Task<Order?> GetOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            return null;
        }

        return order;
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithPaymentSpecification(buyerId), cancellationToken);
        foreach (var order in orders)
        {
            await RefreshFulfilmentIfNeededAsync(order, cancellationToken);
        }

        return orders;
    }

    public async Task<Order> PayOrderAsync(string buyerId, int orderId, PayOrderInput input,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has already been paid and fulfilled.", "ORDER_ALREADY_FULFILLED");
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} was cancelled and can no longer be paid.", "ORDER_CANCELLED");
            }

            // Idempotent: a repeated pay request returns the existing hold instead of
            // authorizing the shopper's card a second time.
            if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                return order;
            }

            if (input is null || (input.PaymentMethodId is null && input.Card is null))
            {
                throw new PaymentOperationException(400,
                    "Provide either card details or paymentMethodId to pay for this order.", "PAYMENT_DETAILS_REQUIRED");
            }

            var amountCents = ToCents(order.Total());
            if (amountCents <= 0)
            {
                throw new PaymentOperationException(400, "The order total must be greater than zero.", "INVALID_ORDER_TOTAL");
            }

            string paymentInstrumentId;
            int? savedPaymentMethodId = null;

            if (input.PaymentMethodId.HasValue)
            {
                var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
                    new SavedCardByIdAndOwnerSpecification(input.PaymentMethodId.Value, buyerId), cancellationToken);

                if (savedCard is null)
                {
                    throw new PaymentOperationException(404,
                        $"Saved payment method {input.PaymentMethodId.Value} was not found for this shopper.",
                        "PAYMENT_METHOD_NOT_FOUND");
                }

                paymentInstrumentId = savedCard.FinixPaymentInstrumentId;
                savedPaymentMethodId = savedCard.Id;
            }
            else
            {
                var card = input.Card!;
                ValidateCard(card);
                var identityId = await GetOrCreateFinixIdentityAsync(buyerId, cancellationToken);
                try
                {
                    var paymentInstrument = await _finix.CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
                    paymentInstrumentId = paymentInstrument.Id;
                }
                catch (FinixApiException ex)
                {
                    throw MapFinixException(ex, "Tokenizing the card", 402);
                }
            }

            FinixAuthorization authorization;
            var currency = string.IsNullOrWhiteSpace(order.Currency)
                ? NormalizeCurrency(_finixOptions.Currency)
                : order.Currency;
            order.SetCurrency(currency);
            try
            {
                authorization = await _finix.CreateAuthorizationAsync(
                    amountCents,
                    currency,
                    paymentInstrumentId,
                    $"eshop-order-{order.Id}",
                    $"eshop-order-{order.Id}-{order.OrderDate.ToUnixTimeMilliseconds()}-authorization",
                    cancellationToken);
            }
            catch (FinixApiException ex)
            {
                order.RecordPaymentFailure(DescribeFinixFailure(ex));
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw MapFinixException(ex, "Authorizing the payment", 402);
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                var failure = DescribeAuthorizationFailure(authorization);
                order.RecordPaymentFailure(failure);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(402, failure, authorization.FailureCode ?? "AUTHORIZATION_FAILED");
            }

            order.MarkAuthorized(authorization.Id, authorization.State, paymentInstrumentId,
                savedPaymentMethodId, amountCents, currency);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            _logger.LogInformation("Authorized {AmountCents} {Currency} for order {OrderId} (authorization {AuthorizationId}).",
                amountCents, order.Currency, order.Id, authorization.Id);

            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} was cancelled before fulfilment and has no hold to capture.", "ORDER_CANCELLED");
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has no authorized payment. The shopper must call POST /api/orders/{order.Id}/pay first.",
                    "ORDER_NOT_AUTHORIZED");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
            }
            catch (FinixApiException ex) when (ex.StatusCode == 404)
            {
                var message = UnusableAuthorizationMessage(order,
                    "Finix can no longer find the authorization hold. Ask the shopper to re-authorize the order with POST /api/orders/" + order.Id + "/pay.");
                order.MarkAuthorizationUnusable(message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(409, message, "AUTHORIZATION_NOT_CAPTURABLE");
            }
            catch (FinixApiException ex)
            {
                throw MapFinixException(ex, "Checking the authorization", 502);
            }

            var unusableReason = GetUnusableAuthorizationReason(authorization);
            if (unusableReason is not null)
            {
                var message = UnusableAuthorizationMessage(order, unusableReason);
                order.MarkAuthorizationUnusable(message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(409, message, "AUTHORIZATION_NOT_CAPTURABLE");
            }

            // If a previous capture already produced a transfer, treat the fulfilment as
            // idempotent and finish recording Finix's numbers instead of capturing twice.
            if (!string.IsNullOrWhiteSpace(authorization.TransferId))
            {
                var existingTransfer = await TryGetTransferAsync(authorization.TransferId!, cancellationToken);
                if (existingTransfer is not null)
                {
                    await RecordFulfilmentAsync(order, existingTransfer, cancellationToken);
                    return order;
                }
            }

            var captureAmountCents = order.AuthorizedAmountCents ?? ToCents(order.Total());
            FinixAuthorization capture;
            try
            {
                capture = await _finix.CaptureAuthorizationAsync(order.AuthorizationId, captureAmountCents, cancellationToken);
            }
            catch (FinixApiException ex) when (ex.StatusCode is 404 or 409 or 422)
            {
                var message = UnusableAuthorizationMessage(order,
                    $"Finix could not capture the hold ({DescribeFinixFailure(ex)}). Ask the shopper to re-authorize the order with POST /api/orders/{order.Id}/pay.");
                order.MarkAuthorizationUnusable(message);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw new PaymentOperationException(409, message, "AUTHORIZATION_NOT_CAPTURABLE");
            }
            catch (FinixApiException ex)
            {
                order.RecordPaymentFailure(DescribeFinixFailure(ex));
                await _orderRepository.UpdateAsync(order, cancellationToken);
                throw MapFinixException(ex, "Capturing the payment", 502);
            }

            var captureTransferId = capture.TransferId;
            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                var refreshed = await _finix.GetAuthorizationAsync(order.AuthorizationId, cancellationToken);
                captureTransferId = refreshed.TransferId;
            }

            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                throw new PaymentOperationException(502,
                    "Finix captured the payment but did not return a capture transfer id. Re-run fulfilment; the order can be reconciled by authorization id.",
                    "CAPTURE_TRANSFER_MISSING");
            }

            var transfer = await TryGetTransferAsync(captureTransferId!, cancellationToken)
                ?? new FinixTransfer
                {
                    Id = captureTransferId!,
                    State = capture.State,
                    Amount = capture.Amount,
                    Currency = capture.Currency
                };

            await RecordFulfilmentAsync(order, transfer, cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithPaymentByIdSpec(orderId), cancellationToken);
            if (order is null)
            {
                throw new PaymentOperationException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has already been fulfilled. Use POST /api/orders/{order.Id}/refunds to return the money.",
                    "ORDER_ALREADY_FULFILLED");
            }

            string? note = null;
            if (order.Status == OrderStatus.Authorized && !string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                try
                {
                    var voided = await _finix.VoidAuthorizationAsync(order.AuthorizationId, cancellationToken);
                    order.UpdateAuthorizationState(voided.IsVoid == true || voided.VoidState is not null
                        ? "VOIDED"
                        : voided.State);
                    note = voided.VoidState is not null &&
                           !string.Equals(voided.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                        ? $"Finix reported the void state as {voided.VoidState}."
                        : null;
                }
                catch (FinixApiException ex) when (ex.StatusCode == 404)
                {
                    note = "Finix no longer had the authorization hold; no funds were captured.";
                }
                catch (FinixApiException ex)
                {
                    throw MapFinixException(ex, "Releasing the authorization hold", 409);
                }
            }

            order.MarkCancelled(note);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Cancelled order {OrderId} before fulfilment.", order.Id);

            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(Order Order, PaymentRefund Refund)> RefundOrderAsync(string buyerId, int orderId,
        RefundInput input, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOwnedOrderAsync(buyerId, orderId, cancellationToken);

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has not been fulfilled, so there is no captured payment to refund.",
                    "ORDER_NOT_REFUNDABLE");
            }

            if (string.IsNullOrWhiteSpace(order.CaptureTransferId))
            {
                throw new PaymentOperationException(409,
                    $"Order {order.Id} has no capture transfer id recorded, so it cannot be refunded.", "CAPTURE_TRANSFER_MISSING");
            }

            var idempotencyKey = input?.IdempotencyKey;
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                idempotencyKey = Guid.NewGuid().ToString("N");
            }

            var existingRefund = order.FindRefundByIdempotencyKey(idempotencyKey);
            if (existingRefund is not null)
            {
                return (order, existingRefund);
            }

            var capturedCents = order.CapturedAmountCents ?? 0;
            var remainingCents = capturedCents - order.RefundedAmountCents;
            var amountCents = input?.ResolveAmountCents() ?? remainingCents;

            if (amountCents <= 0)
            {
                throw new PaymentOperationException(400, "The refund amount must be greater than zero.", "INVALID_REFUND_AMOUNT");
            }

            if (amountCents > remainingCents)
            {
                throw new PaymentOperationException(409,
                    $"The refund amount ({amountCents} cents) exceeds the refundable balance ({remainingCents} cents) of the captured payment.",
                    "REFUND_EXCEEDS_CAPTURED");
            }

            FinixTransfer refundTransfer;
            try
            {
                refundTransfer = await _finix.CreateRefundAsync(order.CaptureTransferId, amountCents, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw MapFinixException(ex, "Refunding the captured payment", 409);
            }

            var refund = order.AddRefund(refundTransfer.Id, amountCents, refundTransfer.State,
                idempotencyKey, refundTransfer.CreatedAt ?? DateTimeOffset.UtcNow);

            await _orderRepository.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Refunded {AmountCents} cents on order {OrderId} (refund {RefundId}).",
                amountCents, order.Id, refund.RefundId);

            return (order, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ReconciliationResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "'from' must be before or equal to 'to'.", "INVALID_DATE_RANGE");
        }

        IReadOnlyList<FinixTransfer> transfers;
        IReadOnlyList<FinixAuthorization> authorizations;
        try
        {
            transfers = await _finix.ListTransfersAsync(from, to, cancellationToken);
            authorizations = await _finix.ListAuthorizationsAsync(from, to, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw MapFinixException(ex, "Reading Finix transactions", 502);
        }

        var orders = await _orderRepository.ListAsync(new OrdersWithPaymentDataSpecification(), cancellationToken);

        var result = new ReconciliationResult { From = from, To = to };

        var transferTransactions = transfers.Select(transfer => new ReconciliationFinixTransaction
        {
            Id = transfer.Id,
            RecordType = "TRANSFER",
            Type = transfer.Type,
            OperationKey = transfer.OperationKey,
            State = transfer.State,
            AmountCents = transfer.Amount,
            Currency = transfer.Currency,
            CreatedAt = transfer.CreatedAt,
            ParentTransferId = transfer.ParentTransfer
        }).ToList();

        var authorizationTransactions = authorizations.Select(authorization => new ReconciliationFinixTransaction
        {
            Id = authorization.Id,
            RecordType = "AUTHORIZATION",
            Type = "AUTHORIZATION",
            OperationKey = "AUTHORIZATION",
            State = authorization.State,
            AmountCents = authorization.Amount,
            Currency = authorization.Currency,
            CreatedAt = authorization.CreatedAt,
            ParentTransferId = null
        }).ToList();

        result.FinixTransactions.AddRange(transferTransactions);
        result.FinixTransactions.AddRange(authorizationTransactions);
        result.FinixTransactions = result.FinixTransactions
            .OrderBy(transaction => transaction.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        var transactionsById = result.FinixTransactions
            .GroupBy(transaction => transaction.Id)
            .ToDictionary(group => group.Key, group => group.First());

        var transactionIds = new HashSet<string>(result.FinixTransactions.Select(transaction => transaction.Id),
            StringComparer.Ordinal);

        var relevantOrders = orders.Where(order =>
            (order.OrderDate >= from && order.OrderDate <= to)
            || (!string.IsNullOrWhiteSpace(order.AuthorizationId) && transactionIds.Contains(order.AuthorizationId!))
            || (!string.IsNullOrWhiteSpace(order.CaptureTransferId) && transactionIds.Contains(order.CaptureTransferId!))
            || order.Refunds.Any(refund => transactionIds.Contains(refund.RefundId))).ToList();

        foreach (var order in relevantOrders)
        {
            var refundIds = order.Refunds.Select(refund => refund.RefundId).ToList();
            var orderReport = new ReconciliationOrder
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status.ToString(),
                Total = order.Total(),
                TotalCents = ToCents(order.Total()),
                Currency = order.Currency,
                OrderDate = order.OrderDate,
                AuthorizationId = order.AuthorizationId,
                CaptureTransferId = order.CaptureTransferId,
                RefundIds = refundIds
            };

            if (!string.IsNullOrWhiteSpace(order.AuthorizationId) &&
                transactionsById.TryGetValue(order.AuthorizationId!, out var authorizationTransaction))
            {
                orderReport.MatchedFinixTransactionIds.Add(authorizationTransaction.Id);
                authorizationTransaction.MatchedOrderId = order.Id;
                authorizationTransaction.IsMatched = true;
            }

            if (!string.IsNullOrWhiteSpace(order.CaptureTransferId) &&
                transactionsById.TryGetValue(order.CaptureTransferId!, out var captureTransaction))
            {
                orderReport.MatchedFinixTransactionIds.Add(captureTransaction.Id);
                captureTransaction.MatchedOrderId = order.Id;
                captureTransaction.IsMatched = true;
            }

            foreach (var refundId in refundIds)
            {
                if (transactionsById.TryGetValue(refundId, out var refundTransaction))
                {
                    orderReport.MatchedFinixTransactionIds.Add(refundTransaction.Id);
                    refundTransaction.MatchedOrderId = order.Id;
                    refundTransaction.IsMatched = true;
                }
            }

            // A refund that eShop did not record can still be tied back through the parent
            // capture transfer, which keeps the report useful after a partial data loss.
            if (!string.IsNullOrWhiteSpace(order.CaptureTransferId))
            {
                foreach (var transaction in result.FinixTransactions.Where(transaction =>
                             !transaction.IsMatched &&
                             string.Equals(transaction.ParentTransferId, order.CaptureTransferId, StringComparison.Ordinal)))
                {
                    orderReport.MatchedFinixTransactionIds.Add(transaction.Id);
                    transaction.MatchedOrderId = order.Id;
                    transaction.IsMatched = true;
                }
            }

            orderReport.MatchedFinixTransactionIds = orderReport.MatchedFinixTransactionIds.Distinct().ToList();
            result.Orders.Add(orderReport);
        }

        result.UnmatchedFinixTransactions = result.FinixTransactions.Where(transaction => !transaction.IsMatched).ToList();
        result.UnmatchedOrders = result.Orders
            .Where(order => !string.Equals(order.Status, OrderStatus.AwaitingPayment.ToString(), StringComparison.Ordinal)
                            && !order.IsMatched)
            .ToList();

        return result;
    }

    public async Task<SavedCard> SaveCardAsync(string buyerId, CardDetails card, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "The caller could not be identified from the token.", "UNAUTHENTICATED");
        }

        ValidateCard(card);
        var identityId = await GetOrCreateFinixIdentityAsync(buyerId, cancellationToken);

        FinixPaymentInstrument paymentInstrument;
        try
        {
            paymentInstrument = await _finix.CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw MapFinixException(ex, "Saving the card", 402);
        }

        var savedCard = new SavedCard(
            buyerId,
            paymentInstrument.Id,
            paymentInstrument.Brand,
            paymentInstrument.LastFour,
            paymentInstrument.ExpirationMonth ?? card.ExpirationMonth,
            paymentInstrument.ExpirationYear ?? card.ExpirationYear,
            paymentInstrument.Name ?? card.Name);

        await _savedCardRepository.AddAsync(savedCard, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for buyer {BuyerId}.", savedCard.Id, buyerId);

        return savedCard;
    }

    public async Task<IReadOnlyList<SavedCard>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        return await _savedCardRepository.ListAsync(new SavedCardsByOwnerSpecification(buyerId), cancellationToken);
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var savedCard = await _savedCardRepository.FirstOrDefaultAsync(
            new SavedCardByIdAndOwnerSpecification(paymentMethodId, buyerId), cancellationToken);

        if (savedCard is null)
        {
            throw new PaymentOperationException(404,
                $"Saved payment method {paymentMethodId} was not found for this shopper.", "PAYMENT_METHOD_NOT_FOUND");
        }

        try
        {
            await _finix.DisablePaymentInstrumentAsync(savedCard.FinixPaymentInstrumentId, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.StatusCode == 404)
        {
            // Already gone at Finix; still remove it locally so it cannot be reused.
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Finix could not disable payment instrument {PaymentInstrumentId}: {Message}. Removing it locally so it cannot be reused.",
                savedCard.FinixPaymentInstrumentId, ex.Message);
        }

        savedCard.MarkDeleted();
        await _savedCardRepository.UpdateAsync(savedCard, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for buyer {BuyerId}.", paymentMethodId, buyerId);
    }

    private async Task<Order> GetOwnedOrderAsync(string buyerId, int orderId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(buyerId, orderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.", "ORDER_NOT_FOUND");
        }

        return order;
    }

    private async Task RefreshFulfilmentIfNeededAsync(Order order, CancellationToken cancellationToken)
    {
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(order.CaptureTransferId))
        {
            return;
        }

        if (string.Equals(order.CaptureTransferState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
            && order.FinixFeeCents is > 0)
        {
            return;
        }

        var transfer = await TryGetTransferAsync(order.CaptureTransferId, cancellationToken);
        if (transfer is null)
        {
            return;
        }

        var feeCents = order.FinixFeeCents ?? 0;
        try
        {
            var fees = await _finix.ListFeesForTransferAsync(order.CaptureTransferId, cancellationToken);
            var latestFee = (long)Math.Round(fees.Sum(fee => fee.Amount ?? 0m), MidpointRounding.AwayFromZero);
            if (latestFee > 0 || feeCents == 0)
            {
                feeCents = latestFee;
            }
        }
        catch (FinixApiException)
        {
            // Keep the last known fee if Finix cannot be reached on a read.
        }

        var capturedCents = transfer.Amount
                            ?? order.CapturedAmountCents
                            ?? order.AuthorizedAmountCents
                            ?? ToCents(order.Total());

        if (!string.Equals(transfer.State, order.CaptureTransferState, StringComparison.OrdinalIgnoreCase)
            || capturedCents != order.CapturedAmountCents
            || feeCents != order.FinixFeeCents)
        {
            order.UpdateCaptureOutcome(transfer.State, capturedCents, feeCents, capturedCents - feeCents);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }
    }

    private async Task RecordFulfilmentAsync(Order order, FinixTransfer transfer, CancellationToken cancellationToken)
    {
        // Finix can return the capture while the transfer is still PENDING. Its fees are
        // created when the transfer finishes, so wait for Finix's own numbers instead of
        // recording a misleading zero fee.
        var transferDeadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (!IsTerminalTransferState(transfer.State) && DateTimeOffset.UtcNow < transferDeadline)
        {
            await Task.Delay(2000, cancellationToken);
            var refreshed = await TryGetTransferAsync(transfer.Id, cancellationToken);
            if (refreshed is not null)
            {
                transfer = refreshed;
            }
        }

        var capturedCents = transfer.Amount
                            ?? order.AuthorizedAmountCents
                            ?? ToCents(order.Total());

        if (transfer.State is not null &&
            (transfer.State.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
             transfer.State.Equals("CANCELED", StringComparison.OrdinalIgnoreCase) ||
             transfer.State.Equals("RETURNED", StringComparison.OrdinalIgnoreCase)))
        {
            var message = UnusableAuthorizationMessage(order,
                $"Finix reports the capture transfer is in state {transfer.State}." +
                (string.IsNullOrWhiteSpace(transfer.FailureMessage) ? string.Empty : $" {transfer.FailureMessage}"));
            order.MarkAuthorizationUnusable(message);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            throw new PaymentOperationException(409, message, "CAPTURE_FAILED");
        }

        long feeCents = 0;
        var feeDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (true)
        {
            try
            {
                var fees = await _finix.ListFeesForTransferAsync(transfer.Id, cancellationToken);
                feeCents = (long)Math.Round(fees.Sum(fee => fee.Amount ?? 0m), MidpointRounding.AwayFromZero);
                if (feeCents > 0)
                {
                    break;
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not read Finix fees for transfer {TransferId}: {Message}. Recording fee as zero.",
                    transfer.Id, ex.Message);
                break;
            }

            if (DateTimeOffset.UtcNow >= feeDeadline)
            {
                break;
            }

            await Task.Delay(500, cancellationToken);
        }

        var netCents = capturedCents - feeCents;

        order.MarkFulfilled(transfer.Id, transfer.State, capturedCents, feeCents, netCents);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Fulfilled order {OrderId}: captured {CapturedCents} cents, fee {FeeCents} cents, net {NetCents} cents.",
            order.Id, capturedCents, feeCents, netCents);
    }

    private static bool IsTerminalTransferState(string? state)
    {
        return state is not null &&
               (state.Equals("SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("FAILED", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("CANCELED", StringComparison.OrdinalIgnoreCase) ||
                state.Equals("RETURNED", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<FinixTransfer?> TryGetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.GetTransferAsync(transferId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not read transfer {TransferId}: {Message}.", transferId, ex.Message);
            return null;
        }
    }

    private async Task<string> GetOrCreateFinixIdentityAsync(string ownerId, CancellationToken cancellationToken)
    {
        var existing = await _finixCustomerRepository.FirstOrDefaultAsync(
            new FinixCustomerByOwnerSpecification(ownerId), cancellationToken);
        if (existing is not null)
        {
            return existing.IdentityId;
        }

        var gate = CustomerLocks.GetOrAdd(ownerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _finixCustomerRepository.FirstOrDefaultAsync(
                new FinixCustomerByOwnerSpecification(ownerId), cancellationToken);
            if (existing is not null)
            {
                return existing.IdentityId;
            }

            var email = ownerId.Contains('@') ? ownerId : $"{SanitizeForEmail(ownerId)}@example.com";
            string identityId;
            try
            {
                identityId = await _finix.CreateBuyerIdentityAsync(email, null, null, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                throw MapFinixException(ex, "Creating the Finix buyer identity", 502);
            }

            var customer = new FinixCustomer(ownerId, identityId);
            await _finixCustomerRepository.AddAsync(customer, cancellationToken);
            return identityId;
        }
        finally
        {
            gate.Release();
        }
    }

    private static void ValidateCard(CardDetails card)
    {
        if (card is null)
        {
            throw new PaymentOperationException(400, "Card details are required.", "CARD_DETAILS_REQUIRED");
        }

        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentOperationException(400, "The card number is required.", "CARD_NUMBER_REQUIRED");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "The card expiration month must be between 1 and 12.", "INVALID_CARD_EXPIRATION");
        }

        if (card.ExpirationYear < DateTime.UtcNow.Year)
        {
            throw new PaymentOperationException(400, "The card expiration year is in the past.", "INVALID_CARD_EXPIRATION");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            card.Name = "eShop Shopper";
        }
    }

    private static Address ToAddress(ShippingAddressInput? input)
    {
        if (input is null)
        {
            return new Address("N/A", "N/A", "NA", "USA", "00000");
        }

        return new Address(
            string.IsNullOrWhiteSpace(input.Street) ? "N/A" : input.Street,
            string.IsNullOrWhiteSpace(input.City) ? "N/A" : input.City,
            string.IsNullOrWhiteSpace(input.State) ? "NA" : input.State,
            string.IsNullOrWhiteSpace(input.Country) ? "USA" : input.Country,
            string.IsNullOrWhiteSpace(input.ZipCode) ? "00000" : input.ZipCode);
    }

    private static string? GetUnusableAuthorizationReason(FinixAuthorization authorization)
    {
        if (authorization.IsVoid == true)
        {
            return "Finix reports the authorization hold was voided.";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            return $"Finix reports the authorization hold is in state {authorization.State ?? "unknown"}.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"The authorization hold expired at {authorization.ExpiresAt.Value:O}.";
        }

        return null;
    }

    private static string UnusableAuthorizationMessage(Order order, string reason)
    {
        return $"The authorization hold for order {order.Id} can no longer be captured. {reason} " +
               $"Ask the shopper to re-authorize the order with POST /api/orders/{order.Id}/pay, then run fulfilment again.";
    }

    private static string DescribeAuthorizationFailure(FinixAuthorization authorization)
    {
        var code = string.IsNullOrWhiteSpace(authorization.FailureCode) ? "AUTHORIZATION_FAILED" : authorization.FailureCode;
        var message = string.IsNullOrWhiteSpace(authorization.FailureMessage)
            ? "Finix did not approve the card authorization."
            : authorization.FailureMessage;
        return $"Card authorization failed ({code}): {message}";
    }

    private static string DescribeFinixFailure(FinixApiException exception)
    {
        var code = string.IsNullOrWhiteSpace(exception.Code) ? "FINIX_ERROR" : exception.Code;
        return $"{code}: {exception.Message}";
    }

    private static PaymentOperationException MapFinixException(FinixApiException exception, string operation, int declineStatus)
    {
        var statusCode = exception.StatusCode switch
        {
            404 => 404,
            409 => 409,
            400 or 402 or 422 => declineStatus,
            401 or 403 => 502,
            >= 500 => 502,
            _ => declineStatus
        };

        var detail = string.IsNullOrWhiteSpace(exception.Message) ? "Finix returned an error." : exception.Message;
        return new PaymentOperationException(statusCode, $"{operation} failed: {detail}",
            string.IsNullOrWhiteSpace(exception.Code) ? "FINIX_ERROR" : exception.Code);
    }

    private static string SanitizeForEmail(string value)
    {
        var sanitized = new string(value.Select(character =>
            char.IsLetterOrDigit(character) ? character : '.').ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "shopper" : sanitized;
    }

    public static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static string NormalizeCurrency(string? currency)
    {
        return string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
    }
}
