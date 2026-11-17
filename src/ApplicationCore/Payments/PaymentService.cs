using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

public class PaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IRepository<FinixCustomer> _finixCustomerRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IFinixClient _finixClient;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Order> orderRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IRepository<FinixCustomer> finixCustomerRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IFinixClient finixClient,
        IOptions<FinixOptions> options,
        ILogger<PaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _finixCustomerRepository = finixCustomerRepository;
        _catalogItemRepository = catalogItemRepository;
        _finixClient = finixClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<OrderWithPayment> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<(int CatalogItemId, int Quantity)> items,
        Address? shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        if (items is null || items.Count == 0)
        {
            throw new PaymentException(400, "At least one catalog item is required.");
        }

        if (items.Any(i => i.Quantity <= 0))
        {
            throw new PaymentException(400, "Item quantities must be greater than zero.");
        }

        var catalogItemIds = items.Select(i => i.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(catalogItemIds), cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var requestedItem in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == requestedItem.CatalogItemId);
            if (catalogItem is null)
            {
                throw new PaymentException(400, $"Catalog item {requestedItem.CatalogItemId} was not found.");
            }

            var itemOrdered = new CatalogItemOrdered(
                catalogItem.Id,
                catalogItem.Name,
                catalogItem.PictureUri ?? string.Empty);

            orderItems.Add(new OrderItem(itemOrdered, catalogItem.Price, requestedItem.Quantity));
        }

        var address = shippingAddress ?? new Address("Not provided", "Not provided", "N/A", "USA", "00000");
        var order = new Order(buyerId, address, orderItems);
        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentException(400, "The order total must be greater than zero.");
        }

        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new Payment(order.Id, buyerId, amountCents, _options.Currency);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return new OrderWithPayment(order, payment);
    }

    public async Task<Payment> PayOrderAsync(
        string buyerId,
        int orderId,
        int? savedPaymentMethodId,
        PaymentCard? card,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, buyerId, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken)
                ?? throw new PaymentException(409, "This order has no payment record. Place the order again.");

            if (payment.Status is OrderPaymentStatus.Authorized
                or OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return payment;
            }

            if (payment.Status == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException(409, "This order was cancelled and can no longer be paid.");
            }

            if (savedPaymentMethodId.HasValue && card is not null)
            {
                throw new PaymentException(400, "Provide either a saved payment method or card details, not both.");
            }

            if (payment.Status is OrderPaymentStatus.Failed or OrderPaymentStatus.AuthorizationExpired)
            {
                payment.PrepareForRetry();
            }

            string source;
            if (savedPaymentMethodId.HasValue)
            {
                var savedMethod = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                    new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId), cancellationToken)
                    ?? throw new PaymentException(404, $"Saved payment method {savedPaymentMethodId} was not found.");

                source = savedMethod.FinixPaymentInstrumentId;
            }
            else
            {
                if (card is null)
                {
                    throw new PaymentException(400, "Card details or a saved payment method are required.");
                }

                ValidateCard(card);
                var identityId = await GetOrCreateFinixCustomerAsync(buyerId, card, cancellationToken);
                var paymentInstrument = await _finixClient.CreatePaymentInstrumentAsync(
                    ToPaymentInstrumentRequest(card, identityId, buyerId), cancellationToken);

                source = paymentInstrument.Id;
            }

            payment.SetAuthorizationIdempotencyKey(payment.AuthorizationIdempotencyKey ?? Guid.NewGuid().ToString("N"));
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            FinixAuthorization authorization;
            try
            {
                authorization = await _finixClient.CreateAuthorizationAsync(
                    new FinixCreateAuthorizationRequest
                    {
                        Amount = payment.AmountCents,
                        Currency = payment.Currency,
                        Merchant = _options.MerchantId,
                        Source = source,
                        IdempotencyId = payment.AuthorizationIdempotencyKey,
                        Tags = new Dictionary<string, string>
                        {
                            ["eshop_order_id"] = order.Id.ToString(),
                            ["eshop_kind"] = "authorization"
                        }
                    },
                    cancellationToken);
            }
            catch (FinixException ex)
            {
                payment.MarkAuthorizationFailed(ex.ErrorCode, ex.Message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(402, $"Finix could not authorize the payment: {ex.Message}");
            }

            if (!string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage ?? authorization.State);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(402,
                    $"Finix did not authorize the payment ({authorization.FailureCode ?? authorization.State}): {authorization.FailureMessage}");
            }

            payment.MarkAuthorized(authorization.Id, authorization.State, source);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            await GetOrderAsync(orderId, null, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken)
                ?? throw new PaymentException(409, "This order has no payment record.");

            if (payment.Status is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                return payment;
            }

            if (payment.Status == OrderPaymentStatus.Cancelled)
            {
                throw new PaymentException(409, "This order was cancelled and cannot be fulfilled.");
            }

            if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                throw new PaymentException(409,
                    "This order has not been authorized yet. The shopper must pay before it can be fulfilled.");
            }

            FinixAuthorization authorization;
            try
            {
                authorization = await _finixClient.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            }
            catch (FinixException ex)
            {
                throw new PaymentException(409,
                    $"Finix could not verify authorization {payment.AuthorizationId}: {ex.Message}. " +
                    "Ask the shopper to pay again, or cancel the order.");
            }

            if (authorization.IsVoid
                || !string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase)
                || (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow))
            {
                payment.MarkAuthorizationExpired(
                    authorization.FailureCode ?? "AUTHORIZATION_UNAVAILABLE",
                    authorization.IsVoid
                        ? "The card authorization was voided."
                        : authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow
                            ? "The card authorization has expired."
                            : $"The card authorization is {authorization.State}.");
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                throw new PaymentException(409,
                    "The card authorization can no longer be captured because it is " +
                    (authorization.IsVoid ? "voided" : "expired or unavailable") +
                    ". Ask the shopper to pay again, or cancel this order.");
            }

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                var existingTransfer = await _finixClient.GetTransferAsync(authorization.Transfer, cancellationToken);
                payment.MarkCaptured(
                    existingTransfer.Id,
                    existingTransfer.State,
                    existingTransfer.Amount,
                    existingTransfer.Fee ?? 0);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return payment;
            }

            FinixAuthorization capture;
            try
            {
                capture = await _finixClient.CaptureAuthorizationAsync(
                    authorization.Id, payment.AmountCents, cancellationToken);
            }
            catch (FinixException ex)
            {
                payment.MarkCaptureFailed(ex.ErrorCode, ex.Message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(409,
                    $"Finix could not capture the authorized funds: {ex.Message}. " +
                    "The hold may have expired or been voided; ask the shopper to pay again, or cancel the order.");
            }

            var transferId = capture.Transfer;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var refreshed = await _finixClient.GetAuthorizationAsync(capture.Id, cancellationToken);
                transferId = refreshed.Transfer;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentException(502,
                    "Finix captured the authorization but did not return a transfer id. " +
                    "Reconciliation will surface the transaction; contact Finix support before retrying.");
            }

            var transfer = await WaitForSettledTransferAsync(transferId, cancellationToken);
            payment.MarkCaptured(
                transfer.Id,
                transfer.State,
                transfer.Amount > 0 ? transfer.Amount : payment.AmountCents,
                transfer.Fee ?? 0);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Payment> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            await GetOrderAsync(orderId, null, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken)
                ?? throw new PaymentException(409, "This order has no payment record.");

            if (payment.Status == OrderPaymentStatus.Cancelled)
            {
                return payment;
            }

            if (payment.Status is OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded)
            {
                throw new PaymentException(409,
                    "The payment has already been captured. Use the refunds endpoint instead of cancelling.");
            }

            if (!string.IsNullOrWhiteSpace(payment.AuthorizationId)
                && payment.Status == OrderPaymentStatus.Authorized)
            {
                try
                {
                    var voided = await _finixClient.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    payment.MarkCancelled(voided.State);
                }
                catch (FinixException ex)
                {
                    throw new PaymentException(409,
                        $"Finix could not release the authorized hold: {ex.Message}. " +
                        "If the payment was already captured, use the refunds endpoint.");
                }
            }
            else
            {
                payment.MarkCancelled(payment.AuthorizationState);
            }

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return payment;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResult> RefundOrderAsync(
        int orderId,
        long amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (amountCents <= 0)
        {
            throw new PaymentException(400, "The refund amount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentException(400, "An idempotency key is required for refunds.");
        }

        if (idempotencyKey.Length > 255)
        {
            throw new PaymentException(400, "The idempotency key must be 255 characters or fewer.");
        }

        var gate = LockFor($"order:{orderId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            await GetOrderAsync(orderId, null, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken)
                ?? throw new PaymentException(409, "This order has no payment record.");

            var existingRefund = payment.Refunds.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            if (existingRefund is not null)
            {
                return new RefundResult(payment, existingRefund);
            }

            if (payment.Status is not (OrderPaymentStatus.Captured
                or OrderPaymentStatus.PartiallyRefunded
                or OrderPaymentStatus.Refunded))
            {
                throw new PaymentException(409, "Only captured payments can be refunded.");
            }

            if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            {
                throw new PaymentException(409, "The captured Finix transfer is not available for refund.");
            }

            if (amountCents > payment.RefundableAmountCents)
            {
                throw new PaymentException(400,
                    $"The refund amount ({amountCents}) exceeds the refundable amount " +
                    $"({payment.RefundableAmountCents}) for this order.");
            }

            var transfer = await EnsureCaptureSettledAsync(payment, amountCents, cancellationToken);

            var refund = new Refund(payment.Id, idempotencyKey, amountCents);
            payment.AddRefund(refund);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            try
            {
                var finixRefund = await _finixClient.CreateRefundAsync(
                    transfer.Id, amountCents, cancellationToken);

                if (string.Equals(finixRefund.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                {
                    refund.MarkFailed(finixRefund.FailureCode, finixRefund.FailureMessage ?? finixRefund.State);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentException(409,
                        $"Finix accepted the refund request but it failed: {finixRefund.FailureMessage ?? finixRefund.State}");
                }

                if (string.Equals(finixRefund.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    refund.MarkSucceeded(finixRefund.Id, finixRefund.State);
                }
                else
                {
                    refund.MarkPending(finixRefund.Id, finixRefund.State);
                }

                payment.RecalculateRefundStatus();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                return new RefundResult(payment, refund);
            }
            catch (FinixException ex)
            {
                refund.MarkFailed(ex.ErrorCode, ex.Message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentException(409, $"Finix could not refund the payment: {ex.Message}");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderWithPayment>> GetMyOrdersAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new PaymentsByBuyerSpec(buyerId), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        foreach (var payment in payments)
        {
            var changed = false;

            if (!string.IsNullOrWhiteSpace(payment.CaptureTransferId)
                && string.Equals(payment.CaptureTransferState, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var captureTransfer = await _finixClient.GetTransferAsync(
                        payment.CaptureTransferId, cancellationToken);
                    if (!string.Equals(captureTransfer.State, payment.CaptureTransferState, StringComparison.OrdinalIgnoreCase)
                        || captureTransfer.Fee != payment.FinixFeeCents)
                    {
                        payment.UpdateCaptureState(captureTransfer.State, captureTransfer.Fee);
                        changed = true;
                    }
                }
                catch (FinixException)
                {
                }
            }

            foreach (var refund in payment.Refunds
                         .Where(r => r.State == RefundState.Pending
                                     && !string.IsNullOrWhiteSpace(r.FinixTransferId))
                         .ToList())
            {
                try
                {
                    var transfer = await _finixClient.GetTransferAsync(
                        refund.FinixTransferId!, cancellationToken);
                    if (string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    {
                        refund.MarkSucceeded(transfer.Id, transfer.State);
                        changed = true;
                    }
                    else if (string.Equals(transfer.State, "FAILED", StringComparison.OrdinalIgnoreCase))
                    {
                        refund.MarkFailed(transfer.FailureCode, transfer.FailureMessage ?? transfer.State);
                        changed = true;
                    }
                    else if (!string.Equals(transfer.State, refund.FinixState, StringComparison.OrdinalIgnoreCase))
                    {
                        refund.MarkPending(transfer.Id, transfer.State);
                        changed = true;
                    }
                }
                catch (FinixException)
                {
                }
            }

            if (changed)
            {
                payment.RecalculateRefundStatus();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
            }
        }

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(order => new OrderWithPayment(
                order,
                paymentsByOrder.TryGetValue(order.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<SavedPaymentMethod> SavePaymentMethodAsync(
        string buyerId,
        SavedCardDetails card,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        if (card is null)
        {
            throw new PaymentException(400, "Card details are required.");
        }

        var paymentCard = new PaymentCard
        {
            Number = card.Number,
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Name = card.Name,
            Address = card.Address
        };
        ValidateCard(paymentCard);

        var identityId = await GetOrCreateFinixCustomerAsync(buyerId, paymentCard, cancellationToken);
        var paymentInstrument = await _finixClient.CreatePaymentInstrumentAsync(
            ToPaymentInstrumentRequest(paymentCard, identityId, buyerId), cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            paymentInstrument.Id,
            paymentInstrument.Brand,
            paymentInstrument.LastFour,
            paymentInstrument.ExpirationMonth ?? card.ExpirationMonth,
            paymentInstrument.ExpirationYear ?? card.ExpirationYear,
            paymentInstrument.CardType,
            paymentInstrument.Name ?? card.Name);

        await _savedPaymentMethodRepository.AddAsync(saved, cancellationToken);
        return saved;
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetPaymentMethodsAsync(
        string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentException(401, "A signed-in shopper is required.");
        }

        return await _savedPaymentMethodRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor($"buyer:{buyerId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken)
                ?? throw new PaymentException(404, $"Saved payment method {paymentMethodId} was not found.");

            saved.MarkDeleted();
            await _savedPaymentMethodRepository.UpdateAsync(saved, cancellationToken);

            try
            {
                await _finixClient.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (FinixException ex)
            {
                _logger.LogWarning(
                    "Finix payment instrument {PaymentInstrumentId} could not be disabled after local deletion: {Message}",
                    saved.FinixPaymentInstrumentId,
                    ex.Message);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentException(400, "The 'from' value must be earlier than or equal to the 'to' value.");
        }

        var authorizations = await _finixClient.ListAuthorizationsAsync(from, to, cancellationToken);
        var transfers = await _finixClient.ListTransfersAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new PaymentsWithRefundsSpec(), cancellationToken);
        var orders = await _orderRepository.ListAsync(cancellationToken);

        var ordersById = orders.ToDictionary(o => o.Id);
        var paymentsByAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.AuthorizationId))
            .ToDictionary(p => p.AuthorizationId!, p => p);
        var paymentsByTransfer = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.CaptureTransferId))
            .ToDictionary(p => p.CaptureTransferId!, p => p);
        var refundsByTransfer = payments
            .SelectMany(p => p.Refunds.Select(r => new { Payment = p, Refund = r }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Refund.FinixTransferId))
            .ToDictionary(x => x.Refund.FinixTransferId!, x => x);

        var report = new ReconciliationReport { From = from, To = to };
        var matchedAuthorizationIds = new HashSet<string>(StringComparer.Ordinal);
        var matchedTransferIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authorization in authorizations)
        {
            matchedAuthorizationIds.Add(authorization.Id);
            var orderId = ParseTagOrderId(authorization.Tags);
            if (orderId is null && paymentsByAuthorization.TryGetValue(authorization.Id, out var payment))
            {
                orderId = payment.OrderId;
            }

            var matched = orderId.HasValue
                && ordersById.ContainsKey(orderId.Value)
                && paymentsByAuthorization.TryGetValue(authorization.Id, out var matchedPayment)
                && matchedPayment.OrderId == orderId.Value;

            report.FinixTransactions.Add(new ReconciliationEntry
            {
                FinixId = authorization.Id,
                Kind = "AUTHORIZATION",
                Type = "AUTHORIZATION",
                State = authorization.State,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency,
                CreatedAt = authorization.CreatedAt,
                OrderId = orderId,
                Matched = matched,
                MatchType = matched ? "AUTHORIZATION_ID" : null
            });
        }

        foreach (var transfer in transfers)
        {
            matchedTransferIds.Add(transfer.Id);
            var isRefund = string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase);
            var orderId = ParseTagOrderId(transfer.Tags);
            var matched = false;
            string? matchType = null;

            if (isRefund && refundsByTransfer.TryGetValue(transfer.Id, out var refundMatch))
            {
                orderId ??= refundMatch.Payment.OrderId;
                matched = true;
                matchType = "REFUND_TRANSFER_ID";
            }
            else if (!isRefund && paymentsByTransfer.TryGetValue(transfer.Id, out var captureMatch))
            {
                orderId ??= captureMatch.OrderId;
                matched = true;
                matchType = "CAPTURE_TRANSFER_ID";
            }
            else if (orderId.HasValue && ordersById.ContainsKey(orderId.Value))
            {
                matched = true;
                matchType = "ORDER_TAG";
            }

            report.FinixTransactions.Add(new ReconciliationEntry
            {
                FinixId = transfer.Id,
                Kind = isRefund ? "REFUND" : "CAPTURE",
                Type = transfer.Type,
                State = transfer.State,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency,
                CreatedAt = transfer.CreatedAt,
                OrderId = orderId,
                Matched = matched,
                MatchType = matchType
            });
        }

        foreach (var payment in payments)
        {
            var missingAuthorization = !string.IsNullOrWhiteSpace(payment.AuthorizationId)
                && !matchedAuthorizationIds.Contains(payment.AuthorizationId!)
                && IsInRange(payment.AuthorizedAt, from, to);

            var missingCapture = !string.IsNullOrWhiteSpace(payment.CaptureTransferId)
                && !matchedTransferIds.Contains(payment.CaptureTransferId!)
                && IsInRange(payment.CapturedAt, from, to);

            if (missingAuthorization)
            {
                try
                {
                    var authorization = await _finixClient.GetAuthorizationAsync(
                        payment.AuthorizationId!, cancellationToken);
                    matchedAuthorizationIds.Add(authorization.Id);
                    report.FinixTransactions.Add(new ReconciliationEntry
                    {
                        FinixId = authorization.Id,
                        Kind = "AUTHORIZATION",
                        Type = "AUTHORIZATION",
                        State = authorization.State,
                        AmountCents = authorization.Amount,
                        Currency = authorization.Currency,
                        CreatedAt = authorization.CreatedAt,
                        OrderId = payment.OrderId,
                        Matched = true,
                        MatchType = "AUTHORIZATION_ID"
                    });
                    missingAuthorization = false;
                }
                catch (FinixException)
                {
                }
            }

            if (missingCapture)
            {
                try
                {
                    var transfer = await _finixClient.GetTransferAsync(
                        payment.CaptureTransferId!, cancellationToken);
                    matchedTransferIds.Add(transfer.Id);
                    report.FinixTransactions.Add(new ReconciliationEntry
                    {
                        FinixId = transfer.Id,
                        Kind = "CAPTURE",
                        Type = transfer.Type,
                        State = transfer.State,
                        AmountCents = transfer.Amount,
                        Currency = transfer.Currency,
                        CreatedAt = transfer.CreatedAt,
                        OrderId = payment.OrderId,
                        Matched = true,
                        MatchType = "CAPTURE_TRANSFER_ID"
                    });
                    missingCapture = false;
                }
                catch (FinixException)
                {
                }
            }

            if (!missingAuthorization && !missingCapture)
            {
                continue;
            }

            report.EshopOrdersWithoutFinixRecord.Add(new EshopPaymentEntry
            {
                OrderId = payment.OrderId,
                PaymentId = payment.Id,
                PaymentStatus = payment.Status.ToString(),
                AuthorizationId = payment.AuthorizationId,
                CaptureTransferId = payment.CaptureTransferId,
                AmountCents = payment.AmountCents,
                CapturedAmountCents = payment.CapturedAmountCents,
                Currency = payment.Currency,
                AuthorizedAt = payment.AuthorizedAt,
                CapturedAt = payment.CapturedAt
            });
        }

        report.FinixTransactions = report.FinixTransactions
            .OrderBy(e => e.CreatedAt ?? DateTimeOffset.MinValue)
            .ToList();
        report.UnmatchedFinixTransactions = report.FinixTransactions.Where(e => !e.Matched).ToList();

        return report;
    }

    private async Task<Order> GetOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);

        if (order is null
            || (buyerId is not null && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal)))
        {
            throw new PaymentException(404, $"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Payment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        return await _paymentRepository.FirstOrDefaultAsync(
            new PaymentByOrderIdSpec(orderId), cancellationToken);
    }

    private async Task<string> GetOrCreateFinixCustomerAsync(
        string buyerId,
        PaymentCard card,
        CancellationToken cancellationToken)
    {
        var gate = LockFor($"buyer:{buyerId}");
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _finixCustomerRepository.FirstOrDefaultAsync(
                new FinixCustomerByBuyerSpec(buyerId), cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            var nameParts = (card.Name ?? "eShop Shopper").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var identity = await _finixClient.CreateIdentityAsync(
                new FinixCreateIdentityRequest
                {
                    Entity = new FinixIdentityEntity
                    {
                        FirstName = nameParts.FirstOrDefault() ?? "eShop",
                        LastName = nameParts.Length > 1 ? nameParts[^1] : "Shopper",
                        Email = buyerId.Contains('@') ? buyerId : $"{SafeEmailLocalPart(buyerId)}@eshop.local",
                        PersonalAddress = card.Address ?? DefaultAddress()
                    },
                    IdentityRoles = new List<string> { "BUYER" },
                    Type = "PERSONAL",
                    Tags = new Dictionary<string, string> { ["eshop_buyer"] = buyerId }
                },
                cancellationToken);

            var customer = new FinixCustomer(buyerId, identity.Id);
            await _finixCustomerRepository.AddAsync(customer, cancellationToken);
            return identity.Id;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FinixTransfer> EnsureCaptureSettledAsync(
        Payment payment,
        long requestedRefundAmount,
        CancellationToken cancellationToken)
    {
        var transfer = await _finixClient.GetTransferAsync(payment.CaptureTransferId!, cancellationToken);

        if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase)
            && requestedRefundAmount < (payment.CapturedAmountCents ?? 0))
        {
            transfer = await WaitForSettledTransferAsync(transfer.Id, cancellationToken);
        }

        payment.UpdateCaptureState(transfer.State, transfer.Fee);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);

        if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
        {
            if (requestedRefundAmount < (payment.CapturedAmountCents ?? 0))
            {
                throw new PaymentException(409,
                    "The card capture is still pending at Finix. A partial refund can be requested once the capture settles; " +
                    "a full refund can be requested now.");
            }

            return transfer;
        }

        if (!string.Equals(transfer.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentException(409,
                $"The captured transfer is {transfer.State} at Finix and cannot be refunded. " +
                "Review the transaction in the reconciliation report.");
        }

        return transfer;
    }

    private async Task<FinixTransfer> WaitForSettledTransferAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);
        for (var attempt = 0;
             attempt < 12 && string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase);
             attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await _finixClient.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private static FinixCreatePaymentInstrumentRequest ToPaymentInstrumentRequest(
        PaymentCard card,
        string identityId,
        string buyerId)
    {
        return new FinixCreatePaymentInstrumentRequest
        {
            Type = "PAYMENT_CARD",
            Identity = identityId,
            Number = NormalizeCardNumber(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            SecurityCode = card.SecurityCode,
            Name = card.Name ?? "eShop Shopper",
            Address = card.Address ?? DefaultAddress(),
            Tags = new Dictionary<string, string> { ["eshop_buyer"] = buyerId }
        };
    }

    private static void ValidateCard(PaymentCard card)
    {
        if (string.IsNullOrWhiteSpace(card.Number))
        {
            throw new PaymentException(400, "Card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentException(400, "Card expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            throw new PaymentException(400, "Card expiration year is in the past.");
        }
    }

    private static FinixAddress DefaultAddress()
    {
        return new FinixAddress
        {
            Line1 = "Not provided",
            City = "Not provided",
            Region = "N/A",
            PostalCode = "00000",
            Country = "USA"
        };
    }

    private static string NormalizeCardNumber(string number)
    {
        return new string(number.Where(char.IsDigit).ToArray());
    }

    private static string SafeEmailLocalPart(string buyerId)
    {
        var cleaned = new string(buyerId.Where(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-').ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "shopper" : cleaned;
    }

    private static long ToCents(decimal amount)
    {
        return (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
    }

    private static bool IsInRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to)
    {
        return value.HasValue && value.Value >= from && value.Value <= to;
    }

    private static int? ParseTagOrderId(Dictionary<string, JsonElement>? tags)
    {
        if (tags is null || !tags.TryGetValue("eshop_order_id", out var element))
        {
            return null;
        }

        var text = element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        return int.TryParse(text, out var orderId) ? orderId : null;
    }

    private static SemaphoreSlim LockFor(string key)
    {
        return Locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
    }
}
