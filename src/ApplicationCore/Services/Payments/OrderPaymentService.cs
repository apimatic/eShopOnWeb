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
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services.Payments;

public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultPicture = "eCatalog-item-default.png";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _paymentMethodRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IFinixPaymentGateway _finix;
    private readonly IResourceLock _resourceLock;
    private readonly FinixOptions _finixOptions;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<SavedPaymentMethod> paymentMethodRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IFinixPaymentGateway finix,
        IResourceLock resourceLock,
        IOptions<FinixOptions> finixOptions)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _catalogItemRepository = catalogItemRepository;
        _finix = finix;
        _resourceLock = resourceLock;
        _finixOptions = finixOptions.Value;
    }

    private string Currency => _finixOptions.Currency;

    public async Task<OrderView> CreateOrderAsync(string buyerId,
        IReadOnlyCollection<NewOrderItem> items,
        string? street,
        string? city,
        string? state,
        string? country,
        string? zipCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A buyer identity is required to place an order.");
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("Finix:Currency is not configured.");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        if (items.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentValidationException("Each order item requires a positive catalog item id and quantity.");
        }

        var catalogIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);
        var catalogById = catalogItems.ToDictionary(i => i.Id);

        var missing = catalogIds.Where(id => !catalogById.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException($"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            var catalogItem = catalogById[requested.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri) ? DefaultPicture : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var address = BuildAddress(street, city, state, country, zipCode);
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var totalCents = ToCents(order.Total());
        if (totalCents <= 0)
        {
            throw new PaymentValidationException("The order total must be greater than zero.");
        }

        var payment = new OrderPayment(order.Id, totalCents, Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return BuildOrderView(order, payment);
    }

    public async Task<PayResult> PayAsync(PayOrderCommand command, CancellationToken cancellationToken)
    {
        await using var handle = await _resourceLock.AcquireAsync($"order:{command.OrderId}", cancellationToken);

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, command.BuyerId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException($"Order {command.OrderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id), cancellationToken);
        if (payment is null)
        {
            throw new PaymentConflictException($"Order {order.Id} does not have a payment record.");
        }

        if (payment.Status is OrderPaymentStatus.Authorized
            or OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return new PayResult(BuildOrderView(order, payment), true);
        }

        if (payment.Status == OrderPaymentStatus.Cancelled || order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {order.Id} has been cancelled and can no longer be paid.");
        }

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled)
        {
            throw new PaymentConflictException($"Order {order.Id} has already been fulfilled.");
        }

        if (command.Card is null && !command.PaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId.");
        }

        if (command.Card is not null && command.PaymentMethodId.HasValue)
        {
            throw new PaymentValidationException("Provide either card details or a saved paymentMethodId, not both.");
        }

        string paymentInstrumentId;
        string? brand;
        string? last4;
        int? savedPaymentMethodId = null;

        if (command.PaymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdForBuyerSpec(command.PaymentMethodId.Value, command.BuyerId),
                cancellationToken);

            if (saved is null)
            {
                throw new ResourceNotFoundException($"Saved payment method {command.PaymentMethodId.Value} was not found.");
            }

            var instrument = await _finix.GetPaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            if (instrument is null || !instrument.Enabled)
            {
                throw new PaymentConflictException(
                    $"Saved payment method {saved.Id} is no longer available at Finix. Remove it or pay with another card.");
            }

            paymentInstrumentId = saved.FinixPaymentInstrumentId;
            brand = instrument.Brand ?? saved.Brand;
            last4 = instrument.LastFour ?? saved.Last4;
            savedPaymentMethodId = saved.Id;
        }
        else
        {
            ValidateCard(command.Card!);
            var instrument = await _finix.CreatePaymentInstrumentAsync(command.Card!, cancellationToken);
            paymentInstrumentId = instrument.Id;
            brand = instrument.Brand;
            last4 = instrument.LastFour;
        }

        payment.RecordAuthorizationAttempt();
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        var idempotencyId = $"eshop-order-{order.Id}-{order.OrderDate.UtcTicks}-auth-{payment.AuthorizationAttempts}";
        var authorization = await _finix.CreateAuthorizationAsync(
            paymentInstrumentId,
            payment.AmountCents,
            payment.Currency,
            idempotencyId,
            order.Id.ToString(),
            BuildOrderKey(order),
            order.BuyerId,
            cancellationToken);

        if (string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            payment.RecordAuthorized(
                authorization.Id,
                authorization.State ?? "UNKNOWN",
                authorization.ExpiresAt,
                authorization.IsVoid,
                authorization.VoidState,
                paymentInstrumentId,
                savedPaymentMethodId,
                brand,
                last4);
            order.SetPaymentStatus(OrderPaymentStatus.Authorized);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return new PayResult(BuildOrderView(order, payment), false);
        }

        payment.RecordAuthorizationFailed(
            authorization.Id,
            authorization.State,
            authorization.FailureCode,
            authorization.FailureMessage,
            paymentInstrumentId,
            brand,
            last4);
        order.SetPaymentStatus(OrderPaymentStatus.Failed);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        var reason = authorization.FailureMessage ?? authorization.FailureCode ?? authorization.State ?? "unknown";
        throw new PaymentConflictException(
            $"Finix did not authorize order {order.Id}. Authorization {authorization.Id} is {authorization.State ?? "UNKNOWN"}: {reason}.");
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var handle = await _resourceLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentConflictException($"Order {orderId} does not have a payment record.");
        }

        if (order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled || payment.Status == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} has been cancelled and cannot be fulfilled.");
        }

        if (payment.Status is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded)
        {
            return BuildOrderView(order, payment);
        }

        if (payment.Status != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentConflictException(
                $"Order {orderId} cannot be fulfilled because its payment is {payment.Status}. " +
                "The shopper must authorize payment before the order can be fulfilled.");
        }

        var authorizationId = payment.FinixAuthorizationId!;
        var authorization = await _finix.GetAuthorizationAsync(authorizationId, cancellationToken);
        if (authorization is null)
        {
            throw new PaymentConflictException(
                $"Authorization {authorizationId} for order {orderId} was not found at Finix and can no longer be captured. " +
                "The shopper must authorize payment again.");
        }

        payment.RecordAuthorizationSnapshot(
            authorization.State ?? "UNKNOWN",
            authorization.ExpiresAt,
            authorization.IsVoid,
            authorization.VoidState);

        if (authorization.IsVoid
            || string.Equals(authorization.VoidState, "PENDING", StringComparison.OrdinalIgnoreCase)
            || string.Equals(authorization.VoidState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"Authorization {authorizationId} for order {orderId} has been voided " +
                $"(void_state={authorization.VoidState ?? "UNKNOWN"}) and can no longer be captured. " +
                "The shopper must authorize payment again.");
        }

        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"Authorization {authorizationId} for order {orderId} is in state {authorization.State ?? "UNKNOWN"} " +
                "and cannot be captured. The shopper must authorize payment again.");
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"Authorization {authorizationId} for order {orderId} expired at " +
                $"{authorization.ExpiresAt.Value:O} and can no longer be captured. " +
                "The shopper must authorize payment again.");
        }

        if (!string.IsNullOrWhiteSpace(authorization.TransferId))
        {
            var existingTransfer = await GetTransferWhenSettledAsync(authorization.TransferId!, cancellationToken);
            if (existingTransfer is not null)
            {
                var fee = existingTransfer.FeeCents;
                var captured = existingTransfer.AmountCents > 0 ? existingTransfer.AmountCents : payment.AmountCents;
                payment.RecordCaptured(existingTransfer.Id, existingTransfer.State, captured, fee, Math.Max(0, captured - fee));
                order.SetPaymentStatus(payment.Status);
                order.SetFulfillmentStatus(OrderFulfillmentStatus.Fulfilled);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return BuildOrderView(order, payment);
            }
        }

        var capture = await _finix.CaptureAuthorizationAsync(authorizationId, payment.AmountCents, cancellationToken);
        if (!string.Equals(capture.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(capture.TransferId))
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            var reason = capture.FailureMessage ?? capture.FailureCode ?? capture.State ?? "unknown";
            throw new PaymentConflictException(
                $"Finix could not capture authorization {authorizationId} for order {orderId}: {reason}. " +
                "The shopper may need to authorize payment again.");
        }

        var transfer = await GetTransferWhenSettledAsync(capture.TransferId!, cancellationToken);
        var capturedAmount = transfer?.AmountCents > 0 ? transfer!.AmountCents : capture.AmountCents;
        var transferFee = transfer?.FeeCents ?? 0;
        var net = Math.Max(0, capturedAmount - transferFee);

        payment.RecordCaptured(capture.TransferId!, transfer?.State ?? capture.State, capturedAmount, transferFee, net);
        order.SetPaymentStatus(payment.Status);
        order.SetFulfillmentStatus(OrderFulfillmentStatus.Fulfilled);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return BuildOrderView(order, payment);
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var handle = await _resourceLock.AcquireAsync($"order:{orderId}", cancellationToken);

        var order = await _orderRepository.GetByIdAsync(orderId, cancellationToken);
        if (order is null)
        {
            throw new ResourceNotFoundException($"Order {orderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment is null)
        {
            throw new PaymentConflictException($"Order {orderId} does not have a payment record.");
        }

        if (payment.Status is OrderPaymentStatus.Captured
            or OrderPaymentStatus.PartiallyRefunded
            or OrderPaymentStatus.Refunded
            || order.FulfillmentStatus == OrderFulfillmentStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} has already been fulfilled. Use the refunds endpoint to return captured funds.");
        }

        if (payment.Status == OrderPaymentStatus.Cancelled || order.FulfillmentStatus == OrderFulfillmentStatus.Cancelled)
        {
            return BuildOrderView(order, payment);
        }

        if (payment.Status == OrderPaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            var authorizationId = payment.FinixAuthorizationId!;
            if (payment.AuthorizationIsVoid)
            {
                payment.RecordCancelled(payment.AuthorizationVoidState);
            }
            else
            {
                var voided = await _finix.VoidAuthorizationAsync(authorizationId, cancellationToken);
                payment.RecordCancelled(voided.VoidState ?? voided.State);
            }
        }
        else
        {
            payment.RecordCancelledWithoutAuthorization();
        }

        order.SetPaymentStatus(OrderPaymentStatus.Cancelled);
        order.SetFulfillmentStatus(OrderFulfillmentStatus.Cancelled);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        await _orderRepository.UpdateAsync(order, cancellationToken);

        return BuildOrderView(order, payment);
    }

    public async Task<RefundResult> RefundAsync(RefundOrderCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotencyKey is required for refunds.");
        }

        await using var handle = await _resourceLock.AcquireAsync($"order:{command.OrderId}", cancellationToken);

        var order = await _orderRepository.GetByIdAsync(command.OrderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, command.BuyerId, StringComparison.Ordinal))
        {
            throw new ResourceNotFoundException($"Order {command.OrderId} was not found.");
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(order.Id), cancellationToken);
        if (payment is null)
        {
            throw new PaymentConflictException($"Order {order.Id} does not have a payment record.");
        }

        var existingRefund = payment.FindRefundByIdempotencyKey(command.IdempotencyKey);
        if (existingRefund is not null)
        {
            return BuildRefundResult(order, payment, existingRefund, true);
        }

        if (order.FulfillmentStatus != OrderFulfillmentStatus.Fulfilled
            || payment.Status is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded))
        {
            throw new PaymentConflictException(
                $"Order {order.Id} can only be refunded after it has been fulfilled and captured.");
        }

        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            throw new PaymentConflictException(
                $"Order {order.Id} does not have a Finix capture transfer id, so it cannot be refunded.");
        }

        var captured = payment.CapturedAmountCents ?? 0;
        if (captured <= 0)
        {
            throw new PaymentConflictException($"Order {order.Id} has no captured amount to refund.");
        }

        var remaining = payment.RemainingRefundableCents;
        long amountCents;
        if (command.Amount.HasValue)
        {
            if (command.Amount.Value <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }
            amountCents = ToCents(command.Amount.Value);
        }
        else
        {
            amountCents = remaining;
        }

        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The order has no remaining refundable amount.");
        }

        if (amountCents > remaining)
        {
            throw new PaymentConflictException(
                $"Refund of {FormatAmount(amountCents)} {payment.Currency} exceeds the remaining refundable amount " +
                $"of {FormatAmount(remaining)} {payment.Currency} for order {order.Id}.");
        }

        var settledCapture = await GetTransferWhenSettledAsync(payment.FinixCaptureTransferId!, cancellationToken);
        if (settledCapture is not null
            && string.Equals(settledCapture.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && amountCents < captured)
        {
            throw new PaymentConflictException(
                $"Capture transfer {payment.FinixCaptureTransferId} for order {order.Id} is still PENDING at Finix. " +
                "Retry the partial refund after the capture settles, or refund the full captured amount now.");
        }

        var refundTransfer = await _finix.CreateRefundAsync(
            payment.FinixCaptureTransferId!,
            amountCents,
            cancellationToken);

        var refund = payment.AddRefund(
            amountCents,
            command.IdempotencyKey,
            refundTransfer.Id,
            refundTransfer.State ?? "UNKNOWN",
            refundTransfer.FailureCode,
            refundTransfer.FailureMessage);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        var result = BuildRefundResult(order, payment, refund, false);
        if (!refund.CountsTowardsRefundedTotal)
        {
            throw new PaymentConflictException(
                $"Finix could not refund order {order.Id}: " +
                $"{refundTransfer.FailureMessage ?? refundTransfer.FailureCode ?? refundTransfer.State ?? "unknown"}.");
        }

        return result;
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A buyer identity is required.");
        }

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderView>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new OrderPaymentsByOrderIdsSpec(orderIds), cancellationToken);
        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .Select(order => BuildOrderView(order, paymentsByOrderId.TryGetValue(order.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            throw new PaymentValidationException("The 'from' timestamp must be before or equal to the 'to' timestamp.");
        }

        var finixTransactions = await _finix.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await _orderRepository.ListAsync(new OrdersInDateRangeSpec(from, to), cancellationToken);
        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = orderIds.Length == 0
            ? new List<OrderPayment>()
            : await _paymentRepository.ListAsync(new OrderPaymentsByOrderIdsSpec(orderIds), cancellationToken);

        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);
        var orderIdByKey = orders.ToDictionary(o => BuildOrderKey(o), o => o.Id, StringComparer.Ordinal);
        var paymentByAuthorizationId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .ToDictionary(p => p.FinixAuthorizationId!, p => p);
        var paymentByCaptureTransferId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixCaptureTransferId))
            .ToDictionary(p => p.FinixCaptureTransferId!, p => p);
        var refundsByTransferId = payments
            .SelectMany(p => p.Refunds
                .Where(r => !string.IsNullOrWhiteSpace(r.FinixTransferId))
                .Select(r => new { Payment = p, Refund = r }))
            .GroupBy(x => x.Refund.FinixTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matchedOrderIds = new HashSet<int>();
        var records = new List<ReconciliationRecord>(finixTransactions.Count);

        foreach (var transaction in finixTransactions)
        {
            int? matchedOrderId = null;

            if (string.Equals(transaction.Kind, "authorization", StringComparison.OrdinalIgnoreCase)
                && paymentByAuthorizationId.TryGetValue(transaction.Id, out var authorizationPayment))
            {
                matchedOrderId = authorizationPayment.OrderId;
            }
            else if (string.Equals(transaction.Kind, "transfer", StringComparison.OrdinalIgnoreCase))
            {
                if (paymentByCaptureTransferId.TryGetValue(transaction.Id, out var capturePayment))
                {
                    matchedOrderId = capturePayment.OrderId;
                }
                else if (refundsByTransferId.TryGetValue(transaction.Id, out var refundMatch))
                {
                    matchedOrderId = refundMatch.Payment.OrderId;
                }
            }

            if (!matchedOrderId.HasValue
                && transaction.Tags.TryGetValue("eshop_order_key", out var taggedOrderKey)
                && orderIdByKey.TryGetValue(taggedOrderKey, out var keyedOrderId))
            {
                matchedOrderId = keyedOrderId;
            }

            if (matchedOrderId.HasValue)
            {
                matchedOrderIds.Add(matchedOrderId.Value);
            }

            records.Add(new ReconciliationRecord(
                transaction.Kind,
                transaction.Id,
                transaction.Type,
                transaction.State,
                transaction.AmountCents,
                FromCents(transaction.AmountCents),
                transaction.Currency,
                transaction.CreatedAt,
                transaction.ParentTransferId,
                transaction.Source,
                transaction.FailureCode,
                transaction.FailureMessage,
                matchedOrderId,
                matchedOrderId.HasValue ? "Matched" : "FinixOnly"));
        }

        var orderViews = new List<ReconciliationOrder>(orders.Count);
        foreach (var order in orders)
        {
            paymentsByOrderId.TryGetValue(order.Id, out var payment);
            var refundTransferIds = payment?.Refunds
                .Where(r => !string.IsNullOrWhiteSpace(r.FinixTransferId))
                .Select(r => r.FinixTransferId!)
                .ToArray() ?? Array.Empty<string>();

            orderViews.Add(new ReconciliationOrder(
                order.Id,
                order.BuyerId,
                order.OrderDate,
                order.Total(),
                ToCents(order.Total()),
                payment?.Currency ?? Currency,
                (payment?.Status ?? order.PaymentStatus).ToString(),
                order.FulfillmentStatus.ToString(),
                payment?.FinixAuthorizationId,
                payment?.FinixCaptureTransferId,
                refundTransferIds,
                matchedOrderIds.Contains(order.Id) ? "Matched" : "EshopOnly"));
        }

        var matched = orderViews.Count(o => o.MatchStatus == "Matched");
        var finixOnly = records.Count(r => r.MatchStatus == "FinixOnly");
        var eshopOnly = orderViews.Count(o => o.MatchStatus == "EshopOnly");

        return new ReconciliationReport(
            from,
            to,
            Currency,
            records,
            orderViews,
            matched,
            finixOnly,
            eshopOnly);
    }

    public async Task<SavedCardView> SavePaymentMethodAsync(string buyerId, FinixCardDetails card, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A buyer identity is required.");
        }

        ValidateCard(card);
        var instrument = await _finix.CreatePaymentInstrumentAsync(card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            instrument.Id,
            instrument.Brand,
            instrument.LastFour ?? LastFourFromNumber(card.Number),
            instrument.ExpirationMonth ?? card.ExpirationMonth,
            instrument.ExpirationYear ?? card.ExpirationYear,
            instrument.CardType,
            instrument.Name ?? card.Name,
            instrument.IdentityId);

        saved = await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        return BuildSavedCardView(saved);
    }

    public async Task<IReadOnlyList<SavedCardView>> GetPaymentMethodsAsync(string buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("A buyer identity is required.");
        }

        var methods = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods.Select(BuildSavedCardView).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var saved = await _paymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdForBuyerSpec(paymentMethodId, buyerId),
            cancellationToken);

        if (saved is null)
        {
            throw new ResourceNotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        await _finix.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        await _paymentMethodRepository.DeleteAsync(saved, cancellationToken);
    }

    private static string BuildOrderKey(Order order) => $"{order.Id}-{order.OrderDate.UtcTicks}";

    private async Task<FinixTransfer?> GetTransferWhenSettledAsync(string transferId, CancellationToken cancellationToken)
    {
        FinixTransfer? transfer = null;
        for (var attempt = 0; attempt < 60; attempt++)
        {
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
            if (transfer is null ||
                !string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                return transfer;
            }

            await Task.Delay(500, cancellationToken);
        }

        return transfer;
    }

    private static Address BuildAddress(string? street, string? city, string? state, string? country, string? zipCode)
    {
        return new Address(
            string.IsNullOrWhiteSpace(street) ? "Not provided" : street!,
            string.IsNullOrWhiteSpace(city) ? "Not provided" : city!,
            string.IsNullOrWhiteSpace(state) ? "N/A" : state!,
            string.IsNullOrWhiteSpace(country) ? "USA" : country!,
            string.IsNullOrWhiteSpace(zipCode) ? "00000" : zipCode!);
    }

    private static void ValidateCard(FinixCardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentValidationException("A card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < 2000)
        {
            throw new PaymentValidationException("The card expiration year must be a four-digit year.");
        }
    }

    private OrderView BuildOrderView(Order order, OrderPayment? payment)
    {
        var items = order.OrderItems
            .Select(i => new OrderItemView(
                i.ItemOrdered.CatalogItemId,
                i.ItemOrdered.ProductName,
                i.UnitPrice,
                i.Units,
                i.UnitPrice * i.Units))
            .ToList();

        return new OrderView(
            order.Id,
            order.OrderDate,
            order.BuyerId,
            order.Total(),
            ToCents(order.Total()),
            payment?.Currency ?? Currency,
            (payment?.Status ?? order.PaymentStatus).ToString(),
            order.FulfillmentStatus.ToString(),
            items,
            payment is null ? null : BuildPaymentView(payment));
    }

    private static PaymentView BuildPaymentView(OrderPayment payment)
    {
        var refunds = payment.Refunds
            .OrderBy(r => r.CreatedAt)
            .Select(r => new RefundView(
                r.Id,
                r.AmountCents,
                FromCents(r.AmountCents),
                r.Status,
                r.IdempotencyKey,
                r.FinixTransferId,
                r.FailureCode,
                r.FailureMessage,
                r.CreatedAt))
            .ToList();

        return new PaymentView(
            payment.Status.ToString(),
            payment.Currency,
            payment.AmountCents,
            FromCents(payment.AmountCents),
            payment.FinixAuthorizationId,
            payment.AuthorizationState,
            payment.AuthorizationExpiresAt,
            payment.AuthorizationIsVoid,
            payment.AuthorizationVoidState,
            payment.FinixCaptureTransferId,
            payment.CaptureState,
            payment.CapturedAmountCents,
            payment.CapturedAmountCents.HasValue ? FromCents(payment.CapturedAmountCents.Value) : null,
            payment.FeeCents,
            payment.FeeCents.HasValue ? FromCents(payment.FeeCents.Value) : null,
            payment.NetCents,
            payment.NetCents.HasValue ? FromCents(payment.NetCents.Value) : null,
            payment.RefundedAmountCents,
            FromCents(payment.RefundedAmountCents),
            payment.RemainingRefundableCents,
            FromCents(payment.RemainingRefundableCents),
            payment.FinixPaymentInstrumentId,
            payment.PaymentMethodId,
            payment.CardBrand,
            payment.CardLast4,
            payment.FailureCode,
            payment.FailureMessage,
            refunds);
    }

    private static RefundResult BuildRefundResult(Order order, OrderPayment payment, OrderRefund refund, bool idempotentReplay)
    {
        return new RefundResult(
            refund.Id,
            order.Id,
            refund.AmountCents,
            FromCents(refund.AmountCents),
            payment.Currency,
            refund.Status,
            refund.IdempotencyKey,
            refund.FinixTransferId,
            refund.FailureCode,
            refund.FailureMessage,
            idempotentReplay);
    }

    private static SavedCardView BuildSavedCardView(SavedPaymentMethod saved)
    {
        return new SavedCardView(
            saved.Id,
            saved.Brand ?? "UNKNOWN",
            saved.Last4,
            saved.ExpirationMonth,
            saved.ExpirationYear,
            saved.CardType,
            saved.Name,
            saved.CreatedAt);
    }

    private static string LastFourFromNumber(string number)
    {
        var digits = new string(number.Where(char.IsDigit).ToArray());
        return digits.Length <= 4 ? digits : digits[^4..];
    }

    public static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    public static decimal FromCents(long cents) => cents / 100m;

    private static string FormatAmount(long cents) => FromCents(cents).ToString("0.00");
}
