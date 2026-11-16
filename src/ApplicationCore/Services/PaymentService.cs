using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<Refund> _refundRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IRepository<FinixCustomer> _finixCustomerRepository;
    private readonly IFinixPaymentGateway _gateway;
    private readonly IUriComposer _uriComposer;
    private readonly FinixOptions _options;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<Refund> refundRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IRepository<FinixCustomer> finixCustomerRepository,
        IFinixPaymentGateway gateway,
        IUriComposer uriComposer,
        FinixOptions options)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _refundRepository = refundRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _finixCustomerRepository = finixCustomerRepository;
        _gateway = gateway;
        _uriComposer = uriComposer;
        _options = options;
    }

    private string ConfiguredCurrency =>
        string.IsNullOrWhiteSpace(_options.Currency) ? string.Empty : _options.Currency.Trim().ToUpperInvariant();

    public async Task<OrderSummary> CreateOrderAsync(
        string buyerId,
        CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "A signed-in shopper is required.");
        }

        if (command?.Items == null || command.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "At least one catalog item is required.");
        }

        foreach (var item in command.Items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0)
            {
                throw new PaymentOperationException(400, "Each order item needs a positive catalog item id and quantity.");
            }
        }

        var catalogItemIds = command.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemIds), cancellationToken);
        var catalogItemsById = catalogItems.ToDictionary(i => i.Id);

        var orderItems = new List<OrderItem>();
        foreach (var requested in command.Items)
        {
            if (!catalogItemsById.TryGetValue(requested.CatalogItemId, out var catalogItem))
            {
                throw new PaymentOperationException(400, $"Catalog item {requested.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                _uriComposer.ComposePicUri(catalogItem.PictureUri));
            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var order = new Order(buyerId, ToAddress(command.ShippingAddress), orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        return ToOrderSummary(order, null, Array.Empty<Refund>());
    }

    public async Task<OrderSummary> AuthorizeOrderAsync(
        string buyerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
            if (order == null || !IsOwner(order.BuyerId, buyerId))
            {
                throw new PaymentOperationException(404, "Order not found.");
            }

            if (order.Status == OrderStatus.Fulfilled ||
                order.Status == OrderStatus.Cancelled ||
                order.Status == OrderStatus.PartiallyRefunded ||
                order.Status == OrderStatus.Refunded)
            {
                throw new PaymentOperationException(409, $"Order {orderId} is {order.Status} and can no longer be paid.");
            }

            var existing = await GetPaymentForOrderAsync(orderId, cancellationToken);
            if (existing != null)
            {
                if (existing.CaptureTransferId != null)
                {
                    throw new PaymentOperationException(409, "The order has already been captured.");
                }

                if (existing.IsVoided)
                {
                    throw new PaymentOperationException(409, "The order's authorization was voided and cannot be captured.");
                }

                if (!string.IsNullOrWhiteSpace(existing.AuthorizationId))
                {
                    if (string.Equals(existing.AuthorizationState, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new PaymentOperationException(402,
                            "The previous authorization failed. Cancel this order and create a new order to try again.",
                            existing.FailureCode);
                    }

                    return ToOrderSummary(order, existing, await GetRefundsAsync(existing.Id, cancellationToken));
                }
            }

            if (command == null || (command.PaymentMethodId.HasValue && command.Card != null))
            {
                throw new PaymentOperationException(400, "Provide either a saved paymentMethodId or one-off card details, not both.");
            }

            string paymentInstrumentId;
            int? savedPaymentMethodId = null;

            if (command.PaymentMethodId.HasValue)
            {
                var saved = await _paymentMethodRepository.GetByIdAsync(command.PaymentMethodId.Value, cancellationToken);
                if (saved == null || saved.IsDeleted || !IsOwner(saved.BuyerId, buyerId))
                {
                    throw new PaymentOperationException(404, "Saved payment method not found.");
                }

                if (string.IsNullOrWhiteSpace(saved.FinixPaymentInstrumentId))
                {
                    throw new PaymentOperationException(409, "The saved payment method cannot be used because it has no Finix instrument.");
                }

                paymentInstrumentId = saved.FinixPaymentInstrumentId;
                savedPaymentMethodId = saved.Id;
            }
            else if (command.Card != null)
            {
                var identityId = await GetOrCreateFinixIdentityAsync(buyerId, cancellationToken);
                var card = ToFinixCard(command.Card);
                var instrument = await _gateway.CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
                paymentInstrumentId = instrument.Id;
            }
            else
            {
                throw new PaymentOperationException(400, "Provide either a saved paymentMethodId or one-off card details.");
            }

            var amountCents = ToCents(order.Total());
            if (amountCents <= 0)
            {
                throw new PaymentOperationException(400, "The order total must be greater than zero.");
            }

            var currency = _options.ResolveCurrency();
            var keyWasMissing = string.IsNullOrWhiteSpace(order.PaymentIdempotencyKey);
            var idempotencyId = order.EnsurePaymentIdempotencyKey();
            if (keyWasMissing)
            {
                await _orderRepository.UpdateAsync(order, cancellationToken);
            }

            FinixAuthorizationResult authorization;
            try
            {
                authorization = await _gateway.CreateAuthorizationAsync(
                    paymentInstrumentId,
                    amountCents,
                    currency,
                    idempotencyId,
                    new Dictionary<string, string> { ["orderId"] = orderId.ToString() },
                    cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentOperationException(
                    ex.StatusCode is >= 400 and < 500 ? 402 : 502,
                    $"Finix could not authorize the payment: {ex.Message}",
                    ex.Code);
            }

            var payment = existing ?? new OrderPayment(
                orderId,
                buyerId,
                currency,
                amountCents,
                paymentInstrumentId,
                savedPaymentMethodId);
            payment.RecordAuthorization(authorization.Id, idempotencyId, authorization.State, authorization.ExpiresAt);

            if (string.Equals(authorization.State, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                payment.RecordFailure(authorization.FailureCode, authorization.FailureMessage);
                await AddOrUpdatePaymentAsync(payment, existing == null, cancellationToken);
                throw new PaymentOperationException(
                    402,
                    authorization.FailureMessage ?? "Finix declined the authorization.",
                    authorization.FailureCode);
            }

            await AddOrUpdatePaymentAsync(payment, existing == null, cancellationToken);
            order.MarkAuthorized();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return ToOrderSummary(order, payment, await GetRefundsAsync(payment.Id, cancellationToken));
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderSummary> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
            if (order == null)
            {
                throw new PaymentOperationException(404, "Order not found.");
            }

            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);
            if (payment == null || string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                throw new PaymentOperationException(409,
                    "Order has no authorized payment. Call POST /api/orders/{orderId}/pay before fulfilling.");
            }

            if (payment.CaptureTransferId != null)
            {
                order.MarkFulfilled();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await RefreshCaptureAsync(payment, waitForSettlement: true, cancellationToken);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return ToOrderSummary(order, payment, await GetRefundsAsync(payment.Id, cancellationToken));
            }

            if (payment.IsVoided || string.Equals(payment.AuthorizationState, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"The authorization {payment.AuthorizationId} was voided, so the held funds were released and can no longer be captured. " +
                    "Cancel the order or create a new order and pay again.");
            }

            if (payment.AuthorizationExpiresAt.HasValue && payment.AuthorizationExpiresAt.Value <= DateTimeOffset.UtcNow)
            {
                throw new PaymentOperationException(409,
                    $"The authorization {payment.AuthorizationId} expired at {payment.AuthorizationExpiresAt.Value:O}. " +
                    "The hold can no longer be captured; cancel the order and create a new order to take payment again.");
            }

            if (string.Equals(payment.AuthorizationState, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"The authorization {payment.AuthorizationId} failed ({payment.FailureMessage ?? "no failure detail"}). " +
                    "Cancel the order and create a new order to take payment again.");
            }

            try
            {
                var capture = await _gateway.CaptureAuthorizationAsync(
                    payment.AuthorizationId, payment.AmountCents, cancellationToken);

                if (string.IsNullOrWhiteSpace(capture.TransferId))
                {
                    var fresh = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    capture = fresh ?? capture;
                }

                if (string.IsNullOrWhiteSpace(capture.TransferId))
                {
                    throw new PaymentOperationException(502,
                        "Finix accepted the capture request but did not return a capture transfer id.");
                }

                payment.RecordCapture(capture.TransferId, payment.AmountCents, capture.State);
                order.MarkFulfilled();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                await RefreshCaptureAsync(payment, waitForSettlement: true, cancellationToken);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                return ToOrderSummary(order, payment, await GetRefundsAsync(payment.Id, cancellationToken));
            }
            catch (PaymentGatewayException ex)
            {
                var fresh = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                if (!string.IsNullOrWhiteSpace(fresh?.TransferId))
                {
                    payment.RecordCapture(fresh!.TransferId!, payment.AmountCents, fresh.State);
                    order.MarkFulfilled();
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    await RefreshCaptureAsync(payment, waitForSettlement: true, cancellationToken);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    return ToOrderSummary(order, payment, await GetRefundsAsync(payment.Id, cancellationToken));
                }

                throw new PaymentOperationException(
                    409,
                    BuildActionableCaptureMessage(payment, fresh, ex),
                    ex.Code);
            }
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<OrderSummary> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
            if (order == null)
            {
                throw new PaymentOperationException(404, "Order not found.");
            }

            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled)
            {
                return ToOrderSummary(order, payment, payment == null
                    ? Array.Empty<Refund>()
                    : await GetRefundsAsync(payment.Id, cancellationToken));
            }

            if (order.Status == OrderStatus.Fulfilled ||
                order.Status == OrderStatus.PartiallyRefunded ||
                order.Status == OrderStatus.Refunded ||
                payment?.CaptureTransferId != null)
            {
                throw new PaymentOperationException(409,
                    "The payment has already been captured. Use POST /api/orders/{orderId}/refunds to return funds.");
            }

            if (payment != null && !string.IsNullOrWhiteSpace(payment.AuthorizationId) && !payment.IsVoided)
            {
                try
                {
                    var voided = await _gateway.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    payment.RecordVoid(voided.State);
                }
                catch (PaymentGatewayException ex)
                {
                    var fresh = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    if (fresh?.IsVoid == true)
                    {
                        payment.RecordVoid(fresh.State);
                    }
                    else if (!string.IsNullOrWhiteSpace(fresh?.TransferId))
                    {
                        throw new PaymentOperationException(409,
                            "The payment was captured before it could be cancelled. Use POST /api/orders/{orderId}/refunds to return funds.");
                    }
                    else
                    {
                        throw new PaymentOperationException(409,
                            $"Finix could not release the authorization hold: {ex.Message}. " +
                            "Check the authorization in the Finix dashboard before trying again.",
                            ex.Code);
                    }
                }

                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return ToOrderSummary(order, payment, payment == null
                ? Array.Empty<Refund>()
                : await GetRefundsAsync(payment.Id, cancellationToken));
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<RefundSummary> RefundOrderAsync(
        string buyerId,
        int orderId,
        RefundCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command == null || string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "A non-empty idempotencyKey is required for refunds.");
        }

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderWithItemsAsync(orderId, cancellationToken);
            if (order == null || !IsOwner(order.BuyerId, buyerId))
            {
                throw new PaymentOperationException(404, "Order not found.");
            }

            var payment = await GetPaymentForOrderAsync(orderId, cancellationToken);
            if (payment == null || string.IsNullOrWhiteSpace(payment.CaptureTransferId) || payment.CapturedAmountCents == null)
            {
                throw new PaymentOperationException(409,
                    "The order has no captured payment to refund. Fulfil the order before requesting a refund.");
            }

            var refunds = await GetRefundsAsync(payment.Id, cancellationToken);
            var existing = refunds.FirstOrDefault(r =>
                string.Equals(r.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal));
            if (existing != null)
            {
                return ToRefundSummary(existing);
            }

            var captured = payment.CapturedAmountCents.Value;
            var alreadyRefunded = refunds.Where(r => !IsFailedRefund(r)).Sum(r => r.AmountCents);
            var remaining = captured - alreadyRefunded;
            if (remaining <= 0)
            {
                throw new PaymentOperationException(409, "The captured payment has already been fully refunded.");
            }

            long amountCents = command.Amount.HasValue ? ToCents(command.Amount.Value) : remaining;
            if (amountCents <= 0)
            {
                throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
            }

            if (amountCents > remaining)
            {
                throw new PaymentOperationException(409,
                    $"Refund amount {amountCents} cents exceeds the remaining refundable amount of {remaining} cents.");
            }

            if (amountCents < captured)
            {
                await EnsureTransferSettledAsync(payment, cancellationToken);
            }

            FinixTransferResult refundTransfer;
            try
            {
                refundTransfer = await _gateway.RefundTransferAsync(
                    payment.CaptureTransferId!,
                    amountCents,
                    BuildRefundIdempotencyId(payment.Id, command.IdempotencyKey),
                    cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentOperationException(409,
                    $"Finix could not process the refund: {ex.Message}",
                    ex.Code);
            }

            var refund = new Refund(order.Id, payment.Id, command.IdempotencyKey, amountCents);
            refund.RecordGatewayResult(refundTransfer.Id, refundTransfer.State);
            await _refundRepository.AddAsync(refund, cancellationToken);

            var totalRefunded = alreadyRefunded + amountCents;
            order.MarkRefunded(totalRefunded >= captured);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return ToRefundSummary(refund);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<IReadOnlyList<OrderSummary>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderSummary>();
        }

        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);
        var paymentIds = payments.Select(p => p.Id).ToArray();
        var refunds = paymentIds.Length == 0
            ? Array.Empty<Refund>()
            : (await _refundRepository.ListAsync(new RefundsByPaymentIdsSpec(paymentIds), cancellationToken)).ToArray();
        var refundsByPayment = refunds.GroupBy(r => r.OrderPaymentId).ToDictionary(g => g.Key, g => (IReadOnlyList<Refund>)g.ToList());

        var results = new List<OrderSummary>(orders.Count);
        foreach (var order in orders)
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            if (payment != null)
            {
                await RefreshPaymentBestEffortAsync(payment, cancellationToken);
            }

            var orderRefunds = payment != null && refundsByPayment.TryGetValue(payment.Id, out var list)
                ? list
                : Array.Empty<Refund>();
            results.Add(ToOrderSummary(order, payment, orderRefunds));
        }

        return results;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new PaymentOperationException(400, "'to' must be greater than or equal to 'from'.");
        }

        var finixAuthorizations = await _gateway.ListAuthorizationsAsync(from, to, cancellationToken);
        var finixTransfers = await _gateway.ListTransfersAsync(from, to, cancellationToken);

        var localPayments = await _paymentRepository.ListAsync(
            new OrderPaymentsCreatedBetweenSpec(from, to), cancellationToken);
        var localRefunds = await _refundRepository.ListAsync(
            new RefundsCreatedBetweenSpec(from, to), cancellationToken);

        var finixAuthorizationIds = finixAuthorizations.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var finixTransferIds = finixTransfers.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

        var report = new ReconciliationReport { From = from, To = to };

        foreach (var authorization in finixAuthorizations)
        {
            var payment = localPayments.FirstOrDefault(p =>
                string.Equals(p.AuthorizationId, authorization.Id, StringComparison.Ordinal));
            payment ??= await _paymentRepository.FirstOrDefaultAsync(
                new OrderPaymentByAuthorizationIdSpec(authorization.Id), cancellationToken);

            var orderId = payment?.OrderId ?? TryReadOrderIdTag(authorization.Tags);
            var matched = payment != null;
            report.FinixTransactions.Add(new ReconciliationEntry
            {
                Source = "finix",
                TransactionType = "authorization",
                FinixId = authorization.Id,
                OrderId = orderId,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency,
                State = authorization.State,
                CreatedAt = authorization.CreatedAt,
                Matched = matched,
                MatchStatus = matched ? "matched" : "finix-only"
            });
        }

        foreach (var transfer in finixTransfers)
        {
            var isRefund = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase);
            int? orderId = null;
            var matched = false;

            if (isRefund)
            {
                var refund = localRefunds.FirstOrDefault(r =>
                    string.Equals(r.FinixRefundTransferId, transfer.Id, StringComparison.Ordinal));
                refund ??= await _refundRepository.FirstOrDefaultAsync(
                    new RefundByFinixTransferIdSpec(transfer.Id), cancellationToken);
                if (refund != null)
                {
                    orderId = refund.OrderId;
                    matched = true;
                }
            }
            else
            {
                var payment = localPayments.FirstOrDefault(p =>
                    string.Equals(p.CaptureTransferId, transfer.Id, StringComparison.Ordinal));
                payment ??= await _paymentRepository.FirstOrDefaultAsync(
                    new OrderPaymentByCaptureTransferIdSpec(transfer.Id), cancellationToken);
                if (payment != null)
                {
                    orderId = payment.OrderId;
                    matched = true;
                }
            }

            orderId ??= TryReadOrderIdTag(transfer.Tags);
            report.FinixTransactions.Add(new ReconciliationEntry
            {
                Source = "finix",
                TransactionType = isRefund ? "refund" : "capture",
                FinixId = transfer.Id,
                OrderId = orderId,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency,
                State = transfer.State,
                CreatedAt = transfer.CreatedAt,
                Matched = matched,
                MatchStatus = matched ? "matched" : "finix-only"
            });
        }

        foreach (var payment in localPayments)
        {
            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
                !finixAuthorizationIds.Contains(payment.AuthorizationId))
            {
                report.EshopOnlyTransactions.Add(new ReconciliationEntry
                {
                    Source = "eshop",
                    TransactionType = "authorization",
                    FinixId = payment.AuthorizationId!,
                    OrderId = payment.OrderId,
                    AmountCents = payment.AmountCents,
                    Currency = payment.Currency,
                    State = payment.AuthorizationState,
                    CreatedAt = payment.CreatedAt,
                    Matched = false,
                    MatchStatus = "eshop-only"
                });
            }

            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId) &&
                !finixTransferIds.Contains(payment.CaptureTransferId))
            {
                report.EshopOnlyTransactions.Add(new ReconciliationEntry
                {
                    Source = "eshop",
                    TransactionType = "capture",
                    FinixId = payment.CaptureTransferId!,
                    OrderId = payment.OrderId,
                    AmountCents = payment.CapturedAmountCents ?? payment.AmountCents,
                    Currency = payment.Currency,
                    State = payment.CaptureTransferState,
                    CreatedAt = payment.UpdatedAt,
                    Matched = false,
                    MatchStatus = "eshop-only"
                });
            }
        }

        foreach (var refund in localRefunds)
        {
            if (!string.IsNullOrWhiteSpace(refund.FinixRefundTransferId) &&
                !finixTransferIds.Contains(refund.FinixRefundTransferId))
            {
                report.EshopOnlyTransactions.Add(new ReconciliationEntry
                {
                    Source = "eshop",
                    TransactionType = "refund",
                    FinixId = refund.FinixRefundTransferId!,
                    OrderId = refund.OrderId,
                    AmountCents = refund.AmountCents,
                    Currency = null,
                    State = refund.State,
                    CreatedAt = refund.CreatedAt,
                    Matched = false,
                    MatchStatus = "eshop-only"
                });
            }
        }

        report.Summary.FinixAuthorizationCount = finixAuthorizations.Count;
        report.Summary.FinixTransferCount = finixTransfers.Count;
        report.Summary.MatchedCount = report.FinixTransactions.Count(e => e.Matched);
        report.Summary.FinixOnlyCount = report.FinixTransactions.Count(e => !e.Matched);
        report.Summary.EshopOnlyCount = report.EshopOnlyTransactions.Count;

        return report;
    }

    public async Task<SavedCardSummary> SavePaymentMethodAsync(
        string buyerId,
        SavePaymentMethodCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command?.Card == null)
        {
            throw new PaymentOperationException(400, "Card details are required.");
        }

        var identityId = await GetOrCreateFinixIdentityAsync(buyerId, cancellationToken);
        var card = ToFinixCard(command.Card);

        FinixPaymentInstrumentResult instrument;
        try
        {
            instrument = await _gateway.CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
        }
        catch (PaymentGatewayException ex)
        {
            throw new PaymentOperationException(
                ex.StatusCode is >= 400 and < 500 ? 422 : 502,
                $"Finix could not save the card: {ex.Message}",
                ex.Code);
        }

        var paymentMethod = new PaymentMethod(
            buyerId,
            instrument.Id,
            instrument.Brand,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            command.Alias);
        await _paymentMethodRepository.AddAsync(paymentMethod, cancellationToken);

        return ToSavedCardSummary(paymentMethod);
    }

    public async Task<IReadOnlyList<SavedCardSummary>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        var methods = await _paymentMethodRepository.ListAsync(
            new PaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return methods.Select(ToSavedCardSummary).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var paymentMethod = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (paymentMethod == null || !IsOwner(paymentMethod.BuyerId, buyerId))
        {
            throw new PaymentOperationException(404, "Saved payment method not found.");
        }

        if (paymentMethod.IsDeleted)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(paymentMethod.FinixPaymentInstrumentId))
        {
            try
            {
                await _gateway.DisablePaymentInstrumentAsync(paymentMethod.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (PaymentGatewayException ex) when (ex.StatusCode == 404)
            {
                // Already gone at Finix; still remove it locally.
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentOperationException(502,
                    $"Finix could not disable the saved card: {ex.Message}",
                    ex.Code);
            }
        }

        paymentMethod.MarkDeleted();
        await _paymentMethodRepository.UpdateAsync(paymentMethod, cancellationToken);
    }

    private async Task<Order?> GetOrderWithItemsAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    private async Task<OrderPayment?> GetPaymentForOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private async Task<IReadOnlyList<Refund>> GetRefundsAsync(int paymentId, CancellationToken cancellationToken)
    {
        return await _refundRepository.ListAsync(new RefundsByPaymentIdSpec(paymentId), cancellationToken);
    }

    private async Task AddOrUpdatePaymentAsync(OrderPayment payment, bool isNew, CancellationToken cancellationToken)
    {
        if (isNew)
        {
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }
    }

    private async Task<string> GetOrCreateFinixIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var buyerLock = BuyerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await buyerLock.WaitAsync(cancellationToken);
        try
        {
            var existing = await _finixCustomerRepository.FirstOrDefaultAsync(
                new FinixCustomerByBuyerSpec(buyerId), cancellationToken);
            if (existing != null)
            {
                return existing.FinixIdentityId;
            }

            string identityId;
            try
            {
                identityId = await _gateway.CreateBuyerIdentityAsync(cancellationToken);
            }
            catch (PaymentGatewayException ex)
            {
                throw new PaymentOperationException(502,
                    $"Finix could not create the shopper identity: {ex.Message}",
                    ex.Code);
            }

            var customer = new FinixCustomer(buyerId, identityId);
            await _finixCustomerRepository.AddAsync(customer, cancellationToken);
            return identityId;
        }
        finally
        {
            buyerLock.Release();
        }
    }

    private async Task RefreshPaymentBestEffortAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                await RefreshCaptureAsync(payment, waitForSettlement: false, cancellationToken);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
            else if (!string.IsNullOrWhiteSpace(payment.AuthorizationId) &&
                     string.Equals(payment.AuthorizationState, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                var authorization = await _gateway.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                if (authorization != null)
                {
                    payment.RecordAuthorization(
                        authorization.Id,
                        payment.AuthorizationIdempotencyKey ?? $"order-{payment.OrderId}-authorization",
                        authorization.State,
                        authorization.ExpiresAt);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
            }
        }
        catch (Exception)
        {
            // Reporting an order must not fail because Finix is temporarily unavailable.
        }
    }

    private async Task RefreshCaptureAsync(
        OrderPayment payment,
        bool waitForSettlement,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            return;
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(waitForSettlement ? 60 : 0);
        while (true)
        {
            var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId, cancellationToken);
            if (transfer == null)
            {
                if (waitForSettlement)
                {
                    throw new PaymentOperationException(409, "The captured transfer no longer exists at Finix.");
                }

                return;
            }

            payment.RecordCaptureState(transfer.State);
            var succeeded = string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase);

            if (succeeded || !waitForSettlement)
            {
                var fee = await _gateway.GetTransferFeeCentsAsync(payment.CaptureTransferId, cancellationToken);
                var captured = payment.CapturedAmountCents ?? payment.AmountCents;
                if (fee.HasValue)
                {
                    payment.RecordFees(fee.Value, captured - fee.Value);
                }
                else if (succeeded)
                {
                    payment.RecordFees(0m, captured);
                }
                else
                {
                    payment.MarkFeePending();
                }

                return;
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                payment.RecordFailure(transfer.FailureCode, transfer.FailureMessage);
                return;
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                payment.MarkFeePending();
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task EnsureTransferSettledAsync(OrderPayment payment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            throw new PaymentOperationException(409, "The order has no captured transfer to refund.");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var transfer = await _gateway.GetTransferAsync(payment.CaptureTransferId, cancellationToken);
            if (transfer == null)
            {
                throw new PaymentOperationException(409, "The captured transfer no longer exists at Finix.");
            }

            payment.RecordCaptureState(transfer.State);
            if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"The captured transfer is {transfer.State} and cannot be partially refunded.");
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new PaymentOperationException(409,
                    $"Finix is still settling the captured transfer (state {transfer.State}). " +
                    "Partial refunds become available after settlement; try again shortly.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private static string BuildActionableCaptureMessage(
        OrderPayment payment,
        FinixAuthorizationResult? fresh,
        PaymentGatewayException exception)
    {
        if (fresh?.IsVoid == true)
        {
            return $"The authorization {payment.AuthorizationId} was voided, so the held funds were released and cannot be captured. " +
                   "Cancel the order or create a new order and pay again.";
        }

        if (fresh?.ExpiresAt.HasValue == true && fresh.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            return $"The authorization {payment.AuthorizationId} expired at {fresh.ExpiresAt.Value:O}. " +
                   "The hold can no longer be captured; cancel the order and create a new order to take payment again.";
        }

        if (string.Equals(fresh?.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            return $"The authorization {payment.AuthorizationId} failed ({fresh?.FailureMessage ?? "no failure detail"}). " +
                   "Cancel the order and create a new order to take payment again.";
        }

        return $"Finix could not capture authorization {payment.AuthorizationId}: {exception.Message}. " +
               "The hold may have expired or been voided; cancel the order and create a new order if payment is still needed.";
    }

    private static Address ToAddress(ShippingAddressCommand? address)
    {
        return new Address(
            string.IsNullOrWhiteSpace(address?.Street) ? "1 Main St" : address!.Street!.Trim(),
            string.IsNullOrWhiteSpace(address?.City) ? "Springfield" : address!.City!.Trim(),
            string.IsNullOrWhiteSpace(address?.State) ? "IL" : address!.State!.Trim(),
            string.IsNullOrWhiteSpace(address?.Country) ? "USA" : address!.Country!.Trim(),
            string.IsNullOrWhiteSpace(address?.ZipCode) ? "62704" : address!.ZipCode!.Trim());
    }

    private static FinixCard ToFinixCard(CardCommand card)
    {
        var number = (card.Number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (number.Length < 12 || number.Length > 19 || !number.All(char.IsDigit))
        {
            throw new PaymentOperationException(400, "The card number is invalid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentOperationException(400, "The card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTime.UtcNow.Year || card.ExpirationYear > DateTime.UtcNow.Year + 30)
        {
            throw new PaymentOperationException(400, "The card expiration year is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode) &&
            (card.SecurityCode.Length is < 3 or > 4 || !card.SecurityCode.All(char.IsDigit)))
        {
            throw new PaymentOperationException(400, "The card security code is invalid.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }

        var address = card.Address ?? new ShippingAddressCommand();
        var finixAddress = new FinixAddress(
            string.IsNullOrWhiteSpace(address.Street) ? "1 Main St" : address.Street!.Trim(),
            null,
            string.IsNullOrWhiteSpace(address.City) ? "Springfield" : address.City!.Trim(),
            string.IsNullOrWhiteSpace(address.State) ? "IL" : address.State!.Trim(),
            string.IsNullOrWhiteSpace(address.ZipCode) ? "62704" : address.ZipCode!.Trim(),
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country!.Trim());

        return new FinixCard(
            number,
            card.ExpirationMonth,
            card.ExpirationYear,
            string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode,
            card.Name.Trim(),
            finixAddress);
    }

    private OrderSummary ToOrderSummary(Order order, OrderPayment? payment, IReadOnlyList<Refund> refunds)
    {
        var refundedAmount = refunds.Where(r => !IsFailedRefund(r)).Sum(r => r.AmountCents);
        var captured = payment?.CapturedAmountCents ?? 0;
        var refundable = Math.Max(0, captured - refundedAmount);

        var summary = new OrderSummary
        {
            OrderId = order.Id,
            BuyerId = order.BuyerId,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = order.Total(),
            TotalCents = ToCents(order.Total()),
            Currency = payment?.Currency ?? ConfiguredCurrency,
            ShipToAddress = new ShippingAddressSummary
            {
                Street = order.ShipToAddress.Street,
                City = order.ShipToAddress.City,
                State = order.ShipToAddress.State,
                Country = order.ShipToAddress.Country,
                ZipCode = order.ShipToAddress.ZipCode
            },
            Items = order.OrderItems.Select(i => new OrderItemSummary
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
                LineTotal = i.UnitPrice * i.Units
            }).ToList()
        };

        if (payment != null)
        {
            summary.Payment = new PaymentSummary
            {
                PaymentId = payment.Id,
                Status = GetPaymentStatus(order, payment, refundedAmount),
                Currency = payment.Currency,
                AmountCents = payment.AmountCents,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationState = payment.AuthorizationState,
                AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
                IsVoided = payment.IsVoided,
                CaptureTransferId = payment.CaptureTransferId,
                CaptureState = payment.CaptureTransferState,
                CapturedAmountCents = payment.CapturedAmountCents,
                FeeCents = payment.FeeCents,
                NetCents = payment.NetCents,
                FeePending = payment.FeePending,
                RefundedAmountCents = refundedAmount,
                RefundableAmountCents = refundable,
                FailureCode = payment.FailureCode,
                FailureMessage = payment.FailureMessage,
                Refunds = refunds.Select(ToRefundSummary).ToList()
            };
        }

        return summary;
    }

    private static string GetPaymentStatus(Order order, OrderPayment payment, long refundedAmount)
    {
        if (order.Status == OrderStatus.Cancelled)
        {
            return "Cancelled";
        }

        if (payment.IsVoided)
        {
            return "Voided";
        }

        if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId))
        {
            if (payment.CapturedAmountCents is > 0 && refundedAmount >= payment.CapturedAmountCents.Value)
            {
                return "Refunded";
            }

            if (refundedAmount > 0)
            {
                return "PartiallyRefunded";
            }

            return string.Equals(payment.CaptureTransferState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                ? "Captured"
                : "CapturePending";
        }

        if (!string.IsNullOrWhiteSpace(payment.AuthorizationId))
        {
            return string.Equals(payment.AuthorizationState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                ? "Authorized"
                : payment.AuthorizationState ?? "Authorized";
        }

        return order.Status.ToString();
    }

    private static RefundSummary ToRefundSummary(Refund refund)
    {
        return new RefundSummary
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixRefundTransferId,
            AmountCents = refund.AmountCents,
            State = refund.State,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }

    private static SavedCardSummary ToSavedCardSummary(PaymentMethod paymentMethod)
    {
        return new SavedCardSummary
        {
            PaymentMethodId = paymentMethod.Id,
            Alias = paymentMethod.Alias,
            Brand = paymentMethod.Brand,
            Last4 = paymentMethod.Last4,
            ExpirationMonth = paymentMethod.ExpirationMonth,
            ExpirationYear = paymentMethod.ExpirationYear,
            CreatedAt = paymentMethod.CreatedAt
        };
    }

    private static bool IsFailedRefund(Refund refund)
    {
        return string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(refund.State, "CANCELED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwner(string owner, string caller)
    {
        return !string.IsNullOrWhiteSpace(owner) &&
               string.Equals(owner, caller, StringComparison.OrdinalIgnoreCase);
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static string BuildRefundIdempotencyId(int paymentId, string callerKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{paymentId}:{callerKey}"));
        return $"eshop-refund-{paymentId}-{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static int? TryReadOrderIdTag(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags == null)
        {
            return null;
        }

        foreach (var key in new[] { "orderId", "order_id" })
        {
            if (tags.TryGetValue(key, out var value) && int.TryParse(value, out var orderId))
            {
                return orderId;
            }
        }

        return null;
    }
}
