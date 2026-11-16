using System;
using System.Collections.Concurrent;
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
using Microsoft.eShopWeb.ApplicationCore.Models.Finix;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Owns the order payment lifecycle: authorize (hold), capture (fulfil), void (cancel) and
/// refund. Every state transition is guarded so retries and double-clicks are idempotent.
/// </summary>
public class OrderPaymentService : IOrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();

    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<CatalogItem> _catalogRepository;
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IFinixClient _finix;
    private readonly IUriComposer _uriComposer;
    private readonly FinixSettings _settings;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(IRepository<Order> orderRepository,
        IRepository<CatalogItem> catalogRepository,
        IRepository<Payment> paymentRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IFinixClient finix,
        IUriComposer uriComposer,
        IOptions<FinixSettings> settings,
        ILogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _catalogRepository = catalogRepository;
        _paymentRepository = paymentRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _finix = finix;
        _uriComposer = uriComposer;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<OrderSummary> CreateOrderAsync(string buyerId, IReadOnlyList<OrderLineInput> items,
        Address? shippingAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(401, "UNAUTHENTICATED", "A signed-in shopper is required.");
        }

        var lines = (items ?? Array.Empty<OrderLineInput>())
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.CatalogItemId)
            .Select(g => new OrderLineInput { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (lines.Count == 0)
        {
            throw new PaymentOperationException(400, "EMPTY_ORDER", "At least one catalog item with a positive quantity is required.");
        }

        var catalogItems = await _catalogRepository.ListAsync(
            new CatalogItemsSpecification(lines.Select(l => l.CatalogItemId).ToArray()), cancellationToken);
        var catalogById = catalogItems.ToDictionary(c => c.Id);

        var missing = lines.Where(l => !catalogById.ContainsKey(l.CatalogItemId))
            .Select(l => l.CatalogItemId).ToList();
        if (missing.Count > 0)
        {
            throw new PaymentOperationException(400, "UNKNOWN_CATALOG_ITEM",
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = lines.Select(line =>
        {
            var catalogItem = catalogById[line.CatalogItemId];
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/eCatalog-item-default.png"
                : _uriComposer.ComposePicUri(catalogItem.PictureUri);
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, line.Quantity);
        }).ToList();

        var address = shippingAddress ?? new Address("N/A", "N/A", "N/A", "N/A", "00000");
        var order = new Order(buyerId, address, orderItems);
        order = await _orderRepository.AddAsync(order, cancellationToken);

        var currency = _settings.ResolveCurrency();
        var totalCents = Money.ToCents(order.Total());
        var payment = new Payment(order.Id, buyerId, currency, totalCents);
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return ToOrderSummary(order, currency);
    }

    public Task<PaymentView> PayOrderAsync(string buyerId, int orderId, CardInput? card,
        int? savedPaymentMethodId, string? idempotencyKey, CancellationToken cancellationToken = default) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwned(order, buyerId);

            var payment = await LoadPaymentAsync(orderId, cancellationToken);
            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToPaymentView(payment);
            }

            if (order.Status == OrderStatus.Cancelled || payment.Status == PaymentStatus.Voided)
            {
                throw new PaymentOperationException(409, "ORDER_CANCELLED",
                    "This order was cancelled and can no longer be paid.");
            }

            var (sourceId, brand, last4, securityCode, usedSavedMethodId) =
                await ResolvePaymentSourceAsync(buyerId, card, savedPaymentMethodId, cancellationToken);

            payment.BeginAuthorizationAttempt();

            var finixIdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey)
                ? $"eshop-{payment.AuthorizationIdempotencySeed}-authorize-{payment.AuthorizationAttemptCount}"
                : $"eshop-{payment.AuthorizationIdempotencySeed}-{idempotencyKey.Trim()}";

            try
            {
                var authorization = await _finix.CreateAuthorizationAsync(new FinixCreateAuthorizationRequest
                {
                    Amount = payment.AmountCents,
                    Currency = payment.Currency,
                    Merchant = _settings.MerchantId!,
                    Source = sourceId,
                    SecurityCode = securityCode,
                    IdempotencyId = finixIdempotencyKey,
                    Tags = new Dictionary<string, string>
                    {
                        ["orderId"] = orderId.ToString(),
                        ["buyerId"] = buyerId
                    }
                }, cancellationToken);

                if (!IsAuthorizedState(authorization.State))
                {
                    payment.RecordAuthorizationFailure(authorization.FailureCode, authorization.FailureMessage);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);

                    throw new PaymentOperationException(402, "AUTHORIZATION_DECLINED",
                        BuildFailureMessage("The card authorization was declined", authorization.State,
                            authorization.FailureCode, authorization.FailureMessage));
                }

                payment.RecordAuthorization(authorization.Id, authorization.State ?? "SUCCEEDED",
                    brand, last4, usedSavedMethodId);
                order.MarkAuthorized();

                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);

                return ToPaymentView(payment);
            }
            catch (FinixApiException ex)
            {
                if (ex.StatusCode >= 500 || ex.StatusCode == 409)
                {
                    throw new PaymentOperationException(502, "FINIX_AUTHORIZATION_UNCONFIRMED",
                        $"Finix did not confirm the authorization: {ex.Message} Retry with the same request.");
                }

                payment.RecordAuthorizationFailure(ex.Code, ex.Message);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentOperationException(402, ex.Code ?? "AUTHORIZATION_FAILED", ex.Message);
            }
        });

    public Task<PaymentView> FulfilOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return ToPaymentView(payment);
            }

            if (order.Status == OrderStatus.Cancelled || payment.Status == PaymentStatus.Voided)
            {
                throw new PaymentOperationException(409, "ORDER_CANCELLED",
                    "This order was cancelled, so there is no hold to capture. The shopper must place and pay for a new order.");
            }

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                throw new PaymentOperationException(409, "ORDER_NOT_AUTHORIZED",
                    "This order has no successful card authorization. Pay for the order before fulfilling it.");
            }

            var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);

            var transferId = authorization.TransferId;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                EnsureAuthorizable(authorization);

                try
                {
                    var captured = await _finix.CaptureAuthorizationAsync(payment.AuthorizationId,
                        payment.AmountCents, cancellationToken);
                    transferId = captured.TransferId;
                }
                catch (FinixApiException ex)
                {
                    var refreshed = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    transferId = refreshed.TransferId;

                    if (string.IsNullOrWhiteSpace(transferId))
                    {
                        payment.RecordFailedCapture(ex.Code, ex.Message);
                        await _paymentRepository.UpdateAsync(payment, cancellationToken);
                        throw new PaymentOperationException(409, "AUTHORIZATION_NOT_CAPTURABLE",
                            BuildFailureMessage("Finix could not capture the authorization", refreshed.State,
                                refreshed.FailureCode ?? ex.Code, refreshed.FailureMessage ?? ex.Message) +
                            " The order was not fulfilled; the shopper may need to pay again.");
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentOperationException(502, "CAPTURE_TRANSFER_MISSING",
                    "Finix accepted the capture but did not return a transfer id. Retry the fulfilment request.");
            }

            var transfer = await WaitForTransferAsync(transferId, cancellationToken);

            if (transfer.State is "FAILED" or "CANCELED" or "RETURNED")
            {
                payment.RecordFailedCapture(transfer.FailureCode, transfer.FailureMessage);
                payment.RecordCaptureState(transfer.State ?? "UNKNOWN");
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentOperationException(409, "CAPTURE_NOT_COMPLETED",
                    BuildFailureMessage("Finix did not complete the capture", transfer.State,
                        transfer.FailureCode, transfer.FailureMessage) +
                    " The order was not fulfilled.");
            }

            var capturedAmount = transfer.Amount > 0 ? transfer.Amount : payment.AmountCents;
            var fee = transfer.Fee ?? 0;
            var net = Math.Max(0, capturedAmount - fee);

            payment.RecordCapture(transfer.Id, transfer.State ?? "PENDING", capturedAmount, fee, net);
            order.MarkFulfilled();

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return ToPaymentView(payment);
        });

    public Task<PaymentView> CancelOrderAsync(int orderId, CancellationToken cancellationToken = default) =>
        WithOrderLockAsync(orderId, async () =>
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (order.Status == OrderStatus.Cancelled || payment.Status == PaymentStatus.Voided)
            {
                return ToPaymentView(payment);
            }

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded
                || order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentOperationException(409, "ORDER_ALREADY_FULFILLED",
                    "This order has already been fulfilled. Use the refunds endpoint to return captured money.");
            }

            if (string.IsNullOrWhiteSpace(payment.AuthorizationId))
            {
                payment.RecordVoid("NO_AUTHORIZATION");
                order.MarkCancelled();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return ToPaymentView(payment);
            }

            var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            if (authorization.IsVoid == true || authorization.State == "CANCELED" || authorization.VoidState == "SUCCEEDED")
            {
                payment.RecordVoid(authorization.State);
                order.MarkCancelled();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return ToPaymentView(payment);
            }

            try
            {
                var voided = await _finix.VoidAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                payment.RecordVoid(voided.State ?? "CANCELED");
            }
            catch (FinixApiException ex)
            {
                var refreshed = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                if (refreshed.IsVoid != true && refreshed.VoidState != "SUCCEEDED")
                {
                    throw new PaymentOperationException(409, "AUTHORIZATION_NOT_VOIDABLE",
                        BuildFailureMessage("Finix could not release the hold", refreshed.State,
                            refreshed.FailureCode ?? ex.Code, refreshed.FailureMessage ?? ex.Message) +
                        " No money was captured; retry the cancellation.");
                }

                payment.RecordVoid(refreshed.State);
            }

            order.MarkCancelled();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return ToPaymentView(payment);
        });

    public Task<RefundView> RefundOrderAsync(string buyerId, int orderId, long? amountCents,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        WithOrderLockAsync(orderId, async () =>
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new PaymentOperationException(400, "IDEMPOTENCY_KEY_REQUIRED",
                    "A caller-supplied idempotency key is required for refunds.");
            }

            var order = await LoadOrderAsync(orderId, cancellationToken);
            EnsureOwned(order, buyerId);

            var payment = await LoadPaymentAsync(orderId, cancellationToken);

            if (string.IsNullOrWhiteSpace(payment.CaptureTransferId) || payment.CapturedAmountCents is null)
            {
                throw new PaymentOperationException(409, "ORDER_NOT_FULFILLED",
                    "This order has not been fulfilled yet, so there is no captured payment to refund.");
            }

            var key = idempotencyKey.Trim();
            var refund = payment.FindRefundByIdempotencyKey(key);
            var captured = payment.CapturedAmountCents.Value;

            if (refund != null)
            {
                if (refund.Status == RefundStatus.Succeeded)
                {
                    return ToRefundView(refund);
                }

                if (refund.Status == RefundStatus.Pending)
                {
                    var pendingMatch = await TryReconcileRefundAsync(payment.CaptureTransferId, refund, cancellationToken);
                    if (pendingMatch != null)
                    {
                        payment.ApplyRefundResult(refund, pendingMatch.Id, pendingMatch.State ?? "PENDING",
                            pendingMatch.State != "FAILED");
                        order.MarkRefunded(payment.RefundedAmountCents >= captured);
                        await _paymentRepository.UpdateAsync(payment, cancellationToken);
                        await _orderRepository.UpdateAsync(order, cancellationToken);
                        return ToRefundView(refund);
                    }

                    if (DateTimeOffset.UtcNow - refund.CreatedAt < TimeSpan.FromMinutes(1))
                    {
                        return ToRefundView(refund);
                    }
                }

                refund.MarkPending();
            }

            var requestedAmount = refund?.AmountCents ?? amountCents ?? payment.RefundableAmountCents;

            if (requestedAmount <= 0)
            {
                throw new PaymentOperationException(400, "INVALID_REFUND_AMOUNT",
                    "The refund amount must be greater than zero.");
            }

            if (requestedAmount > payment.RefundableAmountCents)
            {
                throw new PaymentOperationException(409, "REFUND_EXCEEDS_CAPTURED",
                    $"The refund amount ({Money.FromCents(requestedAmount):0.00}) exceeds the remaining refundable amount " +
                    $"({Money.FromCents(payment.RefundableAmountCents):0.00}).");
            }

            var captureTransfer = await _finix.GetTransferAsync(payment.CaptureTransferId, cancellationToken);
            if (requestedAmount < captured && captureTransfer.State != "SUCCEEDED")
            {
                captureTransfer = await WaitForTransferAsync(payment.CaptureTransferId, cancellationToken);
                if (captureTransfer.State != "SUCCEEDED")
                {
                    throw new PaymentOperationException(409, "CAPTURE_STILL_PENDING",
                        $"Finix is still settling the capture (state {captureTransfer.State ?? "UNKNOWN"}). " +
                        "Partial refunds are available once the capture succeeds; retry shortly.");
                }
            }

            if (refund == null)
            {
                refund = payment.AddRefund(requestedAmount, key);
            }

            // Persist the pending refund first so the idempotency key survives a retry.
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            try
            {
                var reversal = await _finix.CreateRefundAsync(payment.CaptureTransferId, requestedAmount, cancellationToken);
                var succeeded = reversal.State != "FAILED";

                payment.ApplyRefundResult(refund, reversal.Id, reversal.State ?? "PENDING", succeeded);

                if (!succeeded)
                {
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentOperationException(502, "REFUND_FAILED",
                        BuildFailureMessage("Finix did not complete the refund", reversal.State,
                            reversal.FailureCode, reversal.FailureMessage));
                }

                order.MarkRefunded(payment.RefundedAmountCents >= captured);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);

                return ToRefundView(refund);
            }
            catch (FinixApiException ex)
            {
                var reconciled = await TryReconcileRefundAsync(payment.CaptureTransferId, refund, cancellationToken);
                if (reconciled != null)
                {
                    payment.ApplyRefundResult(refund, reconciled.Id, reconciled.State ?? "PENDING",
                        reconciled.State != "FAILED");
                    order.MarkRefunded(payment.RefundedAmountCents >= captured);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    await _orderRepository.UpdateAsync(order, cancellationToken);
                    return ToRefundView(refund);
                }

                refund.MarkFailed(ex.Code ?? "FINIX_ERROR");
                await _paymentRepository.UpdateAsync(payment, cancellationToken);

                var statusCode = ex.StatusCode is 409 or 422 ? 409 : 502;
                throw new PaymentOperationException(statusCode, ex.Code ?? "REFUND_FAILED", ex.Message);
            }
        });

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);

        if (orders.Count == 0)
        {
            return Array.Empty<OrderView>();
        }

        var payments = await _paymentRepository.ListAsync(
            new PaymentsByOrderIdsSpec(orders.Select(o => o.Id).ToList()), cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        return orders
            .OrderByDescending(o => o.OrderDate)
            .Select(o => ToOrderView(o, paymentsByOrder.GetValueOrDefault(o.Id)))
            .ToList();
    }

    private async Task<(string SourceId, string? Brand, string? Last4, string? SecurityCode, int? SavedId)>
        ResolvePaymentSourceAsync(string buyerId, CardInput? card, int? savedPaymentMethodId,
            CancellationToken cancellationToken)
    {
        if (savedPaymentMethodId.HasValue)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(savedPaymentMethodId.Value, buyerId), cancellationToken);

            if (saved == null)
            {
                throw new PaymentOperationException(404, "PAYMENT_METHOD_NOT_FOUND",
                    "Saved card not found for this shopper.");
            }

            return (saved.FinixPaymentInstrumentId, saved.Brand, saved.Last4, card?.SecurityCode, saved.Id);
        }

        if (card == null)
        {
            throw new PaymentOperationException(400, "PAYMENT_SOURCE_REQUIRED",
                "Provide card details or the id of one of the shopper's saved payment methods.");
        }

        if (string.IsNullOrWhiteSpace(card.Number) || string.IsNullOrWhiteSpace(card.Name) ||
            card.ExpirationMonth is < 1 or > 12 || card.ExpirationYear < 2000)
        {
            throw new PaymentOperationException(400, "INVALID_CARD",
                "Card number, cardholder name, expiration month and expiration year are required.");
        }

        try
        {
            var identity = await _finix.CreateBuyerIdentityAsync(buyerId, card.Name, cancellationToken);
            var instrument = await _finix.CreatePaymentInstrumentAsync(new FinixCreatePaymentInstrumentRequest
            {
                Identity = identity.Id,
                Name = card.Name.Trim(),
                Number = SavedPaymentMethodService.NormalizeCardNumber(card.Number),
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Address = SavedPaymentMethodService.ToFinixAddress(card.Address),
                Tags = new Dictionary<string, string> { ["buyerId"] = buyerId }
            }, cancellationToken);

            return (instrument.Id, instrument.Brand, instrument.LastFour, card.SecurityCode, null);
        }
        catch (FinixApiException ex)
        {
            throw new PaymentOperationException(402, ex.Code ?? "CARD_TOKENIZATION_FAILED",
                $"Finix could not accept the card: {ex.Message}");
        }
    }

    private async Task<Order> LoadOrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order == null)
        {
            throw new PaymentOperationException(404, "ORDER_NOT_FOUND", "Order not found.");
        }

        return order;
    }

    private async Task<Payment> LoadPaymentAsync(int orderId, CancellationToken cancellationToken)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(orderId), cancellationToken);
        if (payment == null)
        {
            throw new PaymentOperationException(409, "PAYMENT_NOT_INITIALIZED",
                "This order has no payment record. Create the order through POST /api/orders first.");
        }

        return payment;
    }

    private static void EnsureOwned(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.OrdinalIgnoreCase))
        {
            // Do not reveal that another shopper's order exists.
            throw new PaymentOperationException(404, "ORDER_NOT_FOUND", "Order not found.");
        }
    }

    private static void EnsureAuthorizable(FinixAuthorizationResponse authorization)
    {
        if (authorization.IsVoid == true || authorization.State is not ("SUCCEEDED" or "PENDING"))
        {
            throw new PaymentOperationException(409, "AUTHORIZATION_NOT_CAPTURABLE",
                BuildFailureMessage("The card hold can no longer be captured", authorization.State,
                    authorization.FailureCode, authorization.FailureMessage) +
                " The order was not fulfilled; the shopper may need to pay again.");
        }
    }

    private static bool IsAuthorizedState(string? state) => state is "SUCCEEDED" or "PENDING";

    private async Task<FinixTransferResponse> WaitForTransferAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);

        while (transfer.State is not ("SUCCEEDED" or "FAILED" or "CANCELED" or "RETURNED")
               && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(1000, cancellationToken);
            transfer = await _finix.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<FinixTransferResponse?> TryReconcileRefundAsync(string captureTransferId, Refund refund,
        CancellationToken cancellationToken)
    {
        try
        {
            var reversals = await _finix.ListTransferReversalsAsync(captureTransferId, cancellationToken);
            return reversals
                .Where(r => r.Amount == refund.AmountCents && r.State != "FAILED")
                .Where(r => r.CreatedAt is null || r.CreatedAt >= refund.CreatedAt.AddMinutes(-2))
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is FinixApiException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Could not reconcile refund {RefundId} after a Finix error.", refund.Id);
            return null;
        }
    }

    private static string BuildFailureMessage(string prefix, string? state, string? code, string? message)
    {
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(state))
        {
            details.Add($"state {state}");
        }
        if (!string.IsNullOrWhiteSpace(code))
        {
            details.Add($"code {code}");
        }
        if (!string.IsNullOrWhiteSpace(message))
        {
            details.Add(message);
        }

        return details.Count == 0 ? $"{prefix}." : $"{prefix} ({string.Join("; ", details)}).";
    }

    private static async Task<T> WithOrderLockAsync<T>(int orderId, Func<Task<T>> action)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();
        try
        {
            return await action();
        }
        finally
        {
            gate.Release();
        }
    }

    private static OrderSummary ToOrderSummary(Order order, string currency)
    {
        var total = order.Total();
        return new OrderSummary
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = total,
            TotalCents = Money.ToCents(total),
            Currency = currency,
            Items = order.OrderItems.Select(ToOrderItemView).ToList()
        };
    }

    private static OrderItemView ToOrderItemView(OrderItem item) => new()
    {
        CatalogItemId = item.ItemOrdered.CatalogItemId,
        ProductName = item.ItemOrdered.ProductName,
        UnitPrice = item.UnitPrice,
        Units = item.Units
    };

    private static OrderView ToOrderView(Order order, Payment? payment)
    {
        var total = order.Total();
        return new OrderView
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Status = order.Status.ToString(),
            Total = total,
            TotalCents = Money.ToCents(total),
            Currency = payment?.Currency ?? string.Empty,
            Items = order.OrderItems.Select(ToOrderItemView).ToList(),
            Payment = payment == null ? null : ToPaymentView(payment)
        };
    }

    private static PaymentView ToPaymentView(Payment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        Currency = payment.Currency,
        AmountCents = payment.AmountCents,
        Amount = Money.FromCents(payment.AmountCents),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        CaptureTransferId = payment.CaptureTransferId,
        CaptureState = payment.CaptureState,
        CapturedAmountCents = payment.CapturedAmountCents,
        CapturedAmount = payment.CapturedAmountCents.HasValue ? Money.FromCents(payment.CapturedAmountCents.Value) : null,
        FeeCents = payment.FeeCents,
        Fee = payment.FeeCents.HasValue ? Money.FromCents(payment.FeeCents.Value) : null,
        NetCents = payment.NetCents,
        Net = payment.NetCents.HasValue ? Money.FromCents(payment.NetCents.Value) : null,
        RefundedAmountCents = payment.RefundedAmountCents,
        RefundedAmount = Money.FromCents(payment.RefundedAmountCents),
        RefundableAmountCents = payment.RefundableAmountCents,
        RefundableAmount = Money.FromCents(payment.RefundableAmountCents),
        CardBrand = payment.CardBrand,
        CardLast4 = payment.CardLast4,
        SavedPaymentMethodId = payment.SavedPaymentMethodId,
        FailureCode = payment.FailureCode,
        FailureMessage = payment.FailureMessage,
        Refunds = payment.Refunds.OrderBy(r => r.CreatedAt).Select(ToRefundView).ToList()
    };

    private static RefundView ToRefundView(Refund refund) => new()
    {
        RefundId = refund.Id,
        AmountCents = refund.AmountCents,
        Amount = Money.FromCents(refund.AmountCents),
        Status = refund.Status.ToString(),
        ProviderState = refund.ProviderState,
        FinixTransferId = refund.ProviderTransferId,
        IdempotencyKey = refund.IdempotencyKey,
        CreatedAt = refund.CreatedAt
    };
}
