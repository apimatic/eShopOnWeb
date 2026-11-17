using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

public class OrderPaymentService : IOrderPaymentService
{
    private const string DefaultStreet = "1 eShop Way";
    private const string DefaultCity = "Seattle";
    private const string DefaultState = "WA";
    private const string DefaultCountry = "USA";
    private const string DefaultZipCode = "98101";

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<PaymentMethod> _paymentMethodRepository;
    private readonly IFinixClient _finixClient;
    private readonly FinixOptions _finixOptions;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IRepository<Payment> paymentRepository,
        IRepository<PaymentMethod> paymentMethodRepository,
        IFinixClient finixClient,
        FinixOptions finixOptions,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogItemRepository = catalogItemRepository;
        _paymentRepository = paymentRepository;
        _paymentMethodRepository = paymentMethodRepository;
        _finixClient = finixClient;
        _finixOptions = finixOptions;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderPaymentView> CreateOrderAsync(CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(command.BuyerId, nameof(command.BuyerId));

        if (command.Items == null || command.Items.Count == 0)
        {
            throw new PaymentOperationException(400, "An order must contain at least one catalog item.");
        }

        var requestedItems = command.Items
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new CreateOrderItemCommand(g.Key, g.Sum(i => i.Quantity)))
            .ToList();

        if (requestedItems.Any(i => i.CatalogItemId <= 0 || i.Quantity <= 0))
        {
            throw new PaymentOperationException(400, "Each order item must have a positive catalogItemId and quantity.");
        }

        var catalogIds = requestedItems.Select(i => i.CatalogItemId).ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(new CatalogItemsSpecification(catalogIds), cancellationToken);

        var missing = catalogIds.Except(catalogItems.Select(c => c.Id)).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentOperationException(400, $"Catalog item(s) not found: {string.Join(", ", missing)}.");
        }

        var items = new List<OrderItem>();
        foreach (var requested in requestedItems)
        {
            var catalogItem = catalogItems.First(c => c.Id == requested.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            items.Add(new OrderItem(itemOrdered, catalogItem.Price, requested.Quantity));
        }

        var address = ToAddress(command.ShippingAddress);
        var order = new Order(command.BuyerId, address, items);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        _logger.LogInformation("Created order {OrderId} for buyer {BuyerId} awaiting payment", order.Id, command.BuyerId);
        return BuildOrderView(order, null);
    }

    public async Task<OrderPaymentView> PayOrderAsync(PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(command.OrderId, cancellationToken);
        if (order == null || !string.Equals(order.BuyerId, command.BuyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Order {command.OrderId} was not found.");
        }

        if (order.Status == OrderStatus.Cancelled || order.Status == OrderStatus.Fulfilled ||
            order.Status == OrderStatus.PartiallyRefunded || order.Status == OrderStatus.Refunded)
        {
            throw new PaymentOperationException(409, $"Order {order.Id} is {order.Status} and cannot be paid.");
        }

        var payment = await GetPaymentAsync(order.Id, cancellationToken);

        if (payment != null)
        {
            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return BuildOrderView(order, payment);
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                var existingAuthorization = await _finixClient.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                if (IsUsableAuthorization(existingAuthorization))
                {
                    payment.RecordAuthorization(existingAuthorization.Id, existingAuthorization.State ?? "UNKNOWN", existingAuthorization.ExpiresAt, payment.FinixPaymentInstrumentId);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    order.MarkAuthorized();
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    return BuildOrderView(order, payment);
                }

                payment.RecordFailure("AUTHORIZATION_NOT_USABLE",
                    $"The previous authorization is {existingAuthorization.State}{(existingAuthorization.IsVoid ? " (voided)" : string.Empty)} and can no longer be used.");
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
        }

        var attempt = (payment?.AuthorizationAttempt ?? 0) + 1;
        var sourcePaymentInstrumentId = await ResolvePaymentInstrumentAsync(command, order, cancellationToken);
        var amountCents = ToCents(order.Total());

        if (amountCents <= 0)
        {
            throw new PaymentOperationException(400, "The order total must be greater than zero.");
        }

        var idempotencyId = $"eshop-{order.PaymentReference}-authorize-{attempt}";
        var tags = new Dictionary<string, string>
        {
            ["order_id"] = order.Id.ToString(CultureInfo.InvariantCulture),
            ["app"] = "eshoponweb"
        };

        FinixAuthorization authorization;
        try
        {
            authorization = await _finixClient.CreateAuthorizationAsync(
                amountCents,
                _finixOptions.NormalizedCurrency,
                sourcePaymentInstrumentId,
                idempotencyId,
                tags,
                cancellationToken);
        }
        catch (FinixApiException ex) when (ex.IsDuplicateIdempotency && !string.IsNullOrWhiteSpace(ex.DuplicateAuthorizationId))
        {
            authorization = await _finixClient.GetAuthorizationAsync(ex.DuplicateAuthorizationId!, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Authorization failed for order {OrderId}: {Message}", order.Id, ex.Message);
            throw new PaymentOperationException(402, ex.Message, ex);
        }

        if (!IsUsableAuthorization(authorization))
        {
            var failureCode = authorization.FailureCode ?? "AUTHORIZATION_FAILED";
            var failureMessage = authorization.FailureMessage ??
                $"Finix returned authorization state {authorization.State}{(authorization.IsVoid ? " (voided)" : string.Empty)}{(authorization.ExpiresAt.HasValue && authorization.ExpiresAt <= DateTimeOffset.UtcNow ? " (expired)" : string.Empty)}.";
            if (payment == null)
            {
                payment = Payment.CreateFailed(order.Id, amountCents, _finixOptions.NormalizedCurrency,
                    sourcePaymentInstrumentId, attempt, failureCode, failureMessage);
                await _paymentRepository.AddAsync(payment, cancellationToken);
            }
            else
            {
                payment.RecordFailure(failureCode, failureMessage, attempt);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }

            throw new PaymentOperationException(402,
                $"The card authorization was not approved. {failureMessage} (code: {failureCode}).");
        }

        if (payment == null)
        {
            payment = new Payment(order.Id, amountCents, _finixOptions.NormalizedCurrency,
                sourcePaymentInstrumentId, authorization.Id, authorization.State ?? "SUCCEEDED",
                authorization.ExpiresAt, attempt);
            await _paymentRepository.AddAsync(payment, cancellationToken);
        }
        else
        {
            payment.RecordAuthorization(authorization.Id, authorization.State ?? "SUCCEEDED",
                authorization.ExpiresAt, sourcePaymentInstrumentId, attempt);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
        }

        order.MarkAuthorized();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Authorized order {OrderId} for {AmountCents} {Currency} with Finix authorization {AuthorizationId}",
            order.Id, amountCents, _finixOptions.NormalizedCurrency, authorization.Id);

        return BuildOrderView(order, payment);
    }

    public async Task<OrderPaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            throw new PaymentOperationException(409, $"Order {orderId} has no authorized payment. The shopper must pay before fulfilment.");
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            return BuildOrderView(order, payment);
        }

        if (payment.Status == PaymentStatus.Voided)
        {
            throw new PaymentOperationException(409, $"Order {orderId} was cancelled and its authorization was released. It cannot be fulfilled.");
        }

        if (payment.Status == PaymentStatus.Failed || string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            throw new PaymentOperationException(409, $"Order {orderId} has no usable authorization. The shopper must pay again before it can be fulfilled.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            throw new PaymentOperationException(409, $"Order {orderId} was cancelled and cannot be fulfilled.");
        }

        var authorization = await _finixClient.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
        if (!IsUsableAuthorization(authorization))
        {
            throw new PaymentOperationException(409,
                BuildUnusableAuthorizationMessage(payment.FinixAuthorizationId, authorization));
        }

        var captureAmount = payment.AmountCents;
        FinixAuthorization capture;
        try
        {
            capture = await _finixClient.CaptureAuthorizationAsync(payment.FinixAuthorizationId, captureAmount, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.Message.Contains("already captured", StringComparison.OrdinalIgnoreCase))
        {
            capture = await _finixClient.GetAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(409,
                $"Finix could not capture authorization {payment.FinixAuthorizationId} for order {orderId}: {ex.Message}");
        }

        var transferId = capture.Transfer ?? authorization.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            throw new PaymentOperationException(409,
                $"Finix captured authorization {payment.FinixAuthorizationId} but did not return a capture transfer id. Use reconciliation to investigate before retrying.");
        }

        var transfer = await WaitForTransferSettledAsync(transferId, TimeSpan.FromSeconds(75), cancellationToken);
        if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentOperationException(409,
                $"Finix capture transfer {transfer.Id} failed: {transfer.FailureMessage ?? transfer.FailureCode ?? "no failure detail returned"}.");
        }

        var capturedAmount = transfer.Amount > 0 ? transfer.Amount : captureAmount;
        var feeCents = await CalculateFeeCentsAsync(transferId, payment.FinixAuthorizationId, cancellationToken);
        var netCents = capturedAmount - feeCents;

        payment.RecordCapture(transfer.Id, transfer.State ?? capture.State ?? "UNKNOWN", capturedAmount, feeCents, netCents);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Captured order {OrderId}: transfer {TransferId}, amount {CapturedAmount}, fee {Fee}, net {Net}",
            order.Id, transfer.Id, capturedAmount, feeCents, netCents);

        return BuildOrderView(order, payment);
    }

    public async Task<OrderPaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await GetOrderAsync(orderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentOperationException(404, $"Order {orderId} was not found.");
        }

        var payment = await GetPaymentAsync(orderId, cancellationToken);
        if (payment == null)
        {
            order.MarkCancelled();
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return BuildOrderView(order, null);
        }

        if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
        {
            throw new PaymentOperationException(409,
                $"Order {orderId} has already been captured. Use the refunds endpoint instead of cancelling it.");
        }

        if (payment.Status == PaymentStatus.Voided || order.Status == OrderStatus.Cancelled)
        {
            return BuildOrderView(order, payment);
        }

        if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
        {
            try
            {
                var voided = await _finixClient.VoidAuthorizationAsync(payment.FinixAuthorizationId, cancellationToken);
                payment.RecordVoid(voided.State ?? "CANCELED");
            }
            catch (FinixApiException ex) when (ex.Message.Contains("already captured", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"Order {orderId} has already been captured. Use the refunds endpoint instead of cancelling it.");
            }
            catch (FinixApiException ex) when (ex.Message.Contains("already void", StringComparison.OrdinalIgnoreCase))
            {
                payment.RecordVoid("CANCELED");
            }
            catch (FinixApiException ex)
            {
                throw new PaymentOperationException(409,
                    $"Finix could not release authorization {payment.FinixAuthorizationId} for order {orderId}: {ex.Message}");
            }
        }
        else
        {
            payment.RecordVoid("CANCELED");
        }

        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        order.MarkCancelled();
        await _orderRepository.UpdateAsync(order, cancellationToken);

        _logger.LogInformation("Cancelled order {OrderId} and released its authorization", order.Id);
        return BuildOrderView(order, payment);
    }

    public async Task<RefundView> RefundOrderAsync(RefundOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentOperationException(400, "A non-empty idempotencyKey is required for refunds.");
        }

        var order = await GetOrderAsync(command.OrderId, cancellationToken);
        if (order == null)
        {
            throw new PaymentOperationException(404, $"Order {command.OrderId} was not found.");
        }

        var payment = await GetPaymentAsync(order.Id, cancellationToken);
        if (payment == null)
        {
            throw new PaymentOperationException(409, $"Order {order.Id} has no captured payment to refund.");
        }

        if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
        {
            throw new PaymentOperationException(409,
                $"Order {order.Id} has not been fulfilled. Cancel it instead of refunding it.");
        }

        var existing = payment.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal));
        if (existing != null)
        {
            return BuildRefundView(existing);
        }

        if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
        {
            throw new PaymentOperationException(409, $"Order {order.Id} has no capture transfer id to refund.");
        }

        if (!string.Equals(payment.CaptureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            var settled = await WaitForTransferSettledAsync(payment.FinixTransferId, TimeSpan.FromSeconds(75), cancellationToken);
            if (string.Equals(settled.State, "FAILED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settled.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(settled.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentOperationException(409,
                    $"The capture transfer {payment.FinixTransferId} is {settled.State} and cannot be refunded.");
            }
        }

        var alreadyRefunded = payment.RefundedAmountCents();
        var remaining = payment.CapturedAmountCents - alreadyRefunded;

        if (remaining > 0)
        {
            try
            {
                var finixUnreversed = await _finixClient.GetUnreversedAmountAsync(payment.FinixTransferId, cancellationToken);
                if (finixUnreversed.HasValue)
                {
                    remaining = Math.Min(remaining, finixUnreversed.Value);
                }
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not read Finix unreversed amount for transfer {TransferId}: {Message}",
                    payment.FinixTransferId, ex.Message);
            }
        }

        if (remaining <= 0)
        {
            throw new PaymentOperationException(409, $"Order {order.Id} has no remaining refundable amount.");
        }

        var refundAmount = command.AmountCents ?? remaining;

        if (refundAmount <= 0)
        {
            throw new PaymentOperationException(400, "The refund amount must be greater than zero.");
        }

        if (refundAmount > remaining)
        {
            throw new PaymentOperationException(409,
                $"The refund amount {refundAmount} exceeds the remaining refundable amount {remaining} for order {order.Id}.");
        }

        var finixIdempotencyKey = $"eshop-{order.PaymentReference}-refund-{command.IdempotencyKey}";

        FinixTransfer refundTransfer;
        try
        {
            refundTransfer = await _finixClient.CreateRefundAsync(payment.FinixTransferId, refundAmount, finixIdempotencyKey, cancellationToken);
        }
        catch (FinixApiException ex) when (ex.IsDuplicateIdempotency)
        {
            var existingTransfer = !string.IsNullOrWhiteSpace(ex.DuplicateTransferId)
                ? await _finixClient.GetTransferAsync(ex.DuplicateTransferId!, cancellationToken)
                : await _finixClient.FindTransferByIdempotencyAsync(finixIdempotencyKey, cancellationToken);

            if (existingTransfer == null)
            {
                throw new PaymentOperationException(409,
                    $"Finix reported a duplicate refund idempotency key but did not return the existing refund: {ex.Message}");
            }
            refundTransfer = existingTransfer;
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(409,
                $"Finix could not refund order {order.Id}: {ex.Message}");
        }

        var refund = payment.AddRefund(refundTransfer.Id,
            refundTransfer.ParentTransfer ?? payment.FinixTransferId,
            refundTransfer.Amount > 0 ? refundTransfer.Amount : refundAmount,
            refundTransfer.State ?? "PENDING",
            command.IdempotencyKey);

        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        if (!string.Equals(refund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
        {
            order.MarkRefunded(payment.Status == PaymentStatus.Refunded);
            await _orderRepository.UpdateAsync(order, cancellationToken);
        }

        _logger.LogInformation("Refunded order {OrderId}: Finix refund {RefundId}, amount {Amount}, state {State}",
            order.Id, refund.FinixRefundId, refund.AmountCents, refund.State);

        return BuildRefundView(refund);
    }

    public async Task<IReadOnlyList<OrderPaymentView>> GetMyOrdersAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var orders = await _orderRepository.ListAsync(new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        if (orders.Count == 0)
        {
            return Array.Empty<OrderPaymentView>();
        }

        var orderIds = orders.Select(o => o.Id).ToArray();
        var payments = await _paymentRepository.ListAsync(new PaymentsByOrderIdsSpecification(orderIds), cancellationToken);
        var paymentsByOrderId = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => BuildOrderView(o, paymentsByOrderId.TryGetValue(o.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentOperationException(400, "The 'from' date-time must be earlier than or equal to 'to'.");
        }

        var transfers = await _finixClient.ListTransfersAsync(from, to, cancellationToken);
        var authorizations = await _finixClient.ListAuthorizationsAsync(from, to, cancellationToken);
        var allPayments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpecification(), cancellationToken);

        bool InRange(DateTimeOffset value) => value >= from && value <= to;
        var payments = allPayments
            .Where(p => InRange(p.CreatedAt) || InRange(p.UpdatedAt) || p.Refunds.Any(r => InRange(r.CreatedAt)))
            .ToList();

        var paymentsByTransferId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixTransferId))
            .GroupBy(p => p.FinixTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var paymentsByAuthorizationId = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .GroupBy(p => p.FinixAuthorizationId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var refundsByFinixId = payments
            .SelectMany(p => p.Refunds.Select(r => new { Payment = p, Refund = r }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Refund.FinixRefundId))
            .GroupBy(x => x.Refund.FinixRefundId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var report = new ReconciliationReport
        {
            From = from,
            To = to,
            MerchantId = _finixOptions.MerchantId,
            Currency = _finixOptions.NormalizedCurrency,
            FinixTransferCount = transfers.Count,
            FinixAuthorizationCount = authorizations.Count,
            EshopPaymentCount = payments.Count
        };

        var matchedLocalKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers)
        {
            var item = new ReconciliationTransferItem
            {
                FinixTransferId = transfer.Id,
                Type = transfer.Type,
                State = transfer.State,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency,
                ParentTransferId = transfer.ParentTransfer,
                CreatedAt = transfer.CreatedAt
            };

            if (paymentsByTransferId.TryGetValue(transfer.Id, out var payment))
            {
                item.MatchedOrderId = payment.OrderId;
                item.MatchedLocalType = "Payment";
                item.MatchedLocalId = payment.Id;
                matchedLocalKeys.Add($"Payment:{payment.Id}");
            }
            else if (refundsByFinixId.TryGetValue(transfer.Id, out var refundMatch))
            {
                item.MatchedOrderId = refundMatch.Payment.OrderId;
                item.MatchedLocalType = "Refund";
                item.MatchedLocalId = refundMatch.Refund.Id;
                matchedLocalKeys.Add($"Refund:{refundMatch.Refund.Id}");
            }

            report.Transfers.Add(item);
        }

        foreach (var authorization in authorizations)
        {
            var item = new ReconciliationAuthorizationItem
            {
                FinixAuthorizationId = authorization.Id,
                State = authorization.State,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt
            };

            if (paymentsByAuthorizationId.TryGetValue(authorization.Id, out var payment))
            {
                item.MatchedOrderId = payment.OrderId;
                item.MatchedLocalId = payment.Id;
                matchedLocalKeys.Add($"Payment:{payment.Id}");
            }

            report.Authorizations.Add(item);
        }

        foreach (var payment in payments)
        {
            if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId) &&
                !authorizations.Any(a => string.Equals(a.Id, payment.FinixAuthorizationId, StringComparison.Ordinal)) &&
                !matchedLocalKeys.Contains($"Payment:{payment.Id}"))
            {
                report.EshopOnly.Add(new ReconciliationEshopOnlyItem
                {
                    LocalType = "Payment",
                    LocalId = payment.Id,
                    OrderId = payment.OrderId,
                    FinixId = payment.FinixAuthorizationId,
                    AmountCents = payment.AmountCents,
                    State = payment.AuthorizationState,
                    CreatedAt = payment.CreatedAt
                });
            }

            if (!string.IsNullOrWhiteSpace(payment.FinixTransferId) &&
                !transfers.Any(t => string.Equals(t.Id, payment.FinixTransferId, StringComparison.Ordinal)) &&
                !matchedLocalKeys.Contains($"Payment:{payment.Id}"))
            {
                report.EshopOnly.Add(new ReconciliationEshopOnlyItem
                {
                    LocalType = "Payment",
                    LocalId = payment.Id,
                    OrderId = payment.OrderId,
                    FinixId = payment.FinixTransferId,
                    AmountCents = payment.CapturedAmountCents,
                    State = payment.CaptureState,
                    CreatedAt = payment.UpdatedAt
                });
            }

            foreach (var refund in payment.Refunds)
            {
                if (!string.IsNullOrWhiteSpace(refund.FinixRefundId) &&
                    !transfers.Any(t => string.Equals(t.Id, refund.FinixRefundId, StringComparison.Ordinal)) &&
                    !matchedLocalKeys.Contains($"Refund:{refund.Id}"))
                {
                    report.EshopOnly.Add(new ReconciliationEshopOnlyItem
                    {
                        LocalType = "Refund",
                        LocalId = refund.Id,
                        OrderId = payment.OrderId,
                        FinixId = refund.FinixRefundId,
                        AmountCents = refund.AmountCents,
                        State = refund.State,
                        CreatedAt = refund.CreatedAt
                    });
                }
            }
        }

        return report;
    }

    public async Task<SavedPaymentMethodView> SaveCardAsync(SaveCardCommand command, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(command.BuyerId, nameof(command.BuyerId));
        var card = command.Card ?? throw new PaymentOperationException(400, "Card details are required.");
        var cardNumber = NormalizeCardNumber(card.Number);
        ValidateCard(cardNumber, card);

        var (firstName, lastName) = SplitName(card.Name);
        var email = command.BuyerId.Contains('@', StringComparison.Ordinal) ? command.BuyerId : null;

        var identity = await _finixClient.CreateBuyerIdentityAsync(firstName, lastName, email, cancellationToken);
        var address = ToFinixAddress(card.BillingAddress);

        FinixPaymentInstrument instrument;
        try
        {
            instrument = await _finixClient.CreatePaymentInstrumentAsync(
                identity.Id,
                new FinixCardDetails(cardNumber, card.ExpirationMonth, card.ExpirationYear, card.SecurityCode!, card.Name!),
                address,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(402, $"Finix could not save the card: {ex.Message}", ex);
        }

        var saved = new PaymentMethod(command.BuyerId, identity.Id, instrument.Id, instrument.Brand,
            instrument.LastFour, instrument.CardType, instrument.ExpirationMonth, instrument.ExpirationYear);

        try
        {
            saved = await _paymentMethodRepository.AddAsync(saved, cancellationToken);
        }
        catch
        {
            try
            {
                await _finixClient.DisablePaymentInstrumentAsync(instrument.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not disable orphaned Finix payment instrument {PaymentInstrumentId}: {Message}",
                    instrument.Id, ex.Message);
            }
            throw;
        }

        _logger.LogInformation("Saved card {PaymentMethodId} for buyer {BuyerId}", saved.Id, command.BuyerId);
        return BuildSavedCardView(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodView>> GetSavedCardsAsync(string buyerId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var cards = await _paymentMethodRepository.ListAsync(new SavedPaymentMethodsByBuyerSpecification(buyerId), cancellationToken);
        return cards.Select(BuildSavedCardView).ToList();
    }

    public async Task DeleteSavedCardAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        var card = await _paymentMethodRepository.GetByIdAsync(paymentMethodId, cancellationToken);
        if (card == null || !string.Equals(card.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(404, $"Payment method {paymentMethodId} was not found.");
        }

        if (!string.IsNullOrWhiteSpace(card.CardId))
        {
            try
            {
                await _finixClient.DisablePaymentInstrumentAsync(card.CardId, cancellationToken);
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not disable Finix payment instrument {PaymentInstrumentId}: {Message}", card.CardId, ex.Message);
            }
        }

        await _paymentMethodRepository.DeleteAsync(card, cancellationToken);
        _logger.LogInformation("Deleted saved card {PaymentMethodId} for buyer {BuyerId}", paymentMethodId, buyerId);
    }

    private async Task<string> ResolvePaymentInstrumentAsync(PayOrderCommand command, Order order, CancellationToken cancellationToken)
    {
        if (command.PaymentMethodId.HasValue)
        {
            var saved = await _paymentMethodRepository.GetByIdAsync(command.PaymentMethodId.Value, cancellationToken);
            if (saved == null || !saved.Enabled || !string.Equals(saved.BuyerId, command.BuyerId, StringComparison.Ordinal))
            {
                throw new PaymentOperationException(404, $"Payment method {command.PaymentMethodId.Value} was not found.");
            }

            if (string.IsNullOrWhiteSpace(saved.CardId))
            {
                throw new PaymentOperationException(409, $"Payment method {saved.Id} has no usable Finix payment instrument.");
            }

            return saved.CardId;
        }

        if (command.Card == null)
        {
            throw new PaymentOperationException(400, "Provide either card details or a saved paymentMethodId.");
        }

        var cardNumber = NormalizeCardNumber(command.Card.Number);
        ValidateCard(cardNumber, command.Card);

        var (firstName, lastName) = SplitName(command.Card.Name);
        var email = command.BuyerId.Contains('@', StringComparison.Ordinal) ? command.BuyerId : null;
        var identity = await _finixClient.CreateBuyerIdentityAsync(firstName, lastName, email, cancellationToken);
        var address = command.Card.BillingAddress != null
            ? ToFinixAddress(command.Card.BillingAddress)
            : ToFinixAddress(order.ShipToAddress);

        FinixPaymentInstrument instrument;
        try
        {
            instrument = await _finixClient.CreatePaymentInstrumentAsync(
                identity.Id,
                new FinixCardDetails(cardNumber, command.Card.ExpirationMonth, command.Card.ExpirationYear,
                    command.Card.SecurityCode!, command.Card.Name!),
                address,
                cancellationToken);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(402, $"Finix could not tokenize the card: {ex.Message}", ex);
        }

        return instrument.Id;
    }

    private async Task<int> CalculateFeeCentsAsync(string transferId, string authorizationId, CancellationToken cancellationToken)
    {
        var fees = new List<FinixFee>();
        try
        {
            fees.AddRange(await _finixClient.ListFeesAsync(transferId, cancellationToken));
        }
        catch (FinixApiException ex)
        {
            _logger.LogWarning("Could not list transfer fees for {TransferId}: {Message}", transferId, ex.Message);
        }

        if (!string.IsNullOrWhiteSpace(authorizationId))
        {
            try
            {
                fees.AddRange(await _finixClient.ListFeesAsync(authorizationId, cancellationToken));
            }
            catch (FinixApiException ex)
            {
                _logger.LogWarning("Could not list authorization fees for {AuthorizationId}: {Message}", authorizationId, ex.Message);
            }
        }

        var distinctFees = fees
            .Where(f => !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var total = distinctFees.Sum(f => f.Amount);
        return (int)decimal.Round(total, 0, MidpointRounding.AwayFromZero);
    }

    private async Task<Order?> GetOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
    }

    private async Task<Payment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpecification(orderId), cancellationToken);
    }

    private static bool IsUsableAuthorization(FinixAuthorization authorization)
    {
        if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) || authorization.IsVoid)
        {
            return false;
        }

        return authorization.ExpiresAt == null || authorization.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private static string BuildUnusableAuthorizationMessage(string authorizationId, FinixAuthorization authorization)
    {
        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return $"The Finix authorization {authorizationId} expired at {authorization.ExpiresAt:O} and can no longer be captured. Cancel the order or have the shopper pay again.";
        }

        return $"The Finix authorization {authorizationId} is in state {authorization.State}{(authorization.IsVoid ? " (voided)" : string.Empty)} and can no longer be captured. Cancel the order or have the shopper pay again.";
    }

    private async Task<FinixTransfer> WaitForTransferSettledAsync(string transferId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);

        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static int ToCents(decimal amount)
    {
        return decimal.ToInt32(decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeCardNumber(string? number)
    {
        return (number ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static void ValidateCard(string cardNumber, CardCommand card)
    {
        if (string.IsNullOrWhiteSpace(cardNumber) || cardNumber.Length < 12 || cardNumber.Length > 19 || !cardNumber.All(char.IsDigit))
        {
            throw new PaymentOperationException(400, "A valid card number is required.");
        }

        if (card.ExpirationMonth < 1 || card.ExpirationMonth > 12 || card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentOperationException(400, "A valid future card expiration month and year are required.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) || !card.SecurityCode.All(char.IsDigit) || card.SecurityCode.Length is < 3 or > 4)
        {
            throw new PaymentOperationException(400, "A valid 3- or 4-digit security code is required.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentOperationException(400, "The cardholder name is required.");
        }
    }

    private static (string FirstName, string LastName) SplitName(string? name)
    {
        var cleaned = (name ?? string.Empty).Trim();
        if (cleaned.Length == 0)
        {
            return ("eShop", "Shopper");
        }

        var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1
            ? (parts[0], "Shopper")
            : (parts[0], parts[^1]);
    }

    private static Address ToAddress(ShippingAddressCommand? input)
    {
        if (input == null)
        {
            return new Address(DefaultStreet, DefaultCity, DefaultState, DefaultCountry, DefaultZipCode);
        }

        return new Address(
            string.IsNullOrWhiteSpace(input.Street) ? DefaultStreet : input.Street,
            string.IsNullOrWhiteSpace(input.City) ? DefaultCity : input.City,
            string.IsNullOrWhiteSpace(input.State) ? DefaultState : input.State,
            string.IsNullOrWhiteSpace(input.Country) ? DefaultCountry : input.Country,
            string.IsNullOrWhiteSpace(input.ZipCode) ? DefaultZipCode : input.ZipCode);
    }

    private static FinixAddress ToFinixAddress(ShippingAddressCommand? input)
    {
        if (input == null)
        {
            return new FinixAddress
            {
                Line1 = DefaultStreet,
                City = DefaultCity,
                Region = DefaultState,
                PostalCode = DefaultZipCode,
                Country = DefaultCountry
            };
        }

        return new FinixAddress
        {
            Line1 = Truncate(string.IsNullOrWhiteSpace(input.Street) ? DefaultStreet : input.Street, 35),
            City = Truncate(string.IsNullOrWhiteSpace(input.City) ? DefaultCity : input.City, 20),
            Region = Truncate(string.IsNullOrWhiteSpace(input.State) ? DefaultState : input.State, 2),
            PostalCode = Truncate(string.IsNullOrWhiteSpace(input.ZipCode) ? DefaultZipCode : input.ZipCode, 7),
            Country = NormalizeCountry(input.Country)
        };
    }

    private static FinixAddress ToFinixAddress(Address address)
    {
        return new FinixAddress
        {
            Line1 = Truncate(address.Street, 35),
            City = Truncate(address.City, 20),
            Region = Truncate(address.State, 2),
            PostalCode = Truncate(address.ZipCode, 7),
            Country = NormalizeCountry(address.Country)
        };
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string NormalizeCountry(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "USA";
        }

        var cleaned = country.Trim();
        if (cleaned.Length == 3)
        {
            return cleaned.ToUpperInvariant();
        }

        return cleaned.ToUpperInvariant() switch
        {
            "US" or "UNITED STATES" or "UNITED STATES OF AMERICA" => "USA",
            "CA" or "CANADA" => "CAN",
            "GB" or "UK" or "UNITED KINGDOM" => "GBR",
            "AU" or "AUSTRALIA" => "AUS",
            "DE" or "GERMANY" => "DEU",
            "FR" or "FRANCE" => "FRA",
            _ => "USA"
        };
    }

    private OrderPaymentView BuildOrderView(Order order, Payment? payment)
    {
        var view = new OrderPaymentView
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            OrderDate = order.OrderDate,
            Total = order.Total(),
            AmountCents = ToCents(order.Total()),
            Currency = payment?.Currency ?? _finixOptions.NormalizedCurrency,
            Items = order.OrderItems.Select(i => new OrderItemView
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units
            }).ToList()
        };

        if (payment != null)
        {
            view.Payment = BuildPaymentView(payment);
            view.Currency = payment.Currency;
            view.Refunds = payment.Refunds.Select(BuildRefundView).ToList();
        }

        return view;
    }

    private static PaymentView BuildPaymentView(Payment payment)
    {
        return new PaymentView
        {
            Status = payment.Status.ToString(),
            AmountCents = payment.AmountCents,
            Currency = payment.Currency,
            PaymentInstrumentId = payment.FinixPaymentInstrumentId,
            AuthorizationId = payment.FinixAuthorizationId,
            AuthorizationState = payment.AuthorizationState,
            AuthorizationExpiresAt = payment.AuthorizationExpiresAt,
            TransferId = payment.FinixTransferId,
            CaptureState = payment.CaptureState,
            CapturedAmountCents = payment.CapturedAmountCents,
            FeeCents = payment.FeeCents,
            NetCents = payment.NetCents,
            RefundedAmountCents = payment.RefundedAmountCents(),
            FailureCode = payment.FailureCode,
            FailureMessage = payment.FailureMessage
        };
    }

    private static RefundView BuildRefundView(Refund refund)
    {
        return new RefundView
        {
            RefundId = refund.Id,
            FinixRefundId = refund.FinixRefundId,
            ParentTransferId = refund.ParentTransferId,
            AmountCents = refund.AmountCents,
            State = refund.State,
            IdempotencyKey = refund.IdempotencyKey,
            CreatedAt = refund.CreatedAt
        };
    }

    private static SavedPaymentMethodView BuildSavedCardView(PaymentMethod card)
    {
        return new SavedPaymentMethodView
        {
            PaymentMethodId = card.Id,
            Alias = card.Alias,
            Brand = card.Brand,
            Last4 = card.Last4,
            CardType = card.CardType,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            CreatedAt = card.CreatedAt
        };
    }
}

