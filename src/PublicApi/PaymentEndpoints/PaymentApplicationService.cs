using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public sealed class PaymentApplicationService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> OperationLocks = new();
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);
    private readonly CatalogContext _db;
    private readonly IPayPalClient _payPal;
    private readonly TimeProvider _timeProvider;
    private readonly string _currency;

    public PaymentApplicationService(CatalogContext db, IPayPalClient payPal, TimeProvider timeProvider,
        IOptions<PayPalOptions> options)
    {
        _db = db;
        _payPal = payPal;
        _timeProvider = timeProvider;
        _currency = options.Value.Currency.ToUpperInvariant();
    }

    public async Task<CreatePaidOrderResponse> CreateOrderAsync(string buyerId, CreatePaidOrderRequest request,
        CancellationToken cancellationToken)
    {
        EnsureCurrency();
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        if (request.Items.Count is < 1 or > 100)
            throw BadRequest("An order must contain between 1 and 100 catalog items.");
        ValidateShippingAddress(request.ShipToAddress);

        var quantities = new Dictionary<int, int>();
        foreach (var item in request.Items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity is < 1 or > 1000)
                throw BadRequest("Every item needs a positive catalogItemId and a quantity from 1 to 1000.");
            quantities[item.CatalogItemId] = checked(quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity);
        }

        var catalogItems = await _db.CatalogItems
            .Where(x => quantities.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = quantities.Keys.Except(catalogItems.Select(x => x.Id)).OrderBy(x => x).ToArray();
        if (missing.Length > 0)
            throw BadRequest($"Catalog item(s) {string.Join(", ", missing)} do not exist.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            RequireCentAmount(item.Price, $"Catalog item {item.Id} price"),
            quantities[item.Id])).ToList();
        var address = new Address(request.ShipToAddress.Street, request.ShipToAddress.City,
            request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);
        var order = new Order(buyerId, address, items);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return new CreatePaidOrderResponse(order.Id, order.Status.ToString(), order.Total(), _currency);
    }

    public Task<PayOrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken) => WithLockAsync($"order:{orderId}", async () =>
    {
        EnsureCurrency();
        ValidatePaymentChoice(request);
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        if (order.Status == OrderStatus.Authorized && order.Payment is not null)
            return new PayOrderResponse(order.Id, order.Status.ToString(), order.Payment.ToDto());
        if (order.Status != OrderStatus.AwaitingPayment)
            throw Conflict($"Order {orderId} cannot be paid while it is {order.Status}.");

        var payment = order.Payment ?? order.StartPayment(_currency,
            $"eshop-order-{order.PaymentOperationId:N}-create");
        if (payment.PayPalOrderId is null)
        {
            PayPalOrderCreationResult created;
            try
            {
                created = await _payPal.CreateOrderAsync(order.Id, order.Total(), _currency,
                    payment.CreateOrderRequestId, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw PayPalFailure("creating the PayPal order", ex);
            }
            payment.RecordPayPalOrder(created.Id, created.Status, Now());
            await _db.SaveChangesAsync(cancellationToken);
        }

        PayPalAuthorizationResult? authorization;
        try
        {
            authorization = await _payPal.GetOrderAuthorizationAsync(payment.PayPalOrderId!, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw PayPalFailure("checking the PayPal order", ex);
        }

        if (authorization is null)
        {
            var source = request.PaymentMethodId.HasValue
                ? PayPalPaymentSource.FromVault(await GetOwnedVaultIdAsync(buyerId, request.PaymentMethodId.Value,
                    cancellationToken))
                : PayPalPaymentSource.FromCard(ToPayPalCard(request.Card!));
            try
            {
                authorization = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, source,
                    payment.AuthorizeRequestId!, cancellationToken);
            }
            catch (PayPalPayerActionRequiredException ex)
            {
                throw new ApiException(StatusCodes.Status422UnprocessableEntity, "Browser approval required", ex.Message);
            }
            catch (PayPalApiException ex)
            {
                throw PayPalFailure("authorizing the card", ex);
            }
        }

        await ValidateAuthorizationAsync(order, payment, authorization, cancellationToken);
        payment.RecordAuthorization(authorization.PayPalOrderStatus, authorization.AuthorizationId,
            authorization.AuthorizationStatus, authorization.Amount, authorization.CreatedAt,
            authorization.ExpiresAt, authorization.CardBrand, authorization.CardLast4, Now());
        order.MarkAuthorized();
        await _db.SaveChangesAsync(cancellationToken);
        return new PayOrderResponse(order.Id, order.Status.ToString(), payment.ToDto());
    }, cancellationToken);

    public Task<FulfilOrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken) =>
        WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await LoadOrderAsync(orderId, null, cancellationToken);
            var payment = order.Payment ?? throw Conflict("The order has no payment to capture.");
            if (payment.CaptureId is not null && order.Status is (OrderStatus.Fulfilled or
                OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
                return new FulfilOrderResponse(order.Id, order.Status.ToString(), payment.ToDto());
            if (payment.CaptureId is not null)
            {
                PayPalCaptureResult refreshed;
                try
                {
                    refreshed = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw PayPalFailure("checking the pending capture", ex);
                }
                RequirePayPalAmount(refreshed.Amount, refreshed.Currency, order.Total(), "capture");
                payment.RecordCapture(refreshed.CaptureId, refreshed.Status, refreshed.Amount,
                    refreshed.PayPalFee, refreshed.NetAmount, refreshed.CreatedAt, Now());
                if (string.Equals(refreshed.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                    order.MarkFulfilled();
                else if (string.Equals(refreshed.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                    order.MarkFulfilmentPending();
                await _db.SaveChangesAsync(cancellationToken);
                if (payment.Status == PaymentStatus.Failed)
                    throw Conflict($"PayPal capture {payment.CaptureId} is {refreshed.Status}. Do not ship this order; " +
                                   "review the payment in PayPal and ask the shopper to place a new order if needed.");
                return new FulfilOrderResponse(order.Id, order.Status.ToString(), payment.ToDto());
            }
            if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
                throw Conflict($"Order {orderId} must have an authorized payment before fulfilment.");

            PayPalAuthorizationResult current;
            try
            {
                current = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw PayPalFailure("checking the authorization before fulfilment", ex);
            }

            if (!string.Equals(current.AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase))
                throw Conflict($"PayPal authorization {payment.AuthorizationId} is {current.AuthorizationStatus}. " +
                               "Do not ship this order; ask the shopper to place and pay for a new order.");

            var now = Now();
            var expiresAt = current.ExpiresAt ?? current.CreatedAt.AddDays(29);
            if (expiresAt <= now)
                throw Conflict($"PayPal authorization {payment.AuthorizationId} expired and can no longer be renewed. " +
                               "Do not ship this order; ask the shopper to place and pay for a new order.");

            if (current.CreatedAt.AddDays(3) <= now)
            {
                try
                {
                    current = await _payPal.ReauthorizeAsync(payment.AuthorizationId, order.Total(), _currency,
                        $"eshop-order-{order.Id}-reauthorize-{payment.AuthorizationId}", cancellationToken);
                }
                catch (PayPalApiException ex) when (ex.HasIssue("REAUTHORIZE_NOT_ALLOWED") ||
                                                     ex.HasIssue("AUTHORIZATION_EXPIRED"))
                {
                    throw Conflict($"PayPal can no longer renew authorization {payment.AuthorizationId}. " +
                                   "Do not ship this order; ask the shopper to place and pay for a new order.");
                }
                catch (PayPalApiException ex)
                {
                    throw PayPalFailure("renewing the stale authorization", ex);
                }

                RequirePayPalAmount(current.Amount, current.Currency, order.Total(), "reauthorization");
                payment.RecordReauthorization(current.AuthorizationId, current.AuthorizationStatus, current.Amount,
                    current.CreatedAt, current.ExpiresAt, now);
                await _db.SaveChangesAsync(cancellationToken);
            }

            payment.StartCapture(now);
            await _db.SaveChangesAsync(cancellationToken);
            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAsync(payment.AuthorizationId!, order.Total(), _currency,
                    payment.CaptureRequestId!, cancellationToken);
            }
            catch (PayPalApiException ex)
            {
                throw PayPalFailure("capturing the authorized funds", ex);
            }

            RequirePayPalAmount(capture.Amount, capture.Currency, order.Total(), "capture");
            payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.PayPalFee,
                capture.NetAmount, capture.CreatedAt, Now());
            if (string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                order.MarkFulfilled();
            else if (string.Equals(capture.Status, "PENDING", StringComparison.OrdinalIgnoreCase))
                order.MarkFulfilmentPending();
            await _db.SaveChangesAsync(cancellationToken);
            if (payment.Status == PaymentStatus.Failed)
                throw Conflict($"PayPal capture {capture.CaptureId} is {capture.Status}. Do not ship this order; " +
                               "review the payment in PayPal and ask the shopper to place a new order if needed.");
            return new FulfilOrderResponse(order.Id, order.Status.ToString(), payment.ToDto());
        }, cancellationToken);

    public Task<CancelOrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken) =>
        WithLockAsync($"order:{orderId}", async () =>
        {
            var order = await LoadOrderAsync(orderId, null, cancellationToken);
            if (order.Status == OrderStatus.Cancelled)
                return new CancelOrderResponse(order.Id, order.Status.ToString(), order.Payment?.ToDto());
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.FulfilmentPending or
                OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                throw Conflict("A captured or fulfilled order cannot be cancelled; issue a refund instead.");

            var payment = order.Payment;
            if (payment?.PayPalOrderId is not null && payment.AuthorizationId is null)
            {
                try
                {
                    var recovered = await _payPal.GetOrderAuthorizationAsync(payment.PayPalOrderId, cancellationToken);
                    if (recovered is not null)
                    {
                        payment.RecordAuthorization(recovered.PayPalOrderStatus, recovered.AuthorizationId,
                            recovered.AuthorizationStatus, recovered.Amount, recovered.CreatedAt,
                            recovered.ExpiresAt, recovered.CardBrand, recovered.CardLast4, Now());
                    }
                }
                catch (PayPalApiException ex)
                {
                    throw PayPalFailure("checking for held funds before cancellation", ex);
                }
            }

            if (payment?.AuthorizationId is not null && payment.Status != PaymentStatus.Voided)
            {
                try
                {
                    var current = await _payPal.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
                    if (string.Equals(current.AuthorizationStatus, "CAPTURED", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(current.AuthorizationStatus, "PARTIALLY_CAPTURED", StringComparison.OrdinalIgnoreCase))
                        throw Conflict("PayPal reports captured funds; refund the order instead of cancelling it.");
                    if (!string.Equals(current.AuthorizationStatus, "VOIDED", StringComparison.OrdinalIgnoreCase))
                    {
                        var voided = await _payPal.VoidAsync(payment.AuthorizationId,
                            $"eshop-order-{order.Id}-void-{payment.AuthorizationId}", cancellationToken);
                        payment.RecordVoid(voided.Status, Now());
                    }
                    else
                    {
                        payment.RecordVoid(current.AuthorizationStatus, Now());
                    }
                }
                catch (PayPalApiException ex) when (ex.HasIssue("PREVIOUSLY_CAPTURED"))
                {
                    throw Conflict("PayPal reports captured funds; refund the order instead of cancelling it.");
                }
                catch (PayPalApiException ex)
                {
                    throw PayPalFailure("releasing the authorization", ex);
                }
            }

            order.MarkCancelled();
            await _db.SaveChangesAsync(cancellationToken);
            return new CancelOrderResponse(order.Id, order.Status.ToString(), payment?.ToDto());
        }, cancellationToken);

    public Task<RefundOrderResponse> RefundAsync(string buyerId, int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken) => WithLockAsync($"order:{orderId}", async () =>
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Length > 200)
            throw BadRequest("idempotencyKey is required and must not exceed 200 characters.");
        var order = await LoadOrderAsync(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("The order has no captured payment to refund.");
        if (payment.CaptureId is null || payment.CaptureAmount is null)
            throw Conflict("The order has no captured payment to refund.");
        if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
            throw Conflict($"Order {orderId} cannot be refunded while it is {order.Status}.");

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == request.IdempotencyKey);
        if (existing is not null && request.Amount.HasValue && request.Amount.Value != existing.Amount)
            throw Conflict("This idempotencyKey was already used with a different refund amount.");
        if (existing?.PayPalRefundId is not null)
        {
            if (existing.Status == PaymentRefundStatus.Pending)
            {
                PayPalRefundResult refreshed;
                try
                {
                    refreshed = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                }
                catch (PayPalApiException ex)
                {
                    throw PayPalFailure("checking the pending refund", ex);
                }
                RequirePayPalAmount(refreshed.Amount, refreshed.Currency, existing.Amount, "refund");
                existing.RecordResult(refreshed.RefundId, refreshed.Status, refreshed.Amount, Now());
                payment.UpdateRefundTotals(Now());
                if (existing.Status != PaymentRefundStatus.Failed) order.UpdateRefundState();
                await _db.SaveChangesAsync(cancellationToken);
            }
            return RefundResponse(order, payment, existing);
        }

        var reserved = payment.Refunds.Where(x => x.Status != PaymentRefundStatus.Failed).Sum(x => x.Amount);
        var remaining = payment.CaptureAmount.Value - reserved;
        var amount = existing?.Amount ?? request.Amount ?? remaining;
        amount = RequireCentAmount(amount, "Refund amount");
        if (amount <= 0 || amount > remaining)
            throw BadRequest($"Refund amount must be greater than zero and no more than {remaining:0.00} {_currency}.");

        var refund = existing ?? payment.StartRefund(request.IdempotencyKey,
            RefundRequestId(payment.CaptureId, request.IdempotencyKey), amount, Now());
        await _db.SaveChangesAsync(cancellationToken);
        PayPalRefundResult result;
        try
        {
            result = await _payPal.RefundAsync(payment.CaptureId, refund.Amount, _currency,
                refund.PayPalRequestId, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw PayPalFailure("refunding the capture", ex);
        }

        RequirePayPalAmount(result.Amount, result.Currency, refund.Amount, "refund");
        refund.RecordResult(result.RefundId, result.Status, result.Amount, Now());
        payment.UpdateRefundTotals(Now());
        if (refund.Status != PaymentRefundStatus.Failed) order.UpdateRefundState();
        await _db.SaveChangesAsync(cancellationToken);
        return RefundResponse(order, payment, refund);
    }, cancellationToken);

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        var orders = await _db.Orders.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(ToOrderDto).ToArray();
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        var card = ToPayPalCard(request);
        var customerId = await _db.SavedPaymentMethods
            .Where(x => x.BuyerId == buyerId)
            .Select(x => x.PayPalCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
        PayPalSavedCardResult saved;
        try
        {
            saved = await _payPal.SaveCardAsync(card, customerId,
                $"eshop-vault-setup-{Guid.NewGuid():N}", $"eshop-vault-token-{Guid.NewGuid():N}", cancellationToken);
        }
        catch (PayPalPayerActionRequiredException ex)
        {
            throw new ApiException(StatusCodes.Status422UnprocessableEntity, "Browser approval required", ex.Message);
        }
        catch (PayPalApiException ex)
        {
            throw PayPalFailure("saving the card", ex);
        }

        var method = new SavedPaymentMethod(buyerId, saved.PaymentTokenId, saved.CustomerId, saved.Brand,
            saved.Last4, saved.Expiry, Now());
        _db.SavedPaymentMethods.Add(method);
        await _db.SaveChangesAsync(cancellationToken);
        return ToPaymentMethodResponse(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
        var methods = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
        return methods.Select(ToPaymentMethodResponse).ToArray();
    }

    public Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken) =>
        WithLockAsync($"payment-method:{paymentMethodId}", async () =>
        {
            if (string.IsNullOrWhiteSpace(buyerId)) throw Unauthorized();
            var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(
                x => x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
            if (method is null) throw NotFound("Payment method not found.");
            try
            {
                await _payPal.DeletePaymentTokenAsync(method.PayPalPaymentTokenId, cancellationToken);
            }
            catch (PayPalApiException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
            {
                // The remote token is already gone; complete the idempotent local deletion.
            }
            catch (PayPalApiException ex)
            {
                throw PayPalFailure("deleting the saved card", ex);
            }
            method.Deactivate(Now());
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }, cancellationToken);

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (to <= from) throw BadRequest("to must be later than from.");
        IReadOnlyList<PayPalTransaction> remote;
        try
        {
            remote = await _payPal.SearchTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalApiException ex)
        {
            throw PayPalFailure("retrieving PayPal transaction reporting", ex);
        }

        var payments = await _db.OrderPayments.AsNoTracking().Include(x => x.Refunds)
            .ToListAsync(cancellationToken);
        var byIdentifier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var payment in payments)
        {
            AddIdentifier(byIdentifier, payment.PayPalOrderId, payment.OrderId);
            AddIdentifier(byIdentifier, payment.AuthorizationId, payment.OrderId);
            AddIdentifier(byIdentifier, payment.CaptureId, payment.OrderId);
            foreach (var refund in payment.Refunds) AddIdentifier(byIdentifier, refund.PayPalRefundId, payment.OrderId);
        }

        var remoteIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remoteDtos = remote.Select(transaction =>
        {
            remoteIds.Add(transaction.TransactionId);
            if (transaction.ReferenceId is not null) remoteIds.Add(transaction.ReferenceId);
            int? orderId = null;
            if (byIdentifier.TryGetValue(transaction.TransactionId, out var direct)) orderId = direct;
            else if (transaction.ReferenceId is not null && byIdentifier.TryGetValue(transaction.ReferenceId, out var referenced))
                orderId = referenced;
            return new ReconciliationTransactionDto(transaction.TransactionId, transaction.ReferenceId,
                transaction.EventCode, transaction.Status, transaction.InitiatedAt, transaction.Amount,
                transaction.Fee, transaction.Currency, orderId, orderId.HasValue ? "Matched" : "PayPalOnly");
        }).ToArray();

        var localOnly = new List<UnmatchedEShopPaymentDto>();
        foreach (var payment in payments)
        {
            if (payment.AuthorizationId is not null && payment.AuthorizationCreatedAt is { } authorizedAt &&
                authorizedAt >= from && authorizedAt <= to && !remoteIds.Contains(payment.AuthorizationId))
                localOnly.Add(new(payment.OrderId, "Authorization", payment.AuthorizationId,
                    payment.AuthorizationStatus ?? payment.Status.ToString(), authorizedAt,
                    payment.AuthorizationAmount ?? 0, payment.Currency));
            if (payment.CaptureId is not null && payment.CapturedAt is { } capturedAt &&
                capturedAt >= from && capturedAt <= to && !remoteIds.Contains(payment.CaptureId))
                localOnly.Add(new(payment.OrderId, "Capture", payment.CaptureId,
                    payment.CaptureStatus ?? payment.Status.ToString(), capturedAt,
                    payment.CaptureAmount ?? 0, payment.Currency));
            foreach (var refund in payment.Refunds)
            {
                if (refund.PayPalRefundId is not null && refund.CreatedAt >= from && refund.CreatedAt <= to &&
                    !remoteIds.Contains(refund.PayPalRefundId))
                    localOnly.Add(new(payment.OrderId, "Refund", refund.PayPalRefundId,
                        refund.PayPalStatus ?? refund.Status.ToString(), refund.CreatedAt, refund.Amount, refund.Currency));
            }
        }

        return new ReconciliationResponse(from, to, remoteDtos,
            localOnly.OrderBy(x => x.OccurredAt).ToArray());
    }

    private async Task<Order> LoadOrderAsync(int orderId, string? buyerId, CancellationToken cancellationToken)
    {
        var query = _db.Orders
            .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
            .Include(x => x.Payment).ThenInclude(x => x!.Refunds)
            .Where(x => x.Id == orderId);
        if (buyerId is not null) query = query.Where(x => x.BuyerId == buyerId);
        return await query.SingleOrDefaultAsync(cancellationToken)
               ?? throw NotFound("Order not found.");
    }

    private async Task<string> GetOwnedVaultIdAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        var method = await _db.SavedPaymentMethods.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId && x.DeletedAt == null, cancellationToken);
        return method?.PayPalPaymentTokenId ?? throw NotFound("Payment method not found.");
    }

    private async Task ValidateAuthorizationAsync(Order order, OrderPayment payment,
        PayPalAuthorizationResult authorization, CancellationToken cancellationToken)
    {
        try
        {
            RequirePayPalAmount(authorization.Amount, authorization.Currency, order.Total(), "authorization");
        }
        catch (ApiException)
        {
            try
            {
                await _payPal.VoidAsync(authorization.AuthorizationId,
                    $"eshop-order-{order.Id}-void-mismatch", cancellationToken);
                payment.RecordVoid("VOIDED", Now());
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (PayPalApiException)
            {
                // Preserve the original mismatch error; reconciliation exposes the remote authorization.
            }
            throw;
        }
    }

    private static RefundOrderResponse RefundResponse(Order order, OrderPayment payment, PaymentRefund refund)
    {
        var remaining = Math.Max(0, (payment.CaptureAmount ?? 0) - payment.RefundedAmount);
        return new RefundOrderResponse(refund.PayPalRefundId!, order.Id,
            refund.PayPalStatus ?? refund.Status.ToString(), refund.Amount, refund.Currency,
            payment.RefundedAmount, remaining);
    }

    private OrderDto ToOrderDto(Order order) => new(
        order.Id,
        order.OrderDate,
        order.Status.ToString(),
        order.Total(),
        order.Payment?.Currency ?? _currency,
        order.OrderItems.Select(x => new OrderItemDto(x.ItemOrdered.CatalogItemId, x.ItemOrdered.ProductName,
            x.UnitPrice, x.Units)).ToArray(),
        order.Payment?.ToDto());

    private static PaymentMethodResponse ToPaymentMethodResponse(SavedPaymentMethod method) =>
        new(method.Id, method.Brand, method.Last4, method.Expiry, method.CreatedAt);

    private PayPalCardDetails ToPayPalCard(CardRequest request)
    {
        var number = new string(request.Number.Where(char.IsDigit).ToArray());
        if (number.Length is < 12 or > 19 || !PassesLuhn(number))
            throw BadRequest("The card number is invalid.");
        if (!ExpiryPattern.IsMatch(request.Expiry) ||
            !DateOnly.TryParseExact(request.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) ||
            expiry.AddMonths(1) <= DateOnly.FromDateTime(Now().UtcDateTime))
            throw BadRequest("expiry must be a future month in YYYY-MM format.");
        if (request.SecurityCode.Length is < 3 or > 4 || !request.SecurityCode.All(char.IsDigit))
            throw BadRequest("securityCode must contain three or four digits.");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 300)
            throw BadRequest("Cardholder name is required.");
        var address = request.BillingAddress ?? throw BadRequest("billingAddress is required.");
        if (string.IsNullOrWhiteSpace(address.AddressLine1) || string.IsNullOrWhiteSpace(address.AdminArea2) ||
            string.IsNullOrWhiteSpace(address.PostalCode) || address.CountryCode?.Length != 2)
            throw BadRequest("A complete billing address with a two-letter countryCode is required.");

        return new PayPalCardDetails
        {
            Number = number,
            Expiry = request.Expiry,
            SecurityCode = request.SecurityCode,
            Name = request.Name.Trim(),
            BillingAddress = new PayPalBillingAddress(address.AddressLine1, address.AddressLine2,
                address.AdminArea2, address.AdminArea1, address.PostalCode, address.CountryCode.ToUpperInvariant())
        };
    }

    private static void ValidatePaymentChoice(PayOrderRequest request)
    {
        if ((request.Card is null) == !request.PaymentMethodId.HasValue)
            throw BadRequest("Provide exactly one of card or paymentMethodId.");
        if (request.PaymentMethodId.HasValue && request.PaymentMethodId.Value <= 0)
            throw BadRequest("paymentMethodId must be positive.");
    }

    private static void ValidateShippingAddress(ShippingAddressRequest address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw BadRequest("A complete shipToAddress is required.");
    }

    private void EnsureCurrency()
    {
        if (_currency.Length != 3 || !_currency.All(char.IsLetter))
            throw new ApiException(StatusCodes.Status500InternalServerError, "Payment configuration error",
                "PayPal:Currency must be a three-letter ISO-4217 code.");
    }

    private static decimal RequireCentAmount(decimal amount, string name)
    {
        if (amount <= 0 || decimal.Round(amount, 2, MidpointRounding.ToEven) != amount)
            throw BadRequest($"{name} must be positive and have no more than two decimal places.");
        return amount;
    }

    private void RequirePayPalAmount(decimal actual, string actualCurrency, decimal expected, string operation)
    {
        if (actual != expected || !string.Equals(actualCurrency, _currency, StringComparison.OrdinalIgnoreCase))
            throw new ApiException(StatusCodes.Status502BadGateway, "PayPal amount mismatch",
                $"PayPal reported {actual:0.00} {actualCurrency} for the {operation}; expected {expected:0.00} {_currency}.");
    }

    private static bool PassesLuhn(string number)
    {
        var sum = 0;
        var doubleDigit = false;
        for (var index = number.Length - 1; index >= 0; index--)
        {
            var digit = number[index] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }

    private static string RefundRequestId(string captureId, string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(captureId + "\0" + key));
        return $"eshop-refund-{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    private static void AddIdentifier(Dictionary<string, int> lookup, string? identifier, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(identifier)) lookup[identifier] = orderId;
    }

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private static async Task<T> WithLockAsync<T>(string key, Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var gate = OperationLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try { return await action(); }
        finally { gate.Release(); }
    }

    private static ApiException PayPalFailure(string operation, PayPalApiException ex)
    {
        var status = ex.StatusCode is >= 400 and < 500
            ? StatusCodes.Status422UnprocessableEntity
            : StatusCodes.Status502BadGateway;
        var issues = ex.Issues.Count == 0 ? string.Empty : $" Issues: {string.Join(", ", ex.Issues)}.";
        var debug = string.IsNullOrWhiteSpace(ex.DebugId) ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
        return new ApiException(status, "PayPal operation failed",
            $"PayPal failed while {operation}: {ex.Message}.{issues}{debug}");
    }

    private static ApiException BadRequest(string detail) =>
        new(StatusCodes.Status400BadRequest, "Invalid request", detail);
    private static ApiException Conflict(string detail) =>
        new(StatusCodes.Status409Conflict, "Operation not allowed", detail);
    private static ApiException NotFound(string detail) =>
        new(StatusCodes.Status404NotFound, "Not found", detail);
    private static ApiException Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, "Authentication required", "The caller identity is missing.");
}
