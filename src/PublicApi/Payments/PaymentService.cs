using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentService
{
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly PayPalSettings _settings;
    private readonly OrderOperationLock _orderLocks;

    public PaymentService(CatalogContext context, IPayPalClient payPal,
        IOptions<PayPalSettings> settings, OrderOperationLock orderLocks)
    {
        _context = context;
        _payPal = payPal;
        _settings = settings.Value;
        _orderLocks = orderLocks;
    }

    public async Task<OrderCreatedResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw new ArgumentException("At least one catalog item is required.");
        if (request.ShippingAddress is null)
            throw new ArgumentException("A shipping address is required.");
        if (string.IsNullOrWhiteSpace(request.ShippingAddress.Street) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.City) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.Country) ||
            string.IsNullOrWhiteSpace(request.ShippingAddress.ZipCode))
            throw new ArgumentException("Shipping street, city, country, and zipCode are required.");
        if (request.Items.Any(item => item.CatalogItemId <= 0 || item.Quantity is <= 0 or > 1000))
            throw new ArgumentException("Catalog item IDs must be positive and quantities must be between 1 and 1000.");

        var requestedItems = request.Items.GroupBy(item => item.CatalogItemId)
            .Select(group => new OrderLineRequest(group.Key, group.Sum(item => item.Quantity)))
            .ToList();
        if (requestedItems.Any(item => item.Quantity > 1000))
            throw new ArgumentException("The combined quantity for an item cannot exceed 1000.");

        var ids = requestedItems.Select(item => item.CatalogItemId).ToList();
        var catalogItems = await _context.CatalogItems.AsNoTracking()
            .Where(item => ids.Contains(item.Id)).ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Count)
        {
            var missing = ids.Except(catalogItems.Select(item => item.Id));
            throw new KeyNotFoundException($"Catalog items were not found: {string.Join(", ", missing)}.");
        }

        var lines = requestedItems.Select(requested =>
        {
            var catalog = catalogItems.Single(item => item.Id == requested.CatalogItemId);
            return new OrderItem(new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri),
                catalog.Price, requested.Quantity);
        }).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            lines);
        order.InitializePayment(_settings.Currency);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);

        return new OrderCreatedResponse(order.Id, order.PaymentStatus.ToString(), order.Total(),
            order.PaymentCurrency!);
    }

    public async Task<OrderActionResponse> PayAsync(int orderId, string buyerId,
        PayOrderRequest request, CancellationToken cancellationToken)
    {
        using var operationLock = await _orderLocks.AcquireAsync(orderId, cancellationToken);
        var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);

        if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending)
        {
            var pending = await _payPal.GetAuthorizationAsync(order.PaypalAuthorizationId!, cancellationToken);
            EnsureAmountAndCurrency(order, pending.Amount, pending.Currency);
            EnsureAuthorizationStatus(pending.Status);
            order.RecordAuthorization(pending.Id, pending.Status, pending.Amount, pending.CreatedAt,
                pending.ExpiresAt);
            await _context.SaveChangesAsync(cancellationToken);
            if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending)
                throw new PaymentConflictException("PayPal is still reviewing this authorization; retry later.");
            return ActionResponse(order);
        }

        if (order.PaymentStatus == OrderPaymentStatus.Authorized) return ActionResponse(order);
        if (order.PaymentStatus is OrderPaymentStatus.Captured or OrderPaymentStatus.CapturePending or
            OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            return ActionResponse(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            throw new PaymentConflictException("A cancelled order cannot be paid.");
        if (order.PaymentStatus is not (OrderPaymentStatus.AwaitingPayment or
            OrderPaymentStatus.AuthorizationRenewalRequired))
            throw new PaymentConflictException($"Order {orderId} cannot be paid from state {order.PaymentStatus}.");

        var hasCard = request.Card is not null;
        var hasSavedMethod = request.PaymentMethodId.HasValue;
        if (hasCard == hasSavedMethod)
            throw new ArgumentException("Provide exactly one of card or paymentMethodId.");

        string? vaultId = null;
        if (request.PaymentMethodId is int paymentMethodId)
        {
            var method = await _context.PaymentMethods.SingleOrDefaultAsync(method =>
                method.Id == paymentMethodId && method.BuyerId == buyerId && method.DeletedAt == null,
                cancellationToken);
            if (method is null) throw new KeyNotFoundException("The saved payment method was not found.");
            vaultId = method.PaypalPaymentTokenId;
        }

        var version = order.AuthorizationAttempt + 1;
        if (order.PaypalOrderId is null)
        {
            var paypalOrder = await _payPal.CreateOrderAsync(order.Id, order.PaymentReference!, order.Total(),
                order.PaymentCurrency!, $"eshop-{order.PaymentReference}-order-{version}", cancellationToken);
            order.RecordPaypalOrder(paypalOrder.Id, paypalOrder.Status);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _payPal.AuthorizeOrderAsync(order.PaypalOrderId!,
            request.Card is null ? null : ToCard(request.Card), vaultId,
            $"eshop-{order.PaymentReference}-authorize-{version}", cancellationToken);
        EnsureAmountAndCurrency(order, authorization.Amount, authorization.Currency);
        EnsureAuthorizationStatus(authorization.Status);
        order.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreatedAt, authorization.ExpiresAt);
        await _context.SaveChangesAsync(cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending)
            throw new PaymentConflictException("PayPal is reviewing this authorization; no second authorization was created. Retry later.");
        return ActionResponse(order);
    }

    public async Task<OrderActionResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await _orderLocks.AcquireAsync(orderId, cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled) return ActionResponse(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled)
            throw new PaymentConflictException("A cancelled order cannot be fulfilled.");

        if (order.PaymentStatus == OrderPaymentStatus.CapturePending)
        {
            var existingCapture = await _payPal.GetCaptureAsync(order.PaypalCaptureId!, cancellationToken);
            EnsureAmountAndCurrency(order, existingCapture.Amount, existingCapture.Currency);
            EnsureCaptureStatus(existingCapture.Status);
            order.RecordCapture(existingCapture.Id, existingCapture.Status, existingCapture.Amount,
                existingCapture.Fee, existingCapture.NetAmount, existingCapture.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            if (order.PaymentStatus == OrderPaymentStatus.CapturePending)
                throw new PaymentConflictException("PayPal still reports the capture as pending; retry fulfilment later.");
            return ActionResponse(order);
        }

        if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending)
        {
            var pending = await _payPal.GetAuthorizationAsync(order.PaypalAuthorizationId!, cancellationToken);
            EnsureAmountAndCurrency(order, pending.Amount, pending.Currency);
            EnsureAuthorizationStatus(pending.Status);
            order.RecordAuthorization(pending.Id, pending.Status, pending.Amount, pending.CreatedAt,
                pending.ExpiresAt);
            await _context.SaveChangesAsync(cancellationToken);
        }
        if (order.PaymentStatus != OrderPaymentStatus.Authorized)
            throw new PaymentConflictException("The order must have an active authorization before fulfilment.");

        var authorization = await _payPal.GetAuthorizationAsync(order.PaypalAuthorizationId!,
            cancellationToken);
        EnsureAmountAndCurrency(order, authorization.Amount, authorization.Currency);
        if (authorization.Status == "CAPTURED")
        {
            var recoveredCapture = authorization.RelatedCaptureId is not null
                ? await _payPal.GetCaptureAsync(authorization.RelatedCaptureId, cancellationToken)
                : await _payPal.CaptureAsync(authorization.Id, order.Id, order.PaymentReference!,
                    order.Total(), order.PaymentCurrency!,
                    $"eshop-{order.PaymentReference}-capture-{authorization.Id}", cancellationToken);
            EnsureAmountAndCurrency(order, recoveredCapture.Amount, recoveredCapture.Currency);
            EnsureCaptureStatus(recoveredCapture.Status);
            order.RecordCapture(recoveredCapture.Id, recoveredCapture.Status, recoveredCapture.Amount,
                recoveredCapture.Fee, recoveredCapture.NetAmount, recoveredCapture.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            if (order.PaymentStatus == OrderPaymentStatus.CapturePending)
                throw new PaymentConflictException("PayPal still reports the recovered capture as pending; retry fulfilment later.");
            return ActionResponse(order);
        }
        if (authorization.Status != "CREATED")
        {
            order.RequireNewAuthorization(authorization.Status);
            await _context.SaveChangesAsync(cancellationToken);
            throw RenewalRequired(orderId, authorization.Status);
        }

        var now = DateTimeOffset.UtcNow;
        if (now >= authorization.CreatedAt.AddDays(29) || now >= authorization.ExpiresAt)
        {
            order.RequireNewAuthorization("EXPIRED");
            await _context.SaveChangesAsync(cancellationToken);
            throw RenewalRequired(orderId, "EXPIRED");
        }

        if (now >= authorization.CreatedAt.AddDays(3))
        {
            try
            {
                authorization = await _payPal.ReauthorizeAsync(authorization.Id, order.Total(),
                    order.PaymentCurrency!, $"eshop-{order.PaymentReference}-reauthorize-{authorization.Id}",
                    cancellationToken);
                EnsureAmountAndCurrency(order, authorization.Amount, authorization.Currency);
                EnsureAuthorizationStatus(authorization.Status);
                order.RecordReauthorization(authorization.Id, authorization.Status,
                    authorization.Amount, authorization.CreatedAt, authorization.ExpiresAt);
                await _context.SaveChangesAsync(cancellationToken);
                if (order.PaymentStatus == OrderPaymentStatus.AuthorizationPending)
                    throw new PaymentConflictException(
                        "PayPal is reviewing the renewed authorization; retry fulfilment later.");
            }
            catch (PayPalException exception) when (exception.StatusCode is HttpStatusCode.UnprocessableEntity
                                                    or HttpStatusCode.BadRequest
                                                    or HttpStatusCode.Conflict)
            {
                order.RequireNewAuthorization("REAUTHORIZATION_FAILED");
                await _context.SaveChangesAsync(cancellationToken);
                throw RenewalRequired(orderId, "REAUTHORIZATION_FAILED");
            }
        }

        var capture = await _payPal.CaptureAsync(authorization.Id, order.Id, order.PaymentReference!, order.Total(),
            order.PaymentCurrency!, $"eshop-{order.PaymentReference}-capture-{authorization.Id}", cancellationToken);
        EnsureAmountAndCurrency(order, capture.Amount, capture.Currency);
        EnsureCaptureStatus(capture.Status);
        order.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee,
            capture.NetAmount, capture.CreatedAt);
        await _context.SaveChangesAsync(cancellationToken);
        if (order.PaymentStatus == OrderPaymentStatus.CapturePending)
            throw new PaymentConflictException("PayPal accepted the capture but reports it as pending; retry fulfilment later.");
        return ActionResponse(order);
    }

    public async Task<OrderActionResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        using var operationLock = await _orderLocks.AcquireAsync(orderId, cancellationToken);
        var order = await OrderAsync(orderId, cancellationToken);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled) return ActionResponse(order);
        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled ||
            order.PaymentStatus is OrderPaymentStatus.CapturePending or OrderPaymentStatus.Captured or
                OrderPaymentStatus.PartiallyRefunded or OrderPaymentStatus.Refunded)
            throw new PaymentConflictException("A capture has started or completed; refund the order instead of cancelling it.");

        var paypalStatus = "NOT_AUTHORIZED";
        if (order.PaypalAuthorizationId is not null && order.PaymentStatus is
            (OrderPaymentStatus.Authorized or OrderPaymentStatus.AuthorizationPending or
             OrderPaymentStatus.AuthorizationRenewalRequired))
        {
            var authorization = await _payPal.GetAuthorizationAsync(order.PaypalAuthorizationId,
                cancellationToken);
            if (authorization.Status != "VOIDED")
            {
                if (authorization.Status is not ("CREATED" or "PENDING"))
                    throw new PaymentConflictException(
                        $"PayPal authorization status '{authorization.Status}' cannot be cancelled.");
                await _payPal.VoidAsync(order.PaypalAuthorizationId,
                    $"eshop-{order.PaymentReference}-void-{order.PaypalAuthorizationId}", cancellationToken);
            }
            paypalStatus = "VOIDED";
        }
        order.Cancel(paypalStatus, DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
        return ActionResponse(order);
    }

    public async Task<RefundCreatedResponse> RefundAsync(int orderId, string buyerId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 108)
            throw new ArgumentException("idempotencyKey must contain between 1 and 108 characters.");

        using var operationLock = await _orderLocks.AcquireAsync(orderId, cancellationToken);
        var order = await OwnedOrderAsync(orderId, buyerId, cancellationToken);
        var existing = order.Refunds.SingleOrDefault(refund =>
            refund.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null) return RefundResponse(order, existing);
        if (order.FulfilmentStatus != OrderFulfilmentStatus.Fulfilled ||
            order.PaypalCaptureId is null || order.CapturedAmount is null)
            throw new PaymentConflictException("Only a fulfilled, captured order can be refunded.");

        var remaining = order.CapturedAmount.Value - order.RefundedAmount;
        var amount = request.Amount ?? remaining;
        if (amount <= 0 || decimal.Round(amount, 2) != amount || amount > remaining)
            throw new ArgumentException($"Refund amount must be positive, have at most two decimal places, and not exceed {remaining:0.00}.");

        var paypalRefund = await _payPal.RefundAsync(order.PaypalCaptureId, amount,
            order.PaymentCurrency!, PayPalRefundRequestId(request.IdempotencyKey), cancellationToken);
        EnsureAmountAndCurrency(order, paypalRefund.Amount, paypalRefund.Currency, amount);
        if (paypalRefund.Status is not ("COMPLETED" or "PENDING"))
            throw new PaymentConflictException(
                $"PayPal returned refund status '{paypalRefund.Status}', so the refund was not recorded as successful.");
        var refund = order.RecordRefund(paypalRefund.Id, request.IdempotencyKey, paypalRefund.Amount,
            paypalRefund.Status, paypalRefund.CreatedAt);
        await _context.SaveChangesAsync(cancellationToken);
        return RefundResponse(order, refund);
    }

    public async Task<IReadOnlyList<MyOrderResponse>> MyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await _context.Orders.AsNoTracking().Where(order => order.BuyerId == buyerId)
            .Include(order => order.OrderItems).Include(order => order.Refunds)
            .OrderByDescending(order => order.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(MyOrderResponse).ToList();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var token = await _payPal.SaveCardAsync(buyerId, ToCard(request.Card),
            $"eshop-payment-method-{Guid.NewGuid():N}", cancellationToken);
        var method = new PaymentMethod(buyerId, token.Id, token.Brand, token.LastDigits,
            token.Expiry, DateTimeOffset.UtcNow);
        _context.PaymentMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);
        return PaymentMethodResponse(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> PaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var methods = await _context.PaymentMethods.AsNoTracking()
            .Where(method => method.BuyerId == buyerId && method.DeletedAt == null)
            .OrderByDescending(method => method.CreatedAt).ToListAsync(cancellationToken);
        return methods.Select(PaymentMethodResponse).ToList();
    }

    public async Task DeletePaymentMethodAsync(int paymentMethodId, string buyerId,
        CancellationToken cancellationToken)
    {
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(method =>
            method.Id == paymentMethodId && method.BuyerId == buyerId, cancellationToken);
        if (method is null) throw new KeyNotFoundException("The saved payment method was not found.");
        if (method.IsDeleted) return;
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.PaypalPaymentTokenId, cancellationToken);
        }
        catch (PayPalException exception) when (exception.StatusCode == HttpStatusCode.NotFound) { }
        method.Delete(DateTimeOffset.UtcNow);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw new ArgumentException("from must be earlier than to.");
        if (to > DateTimeOffset.UtcNow.AddMinutes(5))
            throw new ArgumentException("to cannot be in the future.");

        var paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var paypalIds = paypal.SelectMany(transaction => new[]
            { transaction.TransactionId, transaction.PaypalReferenceId })
            .Where(id => id is not null).Select(id => id!).Distinct().ToList();
        var paymentReferences = paypal.Where(transaction =>
                transaction.InvoiceId?.StartsWith("eshop-", StringComparison.Ordinal) == true)
            .Select(transaction => transaction.InvoiceId![6..]).Distinct().ToList();
        var orders = await _context.Orders.AsNoTracking().Include(order => order.Refunds)
            .Where(order => (order.CapturedAt >= from && order.CapturedAt <= to) ||
                            order.Refunds.Any(refund => refund.CreatedAt >= from && refund.CreatedAt <= to) ||
                            (order.PaypalOrderId != null && paypalIds.Contains(order.PaypalOrderId)) ||
                            (order.PaypalCaptureId != null && paypalIds.Contains(order.PaypalCaptureId)) ||
                            (order.PaymentReference != null && paymentReferences.Contains(order.PaymentReference)) ||
                            order.Refunds.Any(refund => paypalIds.Contains(refund.PaypalRefundId)))
            .ToListAsync(cancellationToken);

        var local = new List<LocalTransaction>();
        foreach (var order in orders)
        {
            if (order.PaypalCaptureId is not null && order.CapturedAt >= from && order.CapturedAt <= to)
                local.Add(new LocalTransaction(order.PaypalCaptureId, "Capture", order.Id,
                    order.CapturedAmount!.Value, order.PaymentCurrency!, order.CapturedAt.Value));
            local.AddRange(order.Refunds.Where(refund => refund.CreatedAt >= from && refund.CreatedAt <= to)
                .Select(refund => new LocalTransaction(refund.PaypalRefundId, "Refund", order.Id,
                    refund.Amount, refund.Currency, refund.CreatedAt)));
        }

        int? FindOrder(PayPalTransactionRecord transaction)
        {
            var byTransaction = local.FirstOrDefault(item => item.PaypalTransactionId == transaction.TransactionId);
            if (byTransaction is not null) return byTransaction.OrderId;
            if (transaction.PaypalReferenceId is not null)
            {
                var byReference = orders.FirstOrDefault(order =>
                    order.PaypalOrderId == transaction.PaypalReferenceId ||
                    order.PaypalCaptureId == transaction.PaypalReferenceId ||
                    order.Refunds.Any(refund => refund.PaypalRefundId == transaction.PaypalReferenceId));
                if (byReference is not null) return byReference.Id;
            }
            if (transaction.InvoiceId?.StartsWith("eshop-", StringComparison.Ordinal) == true)
            {
                var paymentReference = transaction.InvoiceId[6..];
                var byInvoice = orders.FirstOrDefault(order => order.PaymentReference == paymentReference);
                if (byInvoice is not null) return byInvoice.Id;
            }
            return null;
        }

        var paypalResponses = paypal.Select(transaction =>
        {
            var orderId = FindOrder(transaction);
            return new ReconciliationTransactionResponse(transaction.TransactionId,
                transaction.PaypalReferenceId, transaction.EventCode, transaction.Status,
                transaction.Amount, transaction.Currency, transaction.Fee, transaction.InitiatedAt,
                orderId, orderId.HasValue ? "Matched" : "PaypalOnly");
        }).ToList();
        var reportedIds = paypal.SelectMany(transaction => new[]
            { transaction.TransactionId, transaction.PaypalReferenceId }).Where(id => id is not null).ToHashSet();
        var eshopOnly = local.Where(item => !reportedIds.Contains(item.PaypalTransactionId))
            .Select(item => new EshopOnlyTransactionResponse(item.PaypalTransactionId, item.Kind,
                item.OrderId, item.Amount, item.Currency, item.OccurredAt)).ToList();
        return new ReconciliationResponse(from, to, paypalResponses, eshopOnly);
    }

    private async Task<Order> OrderAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await _context.Orders.Include(order => order.OrderItems)
            .Include(order => order.Refunds).SingleOrDefaultAsync(order => order.Id == orderId,
                cancellationToken);
        return order ?? throw new KeyNotFoundException("The order was not found.");
    }

    private async Task<Order> OwnedOrderAsync(int orderId, string buyerId,
        CancellationToken cancellationToken)
    {
        var order = await _context.Orders.Include(order => order.OrderItems)
            .Include(order => order.Refunds).SingleOrDefaultAsync(order =>
                order.Id == orderId && order.BuyerId == buyerId, cancellationToken);
        return order ?? throw new KeyNotFoundException("The order was not found.");
    }

    private static CardInput ToCard(CardRequest card)
    {
        if (string.IsNullOrWhiteSpace(card.Name) || string.IsNullOrWhiteSpace(card.Number) ||
            string.IsNullOrWhiteSpace(card.Expiry) || string.IsNullOrWhiteSpace(card.SecurityCode) ||
            card.BillingAddress is null || string.IsNullOrWhiteSpace(card.BillingAddress.CountryCode))
            throw new ArgumentException("Card name, number, expiry, security code, and billing country are required.");
        return new CardInput(card.Name, card.Number.Replace(" ", string.Empty, StringComparison.Ordinal),
            card.Expiry, card.SecurityCode,
            new CardAddress(card.BillingAddress.CountryCode.ToUpperInvariant(),
                card.BillingAddress.AddressLine1, card.BillingAddress.AddressLine2,
                card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode));
    }

    private static void EnsureAmountAndCurrency(Order order, decimal amount, string currency,
        decimal? expectedAmount = null)
    {
        var expected = expectedAmount ?? order.Total();
        if (decimal.Round(amount, 2) != decimal.Round(expected, 2) ||
            !string.Equals(currency, order.PaymentCurrency, StringComparison.OrdinalIgnoreCase))
            throw new PaymentConflictException("PayPal reported an amount or currency that does not match the order.");
    }

    private static void EnsureAuthorizationStatus(string status)
    {
        if (status is not ("CREATED" or "PENDING"))
            throw new PaymentConflictException(
                $"PayPal returned authorization status '{status}', so the order was not authorized.");
    }

    private static void EnsureCaptureStatus(string status)
    {
        if (status is not ("COMPLETED" or "PENDING"))
            throw new PaymentConflictException(
                $"PayPal returned capture status '{status}', so fulfilment was not completed.");
    }

    private static string PayPalRefundRequestId(string callerKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(callerKey));
        return $"eshop-refund-{Convert.ToHexString(digest).ToLowerInvariant()}";
    }

    private static PaymentConflictException RenewalRequired(int orderId, string status) => new(
        $"PayPal authorization for order {orderId} can no longer be renewed (status {status}).",
        $"Ask the shopper to submit POST /api/orders/{orderId}/pay again with a card or active saved payment method, then retry fulfilment.");

    private static OrderActionResponse ActionResponse(Order order) => new(order.Id,
        order.PaymentStatus.ToString(), order.FulfilmentStatus.ToString(), order.Total(),
        order.PaymentCurrency ?? string.Empty, order.PaypalAuthorizationId, order.PaypalCaptureId,
        order.CapturedAmount, order.PaypalFee, order.NetProceeds, order.RefundedAmount);

    private static RefundCreatedResponse RefundResponse(Order order, PaymentRefund refund) => new(
        refund.Id, order.Id, refund.PaypalRefundId, refund.PaypalStatus, refund.Amount,
        refund.Currency);

    private static PaymentMethodResponse PaymentMethodResponse(PaymentMethod method) => new(
        method.Id, method.Brand, method.LastDigits, method.Expiry, method.CreatedAt);

    private static MyOrderResponse MyOrderResponse(Order order) => new(order.Id, order.OrderDate,
        order.Total(), order.PaymentCurrency, order.PaymentStatus.ToString(),
        order.FulfilmentStatus.ToString(), order.PaypalAuthorizationStatus,
        order.PaypalCaptureStatus, order.CapturedAmount, order.PaypalFee, order.NetProceeds,
        order.RefundedAmount, order.OrderItems.Select(item => new OrderItemResponse(
            item.ItemOrdered.CatalogItemId, item.ItemOrdered.ProductName, item.UnitPrice,
            item.Units)).ToList(), order.Refunds.Select(refund => new RefundResponse(refund.Id,
            refund.PaypalRefundId, refund.PaypalStatus, refund.Amount, refund.Currency,
            refund.CreatedAt)).ToList());

    private sealed record LocalTransaction(string PaypalTransactionId, string Kind, int OrderId,
        decimal Amount, string Currency, DateTimeOffset OccurredAt);
}
