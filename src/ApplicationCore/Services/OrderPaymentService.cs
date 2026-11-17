using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IRepository<PaymentRefund> _refundRepository;
    private readonly IRepository<SavedPaymentMethod> _savedPaymentMethodRepository;
    private readonly IRepository<FinixCustomer> _finixCustomerRepository;
    private readonly IRepository<CatalogItem> _catalogItemRepository;
    private readonly IFinixPaymentGateway _gateway;
    private readonly FinixOptions _options;
    private readonly IUriComposer _uriComposer;
    private readonly IAppLogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IRepository<PaymentRefund> refundRepository,
        IRepository<SavedPaymentMethod> savedPaymentMethodRepository,
        IRepository<FinixCustomer> finixCustomerRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IFinixPaymentGateway gateway,
        FinixOptions options,
        IUriComposer uriComposer,
        IAppLogger<OrderPaymentService> logger)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _refundRepository = refundRepository;
        _savedPaymentMethodRepository = savedPaymentMethodRepository;
        _finixCustomerRepository = finixCustomerRepository;
        _catalogItemRepository = catalogItemRepository;
        _gateway = gateway;
        _options = options;
        _uriComposer = uriComposer;
        _logger = logger;
    }

    public async Task<OrderView> CreateOrderAsync(
        string buyerId, CreateOrderCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentValidationException("The caller could not be identified.");
        }

        if (command.Items is null || command.Items.Count == 0)
        {
            throw new PaymentValidationException("An order must contain at least one catalog item.");
        }

        if (command.Items.Any(item => item.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order item must have a quantity greater than zero.");
        }

        var itemIds = command.Items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _catalogItemRepository.ListAsync(
            new CatalogItemsSpecification(itemIds), cancellationToken);

        var missing = itemIds.Except(catalogItems.Select(item => item.Id)).ToArray();
        if (missing.Length > 0)
        {
            throw new PaymentValidationException(
                $"Catalog item(s) {string.Join(", ", missing)} were not found.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var requested in command.Items)
        {
            var catalogItem = catalogItems.First(item => item.Id == requested.CatalogItemId);
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name,
                _uriComposer.ComposePicUri(pictureUri));
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, requested.Quantity));
        }

        var address = new Address(
            ValueOrDefault(command.Street, "123 Main St"),
            ValueOrDefault(command.City, "Seattle"),
            ValueOrDefault(command.State, "WA"),
            ValueOrDefault(command.Country, "USA"),
            ValueOrDefault(command.ZipCode, "98101"));

        var order = new Order(buyerId, address, orderItems);
        await _orderRepository.AddAsync(order, cancellationToken);

        var payment = new OrderPayment(order.Id, buyerId, _options.Currency, ToMinor(order.Total()));
        await _paymentRepository.AddAsync(payment, cancellationToken);

        return BuildOrderView(order, payment, Array.Empty<PaymentRefund>());
    }

    public async Task<OrderView> PayOrderAsync(
        string buyerId, int orderId, PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken);

            if (payment is not null)
            {
                if (payment.Status is PaymentStatus.Authorized or PaymentStatus.CapturePending
                    or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                {
                    return await BuildOrderViewAsync(order, payment, cancellationToken);
                }

                if (payment.Status == PaymentStatus.Canceled)
                {
                    throw new PaymentConflictException(
                        $"Order {orderId} was canceled; create a new order to take payment.");
                }

                if (payment.Status == PaymentStatus.AwaitingAuthorization &&
                    !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
                {
                    var pending = await _gateway.GetAuthorizationAsync(
                        payment.FinixAuthorizationId!, cancellationToken);
                    if (IsSucceeded(pending.State))
                    {
                        payment.MarkAuthorized(pending.Id, pending.State,
                            payment.FinixPaymentInstrumentId ?? string.Empty,
                            payment.SavedPaymentMethodId);
                        order.MarkAuthorized();
                        await _paymentRepository.UpdateAsync(payment, cancellationToken);
                        await _orderRepository.UpdateAsync(order, cancellationToken);
                        return await BuildOrderViewAsync(order, payment, cancellationToken);
                    }

                    if (IsFailed(pending.State))
                    {
                        payment.MarkAuthorizationFailed(pending.FailureCode, pending.FailureMessage);
                        await _paymentRepository.UpdateAsync(payment, cancellationToken);
                        throw new PaymentConflictException(BuildAuthorizationFailureMessage(pending));
                    }

                    throw new PaymentConflictException(
                        $"The card hold for order {orderId} is still pending at Finix; retry shortly.");
                }

                if (payment.Status == PaymentStatus.Failed)
                {
                    payment.BeginRetry(OrderPayment.NewIdempotencyKey(orderId));
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                }
            }
            else
            {
                payment = new OrderPayment(orderId, buyerId, _options.Currency, ToMinor(order.Total()));
                await _paymentRepository.AddAsync(payment, cancellationToken);
            }

            var amountMinor = ToMinor(order.Total());
            if (amountMinor <= 0)
            {
                throw new PaymentValidationException("The order total must be greater than zero.");
            }

            var instrumentId = await ResolvePaymentInstrumentAsync(
                buyerId, command, payment, cancellationToken);

            var authorization = await _gateway.CreateAuthorizationAsync(
                instrumentId, amountMinor, _options.Currency, orderId.ToString(),
                payment.AuthorizationIdempotencyKey,
                command.SecurityCode ?? command.Card?.SecurityCode, cancellationToken);

            if (IsSucceeded(authorization.State))
            {
                payment.MarkAuthorized(authorization.Id, authorization.State, instrumentId,
                    command.SavedPaymentMethodId);
                order.MarkAuthorized();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return await BuildOrderViewAsync(order, payment, cancellationToken);
            }

            if (IsFailed(authorization.State))
            {
                payment.MarkAuthorizationFailed(authorization.FailureCode, authorization.FailureMessage);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentConflictException(BuildAuthorizationFailureMessage(authorization));
            }

            payment.MarkAuthorizationPending(authorization.Id, authorization.State, instrumentId,
                command.SavedPaymentMethodId);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            throw new PaymentConflictException(
                $"The card hold for order {orderId} is pending at Finix (state {authorization.State}); retry shortly.");
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> FulfilOrderAsync(
        int orderId, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken)
                ?? throw new PaymentConflictException(
                    $"Order {orderId} has no payment to capture. The shopper must pay first.");

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return await BuildOrderViewAsync(order, payment, cancellationToken);
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} was canceled; its card hold was released and cannot be captured.");
            }

            if (payment.Status == PaymentStatus.CapturePending &&
                !string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                return await RefreshCaptureAsync(order, payment, cancellationToken);
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} is not authorized (payment state {payment.Status}); it cannot be fulfilled.");
            }

            if (string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has no Finix authorization to capture.");
            }

            var authorizationId = payment.FinixAuthorizationId!;
            var authorization = await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
            if (authorization.IsVoid || IsFailed(authorization.State) ||
                string.Equals(authorization.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
            {
                var reason = BuildCaptureFailureMessage(authorizationId, null, authorization);
                payment.MarkCaptureFailed("AUTHORIZATION_NOT_CAPTURABLE", reason);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentConflictException(reason);
            }

            FinixAuthorization capture;
            try
            {
                capture = await _gateway.CaptureAuthorizationAsync(
                    authorizationId, payment.AmountMinor, cancellationToken);
            }
            catch (FinixGatewayException exception)
            {
                var refreshed = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
                if (refreshed?.TransferId is not null)
                {
                    // The capture exists at Finix even though this response was lost.
                    capture = refreshed;
                }
                else
                {
                    var reason = BuildCaptureFailureMessage(authorizationId, exception, refreshed);
                    payment.MarkCaptureFailed(exception.ErrorCode ?? "CAPTURE_FAILED", reason);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentConflictException(reason);
                }
            }

            var transferId = capture.TransferId;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                var refreshed = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
                transferId = refreshed?.TransferId;
            }

            if (string.IsNullOrWhiteSpace(transferId))
            {
                const string reason = "Finix captured the authorization but did not return a capture " +
                                      "transfer id. Retry fulfilment to refresh the capture.";
                payment.MarkCaptureFailed("CAPTURE_TRANSFER_MISSING", reason);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentConflictException(reason);
            }

            var capturedMinor = capture.AmountMinor > 0 ? capture.AmountMinor : payment.AmountMinor;
            payment.MarkCaptured(transferId!, "PENDING", capturedMinor, null, null);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);

            try
            {
                var transfer = await WaitForTransferAsync(transferId!, TimeSpan.FromSeconds(45),
                    cancellationToken);

                if (IsFailed(transfer.State) ||
                    string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
                {
                    var reason = $"Finix capture {transfer.Id} ended in state {transfer.State}" +
                                 (string.IsNullOrWhiteSpace(transfer.FailureMessage)
                                     ? "."
                                     : $": {transfer.FailureMessage}");
                    payment.MarkCaptureFailed(transfer.FailureCode ?? "CAPTURE_FAILED", reason);
                    await _paymentRepository.UpdateAsync(payment, cancellationToken);
                    throw new PaymentConflictException(reason);
                }

                var fees = await TryGetFeesAsync(transfer.Id, cancellationToken);
                decimal? feeMinor = fees?.TotalFeeMinor;
                decimal? netMinor = feeMinor.HasValue ? capturedMinor - feeMinor.Value : null;

                payment.MarkCaptured(transfer.Id, transfer.State, capturedMinor, feeMinor, netMinor);
                order.MarkFulfilled();
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                await _orderRepository.UpdateAsync(order, cancellationToken);

                return await BuildOrderViewAsync(order, payment, cancellationToken);
            }
            catch (FinixGatewayException exception)
            {
                var reason = $"Capture {transferId} was submitted to Finix, but its settlement state " +
                             $"could not be confirmed: {exception.Message}. Retry fulfilment to refresh the capture.";
                payment.MarkCaptureFailed("CAPTURE_STATUS_UNKNOWN", reason);
                await _paymentRepository.UpdateAsync(payment, cancellationToken);
                throw new PaymentConflictException(reason);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> CancelOrderAsync(
        int orderId, CancellationToken cancellationToken = default)
    {
        var gate = LockFor(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderAsync(orderId, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken);

            if (payment is null)
            {
                order.MarkCanceled();
                await _orderRepository.UpdateAsync(order, cancellationToken);
                return BuildOrderView(order, null, Array.Empty<PaymentRefund>());
            }

            if (payment.Status == PaymentStatus.Canceled)
            {
                return await BuildOrderViewAsync(order, payment, cancellationToken);
            }

            if (payment.Status is PaymentStatus.CapturePending or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has already been captured; use the refunds endpoint instead of cancel.");
            }

            if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId) &&
                payment.Status is PaymentStatus.Authorized or PaymentStatus.AwaitingAuthorization)
            {
                var authorizationId = payment.FinixAuthorizationId!;
                try
                {
                    var voided = await _gateway.VoidAuthorizationAsync(authorizationId, cancellationToken);
                    payment.MarkCanceled(voided.State, voided.VoidState);
                }
                catch (FinixGatewayException exception)
                {
                    var refreshed = await TryGetAuthorizationAsync(authorizationId, cancellationToken);
                    if (refreshed is not null &&
                        (refreshed.IsVoid ||
                         string.Equals(refreshed.State, "CANCELED", StringComparison.OrdinalIgnoreCase)))
                    {
                        payment.MarkCanceled(refreshed.State, refreshed.VoidState);
                    }
                    else
                    {
                        throw new PaymentConflictException(
                            $"Finix could not release the card hold {authorizationId}: {exception.Message}. " +
                            "The order was not canceled; retry, or refund it if the payment was captured.");
                    }
                }
            }
            else
            {
                payment.MarkCanceled(null, null);
            }

            order.MarkCanceled();
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);
            return await BuildOrderViewAsync(order, payment, cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundView> RefundOrderAsync(
        string buyerId, int orderId, RefundOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            throw new PaymentValidationException("An idempotency key is required for refunds.");
        }

        if (command.IdempotencyKey.Length > 128)
        {
            throw new PaymentValidationException("The idempotency key must be 128 characters or fewer.");
        }

        var gate = LockFor(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await GetOrderForBuyerAsync(orderId, buyerId, cancellationToken);
            var payment = await GetPaymentAsync(orderId, cancellationToken);
            if (payment is null || payment.Status is not (PaymentStatus.CapturePending
                or PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has no captured payment to refund.");
            }

            var refunds = await GetRefundsAsync(payment.Id, cancellationToken);
            var existing = refunds.FirstOrDefault(refund =>
                string.Equals(refund.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                return BuildRefundView(existing);
            }

            if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} does not have a Finix capture transfer yet; retry after fulfilment settles.");
            }

            var capturedMinor = payment.CapturedAmountMinor ?? payment.AmountMinor;
            var alreadyRefunded = refunds.Where(refund => refund.CountsAgainstCapturedAmount)
                .Sum(refund => refund.AmountMinor);
            var remainingMinor = capturedMinor - alreadyRefunded;
            if (remainingMinor <= 0)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has already been fully refunded " +
                    $"({FormatMoney(alreadyRefunded, payment.Currency)} of " +
                    $"{FormatMoney(capturedMinor, payment.Currency)}).");
            }

            var amountMinor = command.Amount.HasValue ? ToMinor(command.Amount.Value) : remainingMinor;
            if (amountMinor <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (amountMinor > remainingMinor)
            {
                throw new PaymentConflictException(
                    $"The refund amount {FormatMoney(amountMinor, payment.Currency)} exceeds the remaining refundable " +
                    $"amount {FormatMoney(remainingMinor, payment.Currency)} of the captured " +
                    $"{FormatMoney(capturedMinor, payment.Currency)}.");
            }

            var captureTransferId = payment.FinixCaptureTransferId!;
            var transfer = await _gateway.GetTransferAsync(captureTransferId, cancellationToken);
            if (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase))
            {
                transfer = await WaitForTransferAsync(captureTransferId, TimeSpan.FromSeconds(30),
                    cancellationToken);
            }

            if (!IsSucceeded(transfer.State))
            {
                throw new PaymentConflictException(
                    $"Finix capture {captureTransferId} is in state {transfer.State}; refunds can only be " +
                    "issued after the capture succeeds. Retry once it settles.");
            }

            var finixIdempotencyKey = BuildRefundIdempotencyKey(payment.Id, command.IdempotencyKey);
            var result = await _gateway.CreateRefundAsync(captureTransferId, amountMinor,
                finixIdempotencyKey, cancellationToken);

            var refund = new PaymentRefund(payment.Id, amountMinor, result.Id, result.State,
                command.IdempotencyKey, result.FailureCode, result.FailureMessage);
            await _refundRepository.AddAsync(refund, cancellationToken);

            var activeRefunded = refunds.Where(item => item.CountsAgainstCapturedAmount)
                .Sum(item => item.AmountMinor);
            if (refund.CountsAgainstCapturedAmount)
            {
                activeRefunded += amountMinor;
            }

            payment.ApplyRefundTotals(activeRefunded);
            if (activeRefunded > 0)
            {
                if (payment.Status == PaymentStatus.Refunded)
                {
                    order.MarkRefunded();
                }
                else
                {
                    order.MarkPartiallyRefunded();
                }
            }

            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            await _orderRepository.UpdateAsync(order, cancellationToken);

            return BuildRefundView(refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderView>> GetMyOrdersAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var orders = await _orderRepository.ListAsync(
            new CustomerOrdersWithItemsSpecification(buyerId), cancellationToken);
        var payments = await _paymentRepository.ListAsync(
            new OrderPaymentsByBuyerSpec(buyerId), cancellationToken);

        var paymentByOrder = payments.ToDictionary(payment => payment.OrderId);
        var paymentIds = payments.Select(payment => payment.Id).ToHashSet();
        var allRefunds = await _refundRepository.ListAsync(
            new AllPaymentRefundsSpec(), cancellationToken);
        var refundsByPayment = allRefunds
            .Where(refund => paymentIds.Contains(refund.OrderPaymentId))
            .GroupBy(refund => refund.OrderPaymentId)
            .ToDictionary(group => group.Key, group => group.ToList());
        var views = new List<OrderView>();

        foreach (var order in orders.OrderByDescending(order => order.OrderDate))
        {
            paymentByOrder.TryGetValue(order.Id, out var payment);
            var refunds = payment is not null && refundsByPayment.TryGetValue(payment.Id, out var found)
                ? found
                : Array.Empty<PaymentRefund>().ToList();
            views.Add(BuildOrderView(order, payment, refunds));
        }

        return views;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (from > to)
        {
            throw new PaymentValidationException("The reconciliation 'from' value must be before 'to'.");
        }

        var finixTransfers = await _gateway.ListTransfersAsync(from, to, cancellationToken);
        var finixAuthorizations = await _gateway.ListAuthorizationsAsync(from, to, cancellationToken);
        var payments = await _paymentRepository.ListAsync(new AllOrderPaymentsSpec(), cancellationToken);
        var refunds = await _refundRepository.ListAsync(new AllPaymentRefundsSpec(), cancellationToken);

        var captureByTransfer = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
            .GroupBy(payment => payment.FinixCaptureTransferId!)
            .ToDictionary(group => group.Key, group => group.First());
        var refundByTransfer = refunds
            .Where(refund => !string.IsNullOrWhiteSpace(refund.FinixRefundId))
            .GroupBy(refund => refund.FinixRefundId!)
            .ToDictionary(group => group.Key, group => group.First());
        var paymentByAuthorization = payments
            .Where(payment => !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            .GroupBy(payment => payment.FinixAuthorizationId!)
            .ToDictionary(group => group.Key, group => group.First());
        var finixIds = finixTransfers.Select(transfer => transfer.Id)
            .ToHashSet(StringComparer.Ordinal);
        var finixAuthorizationIds = finixAuthorizations.Select(authorization => authorization.Id)
            .ToHashSet(StringComparer.Ordinal);

        var matched = new List<ReconciliationItem>();
        var finixOnly = new List<ReconciliationItem>();
        var eshopOnly = new List<ReconciliationItem>();

        foreach (var transfer in finixTransfers)
        {
            if (captureByTransfer.TryGetValue(transfer.Id, out var capturedPayment))
            {
                matched.Add(ToReconciliationItem(transfer, capturedPayment.OrderId, null, "capture",
                    "Finix capture matched an eShop payment."));
            }
            else if (refundByTransfer.TryGetValue(transfer.Id, out var matchedRefund))
            {
                var payment = payments.FirstOrDefault(item => item.Id == matchedRefund.OrderPaymentId);
                matched.Add(ToReconciliationItem(transfer, payment?.OrderId, matchedRefund.Id, "refund",
                    "Finix refund matched an eShop refund."));
            }
            else
            {
                int? orderId = int.TryParse(transfer.OrderReference, out var parsed) ? parsed : null;
                finixOnly.Add(ToReconciliationItem(transfer, orderId, null,
                    string.Equals(transfer.Type, "REVERSAL", StringComparison.OrdinalIgnoreCase)
                        ? "refund"
                        : "capture",
                    "Finix has this transaction but eShop has no matching payment or refund."));
            }
        }

        foreach (var authorization in finixAuthorizations)
        {
            if (paymentByAuthorization.TryGetValue(authorization.Id, out var authorizedPayment))
            {
                matched.Add(new ReconciliationItem(
                    authorization.Id,
                    authorizedPayment.OrderId,
                    null,
                    "authorization",
                    authorization.AmountMinor,
                    FromMinor(authorization.AmountMinor),
                    authorization.Currency,
                    authorization.State,
                    authorization.CreatedAt,
                    authorizedPayment.OrderId.ToString(),
                    "Finix authorization matched an eShop payment."));
            }
            else
            {
                finixOnly.Add(new ReconciliationItem(
                    authorization.Id,
                    null,
                    null,
                    "authorization",
                    authorization.AmountMinor,
                    FromMinor(authorization.AmountMinor),
                    authorization.Currency,
                    authorization.State,
                    authorization.CreatedAt,
                    null,
                    "Finix has this authorization but eShop has no matching payment."));
            }
        }

        foreach (var payment in payments)
        {
            if (string.IsNullOrWhiteSpace(payment.FinixAuthorizationId) ||
                payment.CreatedAt < from || payment.CreatedAt > to ||
                finixAuthorizationIds.Contains(payment.FinixAuthorizationId!))
            {
                continue;
            }

            eshopOnly.Add(new ReconciliationItem(
                payment.FinixAuthorizationId,
                payment.OrderId,
                null,
                "authorization",
                payment.AmountMinor,
                FromMinor(payment.AmountMinor),
                payment.Currency,
                payment.AuthorizationState ?? payment.Status.ToString(),
                payment.CreatedAt,
                payment.OrderId.ToString(),
                "eShop recorded this authorization but Finix did not return it in the date range."));
        }

        foreach (var payment in payments)
        {
            if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId) ||
                payment.CapturedAt is null ||
                payment.CapturedAt < from || payment.CapturedAt > to ||
                finixIds.Contains(payment.FinixCaptureTransferId!))
            {
                continue;
            }

            eshopOnly.Add(new ReconciliationItem(
                payment.FinixCaptureTransferId,
                payment.OrderId,
                null,
                "capture",
                payment.CapturedAmountMinor ?? payment.AmountMinor,
                FromMinor(payment.CapturedAmountMinor ?? payment.AmountMinor),
                payment.Currency,
                payment.CaptureState ?? payment.Status.ToString(),
                payment.CapturedAt,
                payment.OrderId.ToString(),
                "eShop recorded this capture but Finix did not return it in the date range."));
        }

        foreach (var refund in refunds)
        {
            if (string.IsNullOrWhiteSpace(refund.FinixRefundId) ||
                refund.CreatedAt < from || refund.CreatedAt > to ||
                finixIds.Contains(refund.FinixRefundId!))
            {
                continue;
            }

            var payment = payments.FirstOrDefault(item => item.Id == refund.OrderPaymentId);
            eshopOnly.Add(new ReconciliationItem(
                refund.FinixRefundId,
                payment?.OrderId,
                refund.Id,
                "refund",
                refund.AmountMinor,
                FromMinor(refund.AmountMinor),
                payment?.Currency ?? _options.Currency,
                refund.Status,
                refund.CreatedAt,
                payment?.OrderId.ToString(),
                "eShop recorded this refund but Finix did not return it in the date range."));
        }

        return new ReconciliationReport(from, to, matched, finixOnly, eshopOnly,
            finixTransfers.Count + finixAuthorizations.Count, matched.Count + eshopOnly.Count);
    }

    public async Task<SavedPaymentMethodView> SavePaymentMethodAsync(
        string buyerId, SaveCardCommand command, CancellationToken cancellationToken = default)
    {
        ValidateCard(command.Card);
        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var instrument = await _gateway.CreatePaymentInstrumentAsync(
            identityId, command.Card, cancellationToken);

        var saved = new SavedPaymentMethod(buyerId, instrument.Id, instrument.Brand,
            instrument.Last4, instrument.ExpirationMonth, instrument.ExpirationYear,
            command.Card.Name);
        await _savedPaymentMethodRepository.AddAsync(saved, cancellationToken);

        return BuildSavedPaymentMethodView(saved);
    }

    public async Task<IReadOnlyList<SavedPaymentMethodView>> GetPaymentMethodsAsync(
        string buyerId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedPaymentMethodRepository.ListAsync(
            new SavedPaymentMethodsByBuyerSpec(buyerId), cancellationToken);
        return saved.Select(BuildSavedPaymentMethodView).ToList();
    }

    public async Task DeletePaymentMethodAsync(
        string buyerId, int paymentMethodId, CancellationToken cancellationToken = default)
    {
        var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
            new SavedPaymentMethodByIdAndBuyerSpec(paymentMethodId, buyerId), cancellationToken);

        if (saved is null || saved.IsDeleted)
        {
            throw new PaymentNotFoundException(
                $"Saved payment method {paymentMethodId} was not found.");
        }

        await _gateway.DisablePaymentInstrumentAsync(saved.FinixPaymentInstrumentId, cancellationToken);
        saved.MarkDeleted();
        await _savedPaymentMethodRepository.UpdateAsync(saved, cancellationToken);
    }

    private async Task<string> ResolvePaymentInstrumentAsync(
        string buyerId, PayOrderCommand command, OrderPayment payment,
        CancellationToken cancellationToken)
    {
        if (command.SavedPaymentMethodId.HasValue)
        {
            var saved = await _savedPaymentMethodRepository.FirstOrDefaultAsync(
                new SavedPaymentMethodByIdAndBuyerSpec(command.SavedPaymentMethodId.Value, buyerId),
                cancellationToken);

            if (saved is null || saved.IsDeleted)
            {
                throw new PaymentNotFoundException(
                    $"Saved payment method {command.SavedPaymentMethodId.Value} was not found.");
            }

            return saved.FinixPaymentInstrumentId;
        }

        if (command.Card is null)
        {
            throw new PaymentValidationException(
                "Provide card details or a savedPaymentMethodId to pay for this order.");
        }

        ValidateCard(command.Card);
        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var instrument = await _gateway.CreatePaymentInstrumentAsync(
            identityId, command.Card, cancellationToken);
        return instrument.Id;
    }

    private async Task<string> GetOrCreateIdentityAsync(
        string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _finixCustomerRepository.FirstOrDefaultAsync(
            new FinixCustomerByBuyerSpec(buyerId), cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var gate = CustomerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _finixCustomerRepository.FirstOrDefaultAsync(
                new FinixCustomerByBuyerSpec(buyerId), cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            var identityId = await _gateway.CreateIdentityAsync(cancellationToken);
            var customer = new FinixCustomer(buyerId, identityId);
            await _finixCustomerRepository.AddAsync(customer, cancellationToken);
            return identityId;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<OrderView> RefreshCaptureAsync(
        Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.FinixCaptureTransferId))
        {
            return await BuildOrderViewAsync(order, payment, cancellationToken);
        }

        var transfer = await _gateway.GetTransferAsync(
            payment.FinixCaptureTransferId!, cancellationToken);

        if (IsFailed(transfer.State) ||
            string.Equals(transfer.State, "CANCELED", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(transfer.State, "RETURNED", StringComparison.OrdinalIgnoreCase))
        {
            var reason = $"Finix capture {transfer.Id} ended in state {transfer.State}" +
                         (string.IsNullOrWhiteSpace(transfer.FailureMessage)
                             ? "."
                             : $": {transfer.FailureMessage}");
            payment.MarkCaptureFailed(transfer.FailureCode ?? "CAPTURE_FAILED", reason);
            await _paymentRepository.UpdateAsync(payment, cancellationToken);
            return await BuildOrderViewAsync(order, payment, cancellationToken);
        }

        var fees = await TryGetFeesAsync(transfer.Id, cancellationToken);
        var capturedMinor = payment.CapturedAmountMinor ?? payment.AmountMinor;
        decimal? feeMinor = fees?.TotalFeeMinor ?? payment.FinixFeeMinor;
        decimal? netMinor = feeMinor.HasValue ? capturedMinor - feeMinor.Value : payment.NetAmountMinor;
        payment.MarkCaptured(transfer.Id, transfer.State, capturedMinor, feeMinor, netMinor);
        await _paymentRepository.UpdateAsync(payment, cancellationToken);
        return await BuildOrderViewAsync(order, payment, cancellationToken);
    }

    private async Task<OrderView> BuildOrderViewAsync(
        Order order, OrderPayment payment, CancellationToken cancellationToken)
    {
        var refunds = await GetRefundsAsync(payment.Id, cancellationToken);
        return BuildOrderView(order, payment, refunds);
    }

    private OrderView BuildOrderView(
        Order order, OrderPayment? payment, IReadOnlyList<PaymentRefund> refunds)
    {
        var items = order.OrderItems.Select(item => new OrderItemView(
            item.ItemOrdered.CatalogItemId,
            item.ItemOrdered.ProductName,
            item.UnitPrice,
            item.Units,
            item.UnitPrice * item.Units)).ToList();

        var total = order.Total();
        return new OrderView(
            order.Id,
            order.Status.ToString(),
            total,
            ToMinor(total),
            payment?.Currency ?? _options.Currency,
            order.OrderDate,
            items,
            payment is null ? null : BuildPaymentView(payment, refunds));
    }

    private static PaymentView BuildPaymentView(
        OrderPayment payment, IReadOnlyList<PaymentRefund> refunds)
    {
        var feeMinor = payment.FinixFeeMinor;
        var netMinor = payment.NetAmountMinor;
        return new PaymentView(
            payment.Status.ToString(),
            payment.AmountMinor,
            FromMinor(payment.AmountMinor),
            payment.Currency,
            payment.FinixAuthorizationId,
            payment.AuthorizationState,
            payment.VoidState,
            payment.FinixCaptureTransferId,
            payment.CaptureState,
            payment.CapturedAmountMinor,
            payment.CapturedAmountMinor.HasValue ? FromMinor(payment.CapturedAmountMinor.Value) : null,
            feeMinor,
            feeMinor.HasValue ? FromMinor(feeMinor.Value) : null,
            netMinor,
            netMinor.HasValue ? FromMinor(netMinor.Value) : null,
            payment.RefundedAmountMinor,
            FromMinor(payment.RefundedAmountMinor),
            payment.FailureCode,
            payment.FailureMessage,
            refunds.Select(BuildRefundView).ToList());
    }

    private static RefundView BuildRefundView(PaymentRefund refund) =>
        new(
            refund.Id,
            refund.FinixRefundId,
            FromMinor(refund.AmountMinor),
            refund.AmountMinor,
            refund.Status,
            refund.IdempotencyKey,
            refund.FailureCode,
            refund.FailureMessage,
            refund.CreatedAt);

    private static SavedPaymentMethodView BuildSavedPaymentMethodView(SavedPaymentMethod saved) =>
        new(
            saved.Id,
            saved.Brand,
            saved.Last4,
            saved.ExpirationMonth,
            saved.ExpirationYear,
            saved.Name,
            saved.CreatedAt);

    private static ReconciliationItem ToReconciliationItem(
        FinixTransfer transfer, int? orderId, int? refundId, string kind, string note) =>
        new(
            transfer.Id,
            orderId,
            refundId,
            kind,
            transfer.AmountMinor,
            FromMinor(transfer.AmountMinor),
            transfer.Currency,
            transfer.State,
            transfer.CreatedAt,
            transfer.OrderReference,
            note);

    private async Task<Order> GetOrderForBuyerAsync(
        int orderId, string buyerId, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(
            new OrderWithItemsByIdSpec(orderId), cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Order> GetOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId), cancellationToken)
        ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _paymentRepository.FirstOrDefaultAsync(
            new OrderPaymentByOrderIdSpec(orderId), cancellationToken);

    private async Task<IReadOnlyList<PaymentRefund>> GetRefundsAsync(
        int paymentId, CancellationToken cancellationToken) =>
        await _refundRepository.ListAsync(
            new PaymentRefundByPaymentIdSpec(paymentId), cancellationToken);

    private async Task<FinixTransfer> WaitForTransferAsync(
        string transferId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
        while (string.Equals(transfer.State, "PENDING", StringComparison.OrdinalIgnoreCase) &&
               DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            transfer = await _gateway.GetTransferAsync(transferId, cancellationToken);
        }

        return transfer;
    }

    private async Task<FinixFeeSummary?> TryGetFeesAsync(
        string transferId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await _gateway.GetTransferFeesAsync(transferId, cancellationToken);
            }
            catch (FinixGatewayException exception)
            {
                _logger.LogWarning("Could not read Finix fees for transfer {TransferId}: {Message}",
                    transferId, exception.Message);
                if (attempt < 2)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                }
            }
        }

        return null;
    }

    private async Task<FinixAuthorization?> TryGetAuthorizationAsync(
        string authorizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _gateway.GetAuthorizationAsync(authorizationId, cancellationToken);
        }
        catch (FinixGatewayException)
        {
            return null;
        }
    }

    private static string BuildAuthorizationFailureMessage(FinixAuthorization authorization)
    {
        var detail = string.IsNullOrWhiteSpace(authorization.FailureMessage)
            ? "the issuer declined the card"
            : authorization.FailureMessage;
        var code = string.IsNullOrWhiteSpace(authorization.FailureCode)
            ? string.Empty
            : $" ({authorization.FailureCode})";
        return $"Finix could not authorize the card{code}: {detail}";
    }

    private static string BuildCaptureFailureMessage(
        string authorizationId, FinixGatewayException? exception, FinixAuthorization? authorization)
    {
        if (authorization?.IsVoid == true ||
            string.Equals(authorization?.State, "CANCELED", StringComparison.OrdinalIgnoreCase))
        {
            return $"The card hold {authorizationId} was voided and can no longer be captured. " +
                   "Cancel the order or ask the shopper to pay again.";
        }

        if (authorization is not null && IsFailed(authorization.State))
        {
            var detail = string.IsNullOrWhiteSpace(authorization.FailureMessage)
                ? "the authorization failed"
                : authorization.FailureMessage;
            return $"The card hold {authorizationId} failed ({detail}) and can no longer be captured. " +
                   "Cancel the order or ask the shopper to pay again.";
        }

        if (exception is not null &&
            exception.Message.Contains("expired", StringComparison.OrdinalIgnoreCase))
        {
            return $"The card hold {authorizationId} has expired and can no longer be captured. " +
                   "Cancel the order or ask the shopper to pay again.";
        }

        var reason = exception?.Message ?? "Finix reported the authorization cannot be captured";
        return $"Finix could not capture the card hold {authorizationId}: {reason}. " +
               "The order remains authorized; void the hold or ask the shopper to pay again.";
    }

    private static bool IsSucceeded(string? state) =>
        string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase);

    private static bool IsFailed(string? state) =>
        string.Equals(state, "FAILED", StringComparison.OrdinalIgnoreCase);

    private static string BuildRefundIdempotencyKey(int paymentId, string callerKey)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(callerKey)))[..40].ToLowerInvariant();
        return $"refund-{paymentId}-{hash}";
    }

    private static void ValidateCard(CardDetails? card)
    {
        if (card is null)
        {
            throw new PaymentValidationException("Card details are required.");
        }

        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (digits.Length is < 13 or > 19)
        {
            throw new PaymentValidationException("The card number is not valid.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (card.ExpirationYear < now.Year ||
            (card.ExpirationYear == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentValidationException("The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.SecurityCode.Length is < 3 or > 4 ||
            !card.SecurityCode.All(char.IsDigit))
        {
            throw new PaymentValidationException("The card security code must be 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }
    }

    private static long ToMinor(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal FromMinor(long amountMinor) => amountMinor / 100m;

    private static decimal FromMinor(decimal amountMinor) => amountMinor / 100m;

    private static string FormatMoney(long amountMinor, string currency) =>
        $"{FromMinor(amountMinor).ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    private static string ValueOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static SemaphoreSlim LockFor(int orderId) =>
        OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
}
