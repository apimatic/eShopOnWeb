using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.Ordinal);

    private readonly IRepository<Order> _orders;
    private readonly IRepository<Refund> _refunds;
    private readonly IRepository<SavedPaymentMethod> _cards;
    private readonly IRepository<PaymentCustomer> _customers;
    private readonly IRepository<CatalogItem> _catalog;
    private readonly IFinixGateway _finix;
    private readonly IUriComposer _uriComposer;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(IRepository<Order> orders,
        IRepository<Refund> refunds,
        IRepository<SavedPaymentMethod> cards,
        IRepository<PaymentCustomer> customers,
        IRepository<CatalogItem> catalog,
        IFinixGateway finix,
        IUriComposer uriComposer,
        IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _orders = orders;
        _refunds = refunds;
        _cards = cards;
        _customers = customers;
        _catalog = catalog;
        _finix = finix;
        _uriComposer = uriComposer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderData data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(data.BuyerId))
        {
            throw new PaymentValidationException("The caller identity is missing.");
        }

        if (data.Items is null || data.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one order item is required.");
        }

        var requested = data.Items
            .GroupBy(item => item.CatalogItemId)
            .Select(group => new OrderItemInput(group.Key, group.Sum(item => item.Quantity)))
            .ToList();

        if (requested.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new PaymentValidationException("Catalog item ids and quantities must be positive.");
        }

        var ids = requested.Select(item => item.CatalogItemId).ToArray();
        var catalogItems = await _catalog.ListAsync(new CatalogItemsSpecification(ids), cancellationToken);
        var missing = ids.Except(catalogItems.Select(item => item.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requestItem in requested)
        {
            var catalogItem = catalogItems.First(item => item.Id == requestItem.CatalogItemId);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requestItem.Quantity));
        }

        var address = data.ShipToAddress is null
            ? new Address("N/A", "N/A", "N/A", "USA", "00000")
            : new Address(data.ShipToAddress.Street,
                data.ShipToAddress.City,
                data.ShipToAddress.State,
                data.ShipToAddress.Country,
                data.ShipToAddress.ZipCode);

        var order = new Order(data.BuyerId, address, orderItems);
        order.SetCurrency(_options.Currency);
        await _orders.AddAsync(order, cancellationToken);
        _logger.LogInformation("Created order {OrderId} for {BuyerId} with {ItemCount} line(s)",
            order.Id,
            data.BuyerId,
            orderItems.Count);
        return await MapOrderAsync(order, cancellationToken);
    }

    public async Task<OrderDto> PayOrderAsync(PayOrderData data, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(data.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetBuyerOrderAsync(data.OrderId, data.BuyerId, cancellationToken);

            if (order.Status is OrderStatuses.Authorized
                or OrderStatuses.Fulfilled
                or OrderStatuses.PartiallyRefunded
                or OrderStatuses.Refunded)
            {
                return await MapOrderAsync(order, cancellationToken);
            }

            if (order.Status == OrderStatuses.Cancelled)
            {
                throw new PaymentConflictException("This order was cancelled and can no longer be paid.");
            }

            if (order.Status != OrderStatuses.AwaitingPayment)
            {
                throw new PaymentConflictException($"Order is in state '{order.Status}' and cannot be paid.");
            }

            string source;
            string identityId;
            int? savedPaymentMethodId = null;

            if (data.PaymentMethodId.HasValue)
            {
                var savedCard = await _cards.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpecification(data.PaymentMethodId.Value, data.BuyerId),
                    cancellationToken);
                if (savedCard is null)
                {
                    throw new PaymentNotFoundException("Saved payment method not found.");
                }

                source = savedCard.FinixPaymentInstrumentId;
                identityId = savedCard.FinixIdentityId;
                savedPaymentMethodId = savedCard.Id;
            }
            else if (data.Card is not null)
            {
                var customer = await EnsureCustomerAsync(data.BuyerId, data.Card.Name, cancellationToken);
                var card = await _finix.CreateCardAsync(customer.FinixIdentityId, data.Card, cancellationToken);
                source = card.Id;
                identityId = customer.FinixIdentityId;
            }
            else
            {
                throw new PaymentValidationException("Provide either card details or a saved paymentMethodId.");
            }

            var amount = order.TotalCents();
            var currency = string.IsNullOrWhiteSpace(order.Currency) ? _options.Currency : order.Currency!;
            order.SetCurrency(currency);

            var idempotencyId = $"eshop-order-{order.Id}-authorization-{order.AuthorizationIdempotencyKey}";
            FinixAuthorization authorization;
            try
            {
                authorization = await _finix.AuthorizeAsync(_options.MerchantId,
                    currency,
                    amount,
                    source,
                    idempotencyId,
                    identityId,
                    cancellationToken);
            }
            catch (FinixDuplicateAuthorizationException duplicate)
            {
                authorization = await _finix.GetAuthorizationAsync(duplicate.AuthorizationId, cancellationToken);
            }

            if (authorization.IsVoid == true || !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                var detail = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                    ? authorization.FailureCode
                    : authorization.FailureMessage;
                throw new PaymentConflictException(
                    $"Finix did not authorize order {order.Id} (state {authorization.State ?? "UNKNOWN"}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}")}). No funds were held.");
            }

            var authorizedAmount = authorization.Amount ?? amount;
            if (authorizedAmount != amount)
            {
                await TryVoidAsync(authorization.Id, cancellationToken);
                throw new PaymentGatewayException(
                    $"Finix authorized {authorizedAmount} cents but the order total is {amount} cents. The hold was released.");
            }

            order.MarkAuthorized(authorization.Id,
                authorization.State ?? "SUCCEEDED",
                authorizedAmount,
                currency,
                source,
                savedPaymentMethodId,
                authorization.ExpiresAt);
            await _orders.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Order {OrderId} authorized with Finix authorization {AuthorizationId} for {Amount} cents",
                order.Id,
                authorization.Id,
                authorizedAmount);
            return await MapOrderAsync(order, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status is OrderStatuses.Fulfilled
                or OrderStatuses.PartiallyRefunded
                or OrderStatuses.Refunded)
            {
                return await MapOrderAsync(order, cancellationToken);
            }

            if (order.Status == OrderStatuses.Cancelled)
            {
                throw new PaymentConflictException("This order was cancelled and cannot be fulfilled.");
            }

            if (order.Status != OrderStatuses.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                throw new PaymentConflictException("This order has no authorization to capture. The shopper must pay for the order first.");
            }

            var authorization = await _finix.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
            var unusableReason = DescribeUnusableAuthorization(authorization);
            if (unusableReason is not null)
            {
                order.UpdateAuthorizationState(authorization.State ?? "UNKNOWN");
                await _orders.UpdateAsync(order, cancellationToken);
                throw new PaymentConflictException(unusableReason);
            }

            var captureAmount = order.AuthorizedAmount ?? order.TotalCents();
            FinixAuthorization captured;
            try
            {
                captured = await _finix.CaptureAsync(order.AuthorizationId!, captureAmount, cancellationToken);
            }
            catch (PaymentGatewayException exception)
            {
                throw new PaymentConflictException(
                    $"Finix could not capture authorization {order.AuthorizationId}: {exception.Message}. The hold may have expired or been voided; cancel the order and ask the shopper to pay again.");
            }

            var transferId = captured.TransferId;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var refreshed = await _finix.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
                transferId = refreshed.TransferId;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentGatewayException(
                    $"Finix captured authorization {order.AuthorizationId} but did not return a capture transfer id.");
            }

            var transfer = await WaitForCaptureSettlementAsync(transferId!, cancellationToken);
            if (transfer.State is "FAILED" or "CANCELED" or "RETURNED")
            {
                throw new PaymentConflictException(
                    $"Finix reported capture transfer {transferId} in state {transfer.State}{(string.IsNullOrWhiteSpace(transfer.FailureMessage) ? string.Empty : $": {transfer.FailureMessage}")}. The money was not captured; review the authorization in Finix and ask the shopper to pay again if needed.");
            }

            var capturedAmount = transfer.Amount ?? captureAmount;
            var fee = transfer.Fee ?? 0;
            var net = capturedAmount - fee;

            order.MarkCaptured(transferId!,
                transfer.State ?? captured.State ?? "PENDING",
                capturedAmount,
                fee,
                net);
            await _orders.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Order {OrderId} fulfilled with Finix transfer {TransferId}: captured {Captured}, fee {Fee}, net {Net}",
                order.Id,
                transferId,
                capturedAmount,
                fee,
                net);
            return await MapOrderAsync(order, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderDto> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatuses.Cancelled)
            {
                return await MapOrderAsync(order, cancellationToken);
            }

            if (order.Status is OrderStatuses.Fulfilled or OrderStatuses.PartiallyRefunded or OrderStatuses.Refunded)
            {
                throw new PaymentConflictException("This order has already been captured. Use the refunds endpoint instead of cancelling.");
            }

            if (!string.IsNullOrWhiteSpace(order.AuthorizationId))
            {
                try
                {
                    var authorization = await _finix.VoidAsync(order.AuthorizationId!, cancellationToken);
                    order.UpdateAuthorizationState(authorization.State ?? "VOIDED");
                }
                catch (PaymentGatewayException exception)
                {
                    var authorization = await _finix.GetAuthorizationAsync(order.AuthorizationId!, cancellationToken);
                    if (authorization.IsVoid != true
                        && !string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PaymentConflictException(
                            $"Finix could not release authorization {order.AuthorizationId}: {exception.Message}. The shopper's funds may still be held; retry the cancellation or review the authorization in Finix.");
                    }

                    order.UpdateAuthorizationState(authorization.State ?? "VOIDED");
                }
            }

            order.MarkCancelled("Cancelled before fulfilment");
            await _orders.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Order {OrderId} cancelled", order.Id);
            return await MapOrderAsync(order, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundDto> RefundOrderAsync(RefundOrderData data, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(data.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotencyKey is required for refunds.");
        }

        if (data.IdempotencyKey.Length > 256)
        {
            throw new PaymentValidationException("The idempotencyKey must be 256 characters or fewer.");
        }

        var gate = OrderLocks.GetOrAdd(data.OrderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(data.OrderId, cancellationToken);
            if (!data.IsAdministrator && !string.Equals(order.BuyerId, data.BuyerId, StringComparison.Ordinal))
            {
                throw new PaymentNotFoundException("Order not found.");
            }

            if (order.Status is not (OrderStatuses.Fulfilled or OrderStatuses.PartiallyRefunded or OrderStatuses.Refunded)
                || string.IsNullOrWhiteSpace(order.CaptureTransferId))
            {
                throw new PaymentConflictException("This order has no captured payment to refund.");
            }

            var existingRefunds = await _refunds.ListAsync(new RefundsByOrderSpecification(order.Id), cancellationToken);
            var duplicate = existingRefunds.FirstOrDefault(refund =>
                string.Equals(refund.IdempotencyKey, data.IdempotencyKey, StringComparison.Ordinal));
            if (duplicate is not null)
            {
                return MapRefund(duplicate);
            }

            var remaining = order.RefundableAmount();
            if (remaining <= 0)
            {
                throw new PaymentConflictException("This order has already been fully refunded.");
            }

            var amount = data.AmountCents ?? remaining;
            if (amount <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (amount > int.MaxValue)
            {
                throw new PaymentValidationException("The refund amount is too large.");
            }

            if (amount > remaining)
            {
                throw new PaymentConflictException(
                    $"The refund amount {amount} cents exceeds the refundable amount {remaining} cents for this order.");
            }

            var capture = await WaitForCaptureSettlementAsync(order.CaptureTransferId!, cancellationToken);
            if (capture.State is not ("SUCCEEDED" or "PENDING"))
            {
                throw new PaymentConflictException(
                    $"Capture transfer {order.CaptureTransferId} is in state {capture.State ?? "UNKNOWN"} and cannot be refunded.");
            }

            FinixTransfer refundTransfer;
            try
            {
                refundTransfer = await _finix.RefundAsync(order.CaptureTransferId!, amount, cancellationToken);
            }
            catch (PaymentGatewayException exception)
            {
                throw new PaymentConflictException(
                    $"Finix could not refund capture transfer {order.CaptureTransferId}: {exception.Message}.");
            }

            var refund = new Refund(order.Id,
                refundTransfer.Id,
                amount,
                refundTransfer.State ?? "PENDING",
                data.IdempotencyKey);
            await _refunds.AddAsync(refund, cancellationToken);
            order.AddRefund(amount);
            await _orders.UpdateAsync(order, cancellationToken);
            _logger.LogInformation("Order {OrderId} refunded {Amount} cents with Finix transfer {TransferId} (key {IdempotencyKey})",
                order.Id,
                amount,
                refundTransfer.Id,
                data.IdempotencyKey);
            return MapRefund(refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderDto>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orders.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var result = new List<OrderDto>();
        foreach (var order in orders.OrderByDescending(order => order.OrderDate))
        {
            result.Add(await MapOrderAsync(order, cancellationToken));
        }

        return result;
    }

    public async Task<SavedPaymentMethodDto> SavePaymentMethodAsync(SavePaymentMethodData data,
        CancellationToken cancellationToken = default)
    {
        ValidateCard(data.Card);
        var customer = await EnsureCustomerAsync(data.BuyerId, data.Card.Name, cancellationToken);
        var card = await _finix.CreateCardAsync(customer.FinixIdentityId, data.Card, cancellationToken);
        var saved = new SavedPaymentMethod(data.BuyerId,
            card.Id,
            customer.FinixIdentityId,
            card.Brand,
            card.Last4,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Name);
        await _cards.AddAsync(saved, cancellationToken);
        _logger.LogInformation("Saved payment method {PaymentMethodId} for {BuyerId} (Finix {PaymentInstrumentId})",
            saved.Id,
            data.BuyerId,
            card.Id);
        return MapSavedPaymentMethod(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _cards.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(MapSavedPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken = default)
    {
        var card = await _cards.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpecification(paymentMethodId, buyerId),
            cancellationToken);
        if (card is null)
        {
            throw new PaymentNotFoundException("Saved payment method not found.");
        }

        await _finix.DisableCardAsync(card.FinixPaymentInstrumentId, cancellationToken);
        await _cards.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Deleted payment method {PaymentMethodId} for {BuyerId}", paymentMethodId, buyerId);
    }

    public async Task<ReconciliationReport> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentValidationException("The 'to' date must be on or after the 'from' date.");
        }

        var transferListing = await _finix.ListAllTransfersAsync(_options.MerchantId, cancellationToken);
        var authorizationListing = await _finix.ListAllAuthorizationsAsync(_options.MerchantId, cancellationToken);

        var transfersInRange = transferListing.Transfers
            .Where(transfer => transfer.CreatedAt.HasValue
                && transfer.CreatedAt.Value >= from
                && transfer.CreatedAt.Value <= to
                && (string.IsNullOrWhiteSpace(transfer.Merchant)
                    || string.Equals(transfer.Merchant, _options.MerchantId, StringComparison.Ordinal)))
            .ToList();

        var authorizationsInRange = authorizationListing.Authorizations
            .Where(authorization => authorization.CreatedAt.HasValue
                && authorization.CreatedAt.Value >= from
                && authorization.CreatedAt.Value <= to
                && (string.IsNullOrWhiteSpace(authorization.Merchant)
                    || string.Equals(authorization.Merchant, _options.MerchantId, StringComparison.Ordinal)))
            .ToList();

        var orders = await _orders.ListAsync(new AllOrdersWithItemsSpecification(), cancellationToken);

        var refunds = await _refunds.ListAsync(cancellationToken);
        var refundsByTransfer = refunds.ToDictionary(refund => refund.TransferId, StringComparer.Ordinal);
        var ordersByCapture = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.CaptureTransferId))
            .GroupBy(order => order.CaptureTransferId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var ordersByAuthorization = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.AuthorizationId))
            .GroupBy(order => order.AuthorizationId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var transferIds = transfersInRange.Select(transfer => transfer.Id).ToHashSet(StringComparer.Ordinal);
        var authorizationIds = authorizationsInRange.Select(authorization => authorization.Id)
            .ToHashSet(StringComparer.Ordinal);

        var finixTransactions = new List<FinixTransactionDto>();
        foreach (var transfer in transfersInRange.OrderByDescending(transfer => transfer.CreatedAt))
        {
            int? matchedOrderId = null;
            string? matchedRefundTransferId = null;

            if (string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                && refundsByTransfer.TryGetValue(transfer.Id, out var refund))
            {
                matchedOrderId = refund.OrderId;
                matchedRefundTransferId = refund.TransferId;
            }
            else if (ordersByCapture.TryGetValue(transfer.Id, out var matchedOrder))
            {
                matchedOrderId = matchedOrder.Id;
            }

            finixTransactions.Add(new FinixTransactionDto(transfer.Id,
                transfer.Type,
                transfer.State,
                transfer.Amount,
                transfer.Fee,
                transfer.CreatedAt,
                transfer.ParentTransferId,
                matchedOrderId,
                matchedRefundTransferId,
                matchedOrderId.HasValue));
        }

        foreach (var authorization in authorizationsInRange.OrderByDescending(authorization => authorization.CreatedAt))
        {
            ordersByAuthorization.TryGetValue(authorization.Id, out var matchedOrder);
            finixTransactions.Add(new FinixTransactionDto(authorization.Id,
                "AUTHORIZATION",
                authorization.State,
                authorization.Amount,
                null,
                authorization.CreatedAt,
                authorization.TransferId,
                matchedOrder?.Id,
                null,
                matchedOrder is not null));
        }

        var matchedOrderIds = finixTransactions
            .Where(transaction => transaction.MatchedOrderId.HasValue)
            .Select(transaction => transaction.MatchedOrderId!.Value)
            .ToHashSet();

        var reconciliationOrders = orders
            .Where(order => (order.OrderDate >= from && order.OrderDate <= to)
                || matchedOrderIds.Contains(order.Id))
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new ReconciliationOrderDto(order.Id,
                order.Status,
                order.TotalCents(),
                order.Currency ?? _options.Currency,
                order.CaptureTransferId,
                order.OrderDate,
                (!string.IsNullOrWhiteSpace(order.CaptureTransferId)
                    && transferIds.Contains(order.CaptureTransferId!))
                || (!string.IsNullOrWhiteSpace(order.AuthorizationId)
                    && authorizationIds.Contains(order.AuthorizationId!))))
            .ToList();

        return new ReconciliationReport(from,
            to,
            _options.Currency,
            transferListing.PagesFetched + authorizationListing.PagesFetched,
            finixTransactions.Count,
            finixTransactions,
            reconciliationOrders,
            finixTransactions.Where(transaction => !transaction.Matched).ToList(),
            reconciliationOrders.Where(order => !order.Matched
                && order.OrderDate >= from
                && order.OrderDate <= to
                && (order.CaptureTransferId is not null
                    || order.Status is OrderStatuses.Fulfilled
                        or OrderStatuses.PartiallyRefunded
                        or OrderStatuses.Refunded)).ToList());
    }

    private async Task<Order> GetBuyerOrderAsync(int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException("Order not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orders.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null)
        {
            throw new PaymentNotFoundException("Order not found.");
        }

        return order;
    }

    private async Task<PaymentCustomer> EnsureCustomerAsync(string buyerId, string name, CancellationToken cancellationToken)
    {
        var existing = await _customers.FirstOrDefaultAsync(
            new PaymentCustomerByBuyerSpecification(buyerId),
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var gate = CustomerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _customers.FirstOrDefaultAsync(
                new PaymentCustomerByBuyerSpecification(buyerId),
                cancellationToken);
            if (existing is not null)
            {
                return existing;
            }

            var parts = (name ?? string.Empty).Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            var firstName = parts.Length > 0 ? parts[0] : "eShop";
            var lastName = parts.Length > 1 ? parts[1] : "Shopper";
            var email = buyerId.Contains('@') ? buyerId : $"{buyerId}@eshop.local";
            var identity = await _finix.CreateBuyerIdentityAsync(firstName, lastName, email, cancellationToken);
            var customer = new PaymentCustomer(buyerId, identity.Id);
            await _customers.AddAsync(customer, cancellationToken);
            _logger.LogInformation("Created Finix buyer identity {IdentityId} for {BuyerId}", identity.Id, buyerId);
            return customer;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FinixTransfer> WaitForCaptureSettlementAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) == false)
            {
                return transfer;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task TryVoidAsync(string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            await _finix.VoidAsync(authorizationId, cancellationToken);
        }
        catch (PaymentGatewayException exception)
        {
            _logger.LogWarning("Could not void authorization {AuthorizationId}: {Message}",
                authorizationId,
                exception.Message);
        }
    }

    private static string? DescribeUnusableAuthorization(FinixAuthorization authorization)
    {
        if (authorization.IsVoid == true)
        {
            return $"Authorization {authorization.Id} was voided and can no longer be captured. Cancel this order and ask the shopper to pay again.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"Authorization {authorization.Id} expired at {authorization.ExpiresAt.Value:O} and can no longer be captured. Cancel this order and ask the shopper to pay again.";
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            var detail = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                ? authorization.FailureCode
                : authorization.FailureMessage;
            return $"Authorization {authorization.Id} is in state {authorization.State ?? "UNKNOWN"}{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $": {detail}")} and can no longer be captured. Cancel this order and ask the shopper to pay again.";
        }

        return null;
    }

    private async Task<OrderDto> MapOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var refunds = await _refunds.ListAsync(new RefundsByOrderSpecification(order.Id), cancellationToken);
        var items = order.OrderItems
            .Select(item => new OrderLineDto(item.ItemOrdered.CatalogItemId,
                item.ItemOrdered.ProductName,
                item.UnitPrice,
                item.Units,
                item.UnitPrice * item.Units))
            .ToList();

        var payment = new PaymentStateDto(order.Status,
            order.Currency ?? _options.Currency,
            order.AuthorizedAmount,
            order.AuthorizationId,
            order.AuthorizationState,
            order.AuthorizationExpiresAt,
            order.CaptureTransferId,
            order.CaptureState,
            order.CapturedAmount,
            order.FinixFee,
            order.NetAmount,
            order.PaymentInstrumentId,
            order.SavedPaymentMethodId,
            order.RefundedAmount,
            order.RefundableAmount(),
            refunds.Select(MapRefund).ToList());

        return new OrderDto(order.Id,
            order.Status,
            order.Total(),
            order.TotalCents(),
            order.Currency ?? _options.Currency,
            order.OrderDate,
            new AddressData(order.ShipToAddress.Street,
                order.ShipToAddress.City,
                order.ShipToAddress.State,
                order.ShipToAddress.Country,
                order.ShipToAddress.ZipCode),
            items,
            payment);
    }

    private static RefundDto MapRefund(Refund refund) =>
        new(refund.Id,
            refund.TransferId,
            refund.Amount,
            refund.Amount / 100m,
            refund.State,
            refund.IdempotencyKey,
            refund.CreatedAt);

    private static SavedPaymentMethodDto MapSavedPaymentMethod(SavedPaymentMethod card) =>
        new(card.Id,
            card.Brand,
            card.Last4,
            card.ExpirationMonth,
            card.ExpirationYear,
            card.Name,
            card.CreatedAt);

    private static void ValidateCard(CardData card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        if (string.IsNullOrWhiteSpace(card.Number) || card.Number.Count(char.IsDigit) < 12)
        {
            throw new PaymentValidationException("A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentValidationException("The card expiration year must not be in the past.");
        }

        if (string.IsNullOrWhiteSpace(card.Cvc))
        {
            throw new PaymentValidationException("The card security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }
    }
}
