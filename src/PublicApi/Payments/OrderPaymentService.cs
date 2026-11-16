using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class OrderPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CustomerLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly CatalogContext _db;
    private readonly IFinixPaymentGateway _finix;
    private readonly IHttpContextAccessor _http;
    private readonly FinixOptions _options;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(CatalogContext db, IFinixPaymentGateway finix, IHttpContextAccessor http,
        IOptions<FinixOptions> options, ILogger<OrderPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    private string CurrentUser
    {
        get
        {
            var user = _http.HttpContext?.User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(user))
                throw new PaymentException(401, "An authenticated shopper is required.");
            return user;
        }
    }

    private string Currency => _options.Currency.Trim().ToUpperInvariant();

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Items is null || request.Items.Count == 0)
            throw PaymentException.BadRequest("At least one catalog item is required.");
        if (request.Items.Any(i => i.Quantity <= 0))
            throw PaymentException.BadRequest("Every catalog item quantity must be greater than zero.");

        var address = request.ShippingAddress ?? throw PaymentException.BadRequest("A shipping address is required.");
        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
            throw PaymentException.BadRequest("The shipping address must include street, city, country and zip code.");

        var ids = request.Items.Select(i => i.CatalogItemId).Distinct().ToList();
        var catalogItems = await _db.CatalogItems.Where(i => ids.Contains(i.Id)).ToListAsync(cancellationToken);
        var missing = ids.Except(catalogItems.Select(i => i.Id)).ToList();
        if (missing.Count > 0)
            throw PaymentException.BadRequest($"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            var catalogItem = catalogItems.First(i => i.Id == line.CatalogItemId);
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, line.Quantity));
        }

        var order = new Order(CurrentUser,
            new Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);

        await _db.Orders.AddAsync(order, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        var payment = new OrderPayment(order.Id, order.Total(), Currency);
        await _db.OrderPayments.AddAsync(payment, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new CreateOrderResponse
        {
            OrderId = order.Id,
            Total = order.Total(),
            Currency = Currency,
            Status = payment.Status.ToString()
        };
    }

    public async Task<PayOrderResponse> PayOrderAsync(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null || !string.Equals(order.BuyerId, CurrentUser, StringComparison.OrdinalIgnoreCase))
                throw PaymentException.NotFound("Order not found.");

            var payment = await _db.OrderPayments.Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
                throw PaymentException.NotFound("Payment not found for this order.");

            if (payment.Status == PaymentStatus.Authorized)
                return MapPayResponse(payment);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                throw PaymentException.Conflict("This order has already been fulfilled. Use the refunds endpoint instead.");
            if (payment.Status == PaymentStatus.Cancelled)
                throw PaymentException.Conflict("This order was cancelled and can no longer be paid.");

            var (instrumentId, savedMethodId) = await ResolvePaymentInstrumentAsync(request, cancellationToken);
            var amountMinor = ToMinorUnits(payment.Amount);
            if (amountMinor <= 0)
                throw PaymentException.BadRequest("The order total must be greater than zero.");

            var result = await _finix.AuthorizeAsync(instrumentId, amountMinor, payment.Currency,
                payment.Status == PaymentStatus.AuthorizationFailed
                    ? $"auth:{payment.PaymentKey}:retry:{Guid.NewGuid():N}"
                    : $"auth:{payment.PaymentKey}",
                payment.OrderId.ToString(), cancellationToken);

            if (string.Equals(result.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) && !result.IsVoid)
            {
                payment.MarkAuthorized(result.AuthorizationId, result.State, result.IsVoid, result.ExpiresAt,
                    instrumentId, savedMethodId);
                await _db.SaveChangesAsync(cancellationToken);
                return MapPayResponse(payment);
            }

            payment.MarkAuthorizationFailed(result.State,
                result.FailureMessage ?? result.FailureCode ?? "Finix declined the authorization.");
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentException(402,
                $"Payment authorization failed: {result.FailureMessage ?? result.FailureCode ?? "the card was declined"}.");
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<FulfilOrderResponse> FulfilOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var payment = await _db.OrderPayments.Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
                throw PaymentException.NotFound("Payment not found for this order.");

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                return MapFulfilResponse(payment);

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
                throw PaymentException.Conflict(
                    $"Order {orderId} cannot be fulfilled because its payment is {payment.Status}. Only an authorized payment can be captured.");

            var authorization = await _finix.GetAuthorizationAsync(payment.AuthorizationId, cancellationToken);
            payment.UpdateAuthorization(authorization.State, authorization.IsVoid);
            await _db.SaveChangesAsync(cancellationToken);

            if (!CanCapture(authorization))
            {
                var reason = authorization.IsVoid
                    ? "the authorization was voided"
                    : authorization.ExpiresAt is { } expiresAt && expiresAt <= DateTimeOffset.UtcNow
                        ? $"the authorization expired at {expiresAt:u}"
                        : $"Finix reports the authorization state as {authorization.State ?? "UNKNOWN"}";

                throw PaymentException.Conflict(
                    $"The hold on order {orderId} can no longer be captured because {reason}. No funds were taken. Cancel the order or ask the shopper to pay again with a new authorization.");
            }

            FinixCaptureResult capture;
            try
            {
                capture = await _finix.CaptureAsync(payment.AuthorizationId, ToMinorUnits(payment.Amount),
                    cancellationToken);
            }
            catch (PaymentException ex)
            {
                throw PaymentException.Conflict(
                    $"The hold on order {orderId} could not be captured: {ex.Message} No funds were taken. Cancel the order or ask the shopper to pay again with a new authorization.");
            }

            if (!IsCapturedOrPending(capture.State))
                throw PaymentException.Conflict(
                    $"Finix did not complete the capture for order {orderId} (state: {capture.State ?? "UNKNOWN"}). No fulfilment was recorded.");

            payment.MarkCaptured(capture.TransferId, capture.State, FromMinorUnits(capture.AmountMinor),
                FromMinorUnits(capture.FeeMinor), FromMinorUnits(capture.NetMinor));
            await _db.SaveChangesAsync(cancellationToken);

            await RefreshCaptureAsync(payment, TimeSpan.FromSeconds(5), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);

            return MapFulfilResponse(payment);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<CancelOrderResponse> CancelOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var payment = await _db.OrderPayments.Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
                throw PaymentException.NotFound("Payment not found for this order.");

            if (payment.Status == PaymentStatus.Cancelled)
                return MapCancelResponse(payment);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
                throw PaymentException.Conflict(
                    "This order has already been fulfilled, so the authorization can no longer be voided. Use the refunds endpoint instead.");

            if (payment.Status != PaymentStatus.Authorized || string.IsNullOrWhiteSpace(payment.AuthorizationId))
                throw PaymentException.Conflict(
                    $"Order {orderId} cannot be cancelled because its payment is {payment.Status}. Only an authorized payment can be released.");

            var result = await _finix.VoidAsync(payment.AuthorizationId, cancellationToken);
            if (!result.IsVoid && !string.Equals(result.State, "CANCELLED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                throw PaymentException.Conflict(
                    $"Finix did not confirm the void for order {orderId} (state: {result.State ?? "UNKNOWN"}). The hold may still be active; retry or review the authorization in Finix.");
            }

            payment.MarkCancelled(result.State);
            await _db.SaveChangesAsync(cancellationToken);
            return MapCancelResponse(payment);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<RefundOrderResponse> RefundOrderAsync(int orderId, RefundOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            throw PaymentException.BadRequest("An idempotency key is required for refunds.");

        var orderLock = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await orderLock.WaitAsync(cancellationToken);
        try
        {
            var payment = await _db.OrderPayments.Include(p => p.Refunds)
                .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);
            if (payment is null)
                throw PaymentException.NotFound("Payment not found for this order.");

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded))
                throw PaymentException.Conflict(
                    $"Order {orderId} cannot be refunded because its payment is {payment.Status}. Only a fulfilled payment can be refunded.");

            if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
                throw PaymentException.Conflict("The order has no captured Finix transfer to refund.");

            var existing = payment.Refunds.FirstOrDefault(r =>
                string.Equals(r.IdempotencyKey, request.IdempotencyKey, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                if (request.Amount.HasValue && request.Amount.Value != existing.Amount)
                    throw PaymentException.Conflict(
                        "This idempotency key was already used for a different refund amount.");

                return MapRefundResponse(payment, existing);
            }

            var refundable = payment.RefundableAmount;
            if (refundable <= 0m)
                throw PaymentException.Conflict("This payment has already been fully refunded.");

            var amount = request.Amount ?? refundable;
            if (amount <= 0m)
                throw PaymentException.BadRequest("The refund amount must be greater than zero.");
            if (amount > refundable)
                throw PaymentException.Conflict(
                    $"The requested refund of {amount:0.00} exceeds the remaining refundable amount of {refundable:0.00}.");

            var captured = payment.CapturedAmount ?? 0m;
            if (amount < captured &&
                !string.Equals(payment.CaptureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                await RefreshCaptureAsync(payment, TimeSpan.FromSeconds(60), cancellationToken);
                await _db.SaveChangesAsync(cancellationToken);

                if (!string.Equals(payment.CaptureState, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
                    throw PaymentException.Conflict(
                        $"The Finix capture is still {payment.CaptureState ?? "PENDING"}. Partial refunds become available once Finix reports SUCCEEDED; retry shortly or refund the full remaining amount.");
            }

            var result = await _finix.RefundAsync(payment.CaptureTransferId, ToMinorUnits(amount),
                $"refund:{payment.PaymentKey}:{request.IdempotencyKey}", cancellationToken);

            if (string.IsNullOrWhiteSpace(result.TransferId))
                throw PaymentException.Upstream("Finix did not return a refund transfer id.");

            var refund = payment.AddRefund(result.TransferId, amount, request.IdempotencyKey,
                result.State, result.Type);
            await _db.SaveChangesAsync(cancellationToken);

            return MapRefundResponse(payment, refund);
        }
        finally
        {
            orderLock.Release();
        }
    }

    public async Task<MyOrdersResponse> GetMyOrdersAsync(CancellationToken cancellationToken = default)
    {
        var user = CurrentUser;
        var orders = await _db.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .Where(o => o.BuyerId == user)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToListAsync(cancellationToken);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            paymentsByOrder.TryGetValue(order.Id, out var payment);
            response.Orders.Add(new MyOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Total = order.Total(),
                Currency = payment?.Currency ?? Currency,
                Status = payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
                AuthorizationId = payment?.AuthorizationId,
                AuthorizationState = payment?.AuthorizationState,
                CaptureTransferId = payment?.CaptureTransferId,
                CaptureState = payment?.CaptureState,
                CapturedAmount = payment?.CapturedAmount,
                Fee = payment?.Fee,
                NetAmount = payment?.NetAmount,
                RefundedAmount = payment?.RefundedAmount ?? 0m,
                RefundableAmount = payment?.RefundableAmount ?? 0m,
                FailureMessage = payment?.FailureMessage,
                Items = order.OrderItems.Select(i => new MyOrderItemDto
                {
                    CatalogItemId = i.ItemOrdered.CatalogItemId,
                    ProductName = i.ItemOrdered.ProductName,
                    UnitPrice = i.UnitPrice,
                    Units = i.Units
                }).ToList(),
                Refunds = payment?.Refunds.Select(r => new MyRefundDto
                {
                    RefundId = r.FinixTransferId,
                    Amount = r.Amount,
                    State = r.State,
                    Type = r.Type,
                    IdempotencyKey = r.IdempotencyKey,
                    CreatedAt = r.CreatedAt
                }).ToList() ?? new List<MyRefundDto>()
            });
        }

        return response;
    }

    public async Task<ReconciliationResponse> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw PaymentException.BadRequest("The 'from' value must be on or before the 'to' value.");

        var records = await _finix.ListTransactionsAsync(from, to, Currency, cancellationToken);

        var orders = await _db.Orders.AsNoTracking().ToListAsync(cancellationToken);
        var payments = await _db.OrderPayments.AsNoTracking().Include(p => p.Refunds)
            .ToListAsync(cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);
        var paymentsByOrder = payments.ToDictionary(p => p.OrderId);

        var orderIdByFinixId = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var payment in payments)
        {
            AddFinixId(orderIdByFinixId, payment.AuthorizationId, payment.OrderId);
            AddFinixId(orderIdByFinixId, payment.CaptureTransferId, payment.OrderId);
            foreach (var refund in payment.Refunds)
                AddFinixId(orderIdByFinixId, refund.FinixTransferId, payment.OrderId);
        }

        var response = new ReconciliationResponse
        {
            From = from,
            To = to,
            Currency = Currency
        };

        var recordIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            recordIds.Add(record.Id);
            var dto = new ReconciliationFinixRecordDto
            {
                Id = record.Id,
                Kind = record.Kind,
                Amount = FromMinorUnits(record.AmountMinor),
                Currency = record.Currency,
                State = record.State,
                Type = record.Type,
                CreatedAt = record.CreatedAt,
                OrderId = orderIdByFinixId.TryGetValue(record.Id, out var orderId) ? orderId : null
            };
            response.FinixRecords.Add(dto);
            if (dto.OrderId is null)
                response.UnmatchedFinixRecords.Add(dto);
        }

        foreach (var payment in payments)
        {
            if (!ordersById.TryGetValue(payment.OrderId, out var order))
                continue;

            var inRange = order.OrderDate >= from && order.OrderDate <= to ||
                          payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to);
            if (!inRange)
                continue;

            var missing = new List<string>();
            AddMissingFinixId(missing, recordIds, payment.AuthorizationId);
            AddMissingFinixId(missing, recordIds, payment.CaptureTransferId);
            foreach (var refund in payment.Refunds)
                AddMissingFinixId(missing, recordIds, refund.FinixTransferId);

            if (missing.Count > 0)
            {
                response.OrdersMissingInFinix.Add(new ReconciliationMissingOrderDto
                {
                    OrderId = payment.OrderId,
                    BuyerId = order.BuyerId,
                    OrderDate = order.OrderDate,
                    Status = payment.Status.ToString(),
                    MissingFinixIds = missing
                });
            }
        }

        return response;
    }

    public async Task<SavePaymentMethodResponse> SavePaymentMethodAsync(SavePaymentMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        var card = request.Card ?? throw PaymentException.BadRequest("Card details are required.");
        ValidateCard(card);

        var user = CurrentUser;
        var identityId = await GetOrCreateIdentityAsync(user, cancellationToken);
        var result = await _finix.CreatePaymentInstrumentAsync(identityId, ToCardDetails(card), cancellationToken);

        var saved = new SavedPaymentMethod(user, result.PaymentInstrumentId, result.Brand, result.Last4,
            result.ExpirationMonth, result.ExpirationYear);
        await _db.SavedPaymentMethods.AddAsync(saved, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        return new SavePaymentMethodResponse
        {
            PaymentMethodId = saved.Id,
            Brand = saved.Brand,
            Last4 = saved.Last4,
            ExpirationMonth = saved.ExpirationMonth,
            ExpirationYear = saved.ExpirationYear
        };
    }

    public async Task<ListPaymentMethodsResponse> GetPaymentMethodsAsync(CancellationToken cancellationToken = default)
    {
        var user = CurrentUser;
        var methods = await _db.SavedPaymentMethods
            .AsNoTracking()
            .Where(m => m.UserId == user && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var response = new ListPaymentMethodsResponse();
        response.PaymentMethods.AddRange(methods.Select(m => new SavedPaymentMethodDto
        {
            PaymentMethodId = m.Id,
            Brand = m.Brand,
            Last4 = m.Last4,
            ExpirationMonth = m.ExpirationMonth,
            ExpirationYear = m.ExpirationYear,
            CreatedAt = m.CreatedAt
        }));
        return response;
    }

    public async Task<DeletePaymentMethodResponse> DeletePaymentMethodAsync(int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var user = CurrentUser;
        var method = await _db.SavedPaymentMethods.FirstOrDefaultAsync(m =>
            m.Id == paymentMethodId && m.UserId == user && !m.IsDeleted, cancellationToken);
        if (method is null)
            throw PaymentException.NotFound("Saved payment method not found.");

        method.MarkDeleted();
        await _db.SaveChangesAsync(cancellationToken);

        var hasLiveAuthorization = await _db.OrderPayments.AnyAsync(p =>
            p.SavedPaymentMethodId == method.Id && p.Status == PaymentStatus.Authorized, cancellationToken);
        if (hasLiveAuthorization)
        {
            _logger.LogInformation(
                "Saved payment method {PaymentMethodId} was removed locally but left enabled at Finix because an authorized order still needs to capture it.",
                method.Id);
        }
        else
        {
            try
            {
                await _finix.DisablePaymentInstrumentAsync(method.FinixPaymentInstrumentId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Saved payment method {PaymentMethodId} was removed locally but Finix could not be disabled.",
                    method.Id);
            }
        }

        return new DeletePaymentMethodResponse
        {
            PaymentMethodId = method.Id,
            Deleted = true
        };
    }

    private async Task<(string InstrumentId, int? SavedMethodId)> ResolvePaymentInstrumentAsync(
        PayOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.PaymentMethodId.HasValue && request.Card is not null)
            throw PaymentException.BadRequest("Provide either card details or a saved paymentMethodId, not both.");

        if (request.PaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(m =>
                m.Id == request.PaymentMethodId.Value && m.UserId == CurrentUser && !m.IsDeleted,
                cancellationToken);
            if (saved is null)
                throw PaymentException.NotFound("Saved payment method not found.");

            return (saved.FinixPaymentInstrumentId, saved.Id);
        }

        if (request.Card is null)
            throw PaymentException.BadRequest("Provide card details or a saved paymentMethodId.");

        ValidateCard(request.Card);
        var identityId = await GetOrCreateIdentityAsync(CurrentUser, cancellationToken);
        var result = await _finix.CreatePaymentInstrumentAsync(identityId, ToCardDetails(request.Card),
            cancellationToken);
        return (result.PaymentInstrumentId, null);
    }

    private async Task<string> GetOrCreateIdentityAsync(string user, CancellationToken cancellationToken)
    {
        var existing = await _db.FinixCustomers.AsNoTracking()
            .FirstOrDefaultAsync(c => c.UserId == user, cancellationToken);
        if (existing is not null)
            return existing.FinixIdentityId;

        var customerLock = CustomerLocks.GetOrAdd(user, _ => new SemaphoreSlim(1, 1));
        await customerLock.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.FinixCustomers.AsNoTracking()
                .FirstOrDefaultAsync(c => c.UserId == user, cancellationToken);
            if (existing is not null)
                return existing.FinixIdentityId;

            var localPart = user.Split('@')[0];
            var nameParts = localPart.Split(new[] { '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var firstName = nameParts.Length > 0 ? nameParts[0] : "eShop";
            var lastName = nameParts.Length > 1 ? nameParts[^1] : "Customer";
            var email = user.Contains('@', StringComparison.Ordinal) ? user : $"{localPart}@eshop.local";

            var identityId = await _finix.CreateBuyerIdentityAsync(email, firstName, lastName, cancellationToken);
            await _db.FinixCustomers.AddAsync(new FinixCustomer(user, identityId), cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            return identityId;
        }
        finally
        {
            customerLock.Release();
        }
    }

    private async Task RefreshCaptureAsync(OrderPayment payment, TimeSpan maxWait,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.CaptureTransferId))
            return;

        var deadline = DateTimeOffset.UtcNow + maxWait;
        while (true)
        {
            try
            {
                var capture = await _finix.GetCaptureAsync(payment.CaptureTransferId, cancellationToken);
                payment.UpdateCapture(capture.State, FromMinorUnits(capture.AmountMinor),
                    FromMinorUnits(capture.FeeMinor), FromMinorUnits(capture.NetMinor));

                if (string.Equals(capture.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
                    DateTimeOffset.UtcNow >= deadline)
                    return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not refresh Finix capture {TransferId}.", payment.CaptureTransferId);
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static bool CanCapture(FinixAuthorizationResult authorization) =>
        !authorization.IsVoid &&
        string.Equals(authorization.State, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) &&
        (authorization.ExpiresAt is null || authorization.ExpiresAt > DateTimeOffset.UtcNow);

    private static bool IsCapturedOrPending(string? state) =>
        string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(state, "PENDING", StringComparison.OrdinalIgnoreCase);

    private static void ValidateCard(CardPaymentRequest card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (digits.Length < 12)
            throw PaymentException.BadRequest("A valid card number is required.");
        if (card.ExpirationMonth is < 1 or > 12)
            throw PaymentException.BadRequest("The card expiration month must be between 1 and 12.");
        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
            throw PaymentException.BadRequest("The card expiration year has passed.");
        if (string.IsNullOrWhiteSpace(card.SecurityCode))
            throw PaymentException.BadRequest("The card security code is required.");
        if (string.IsNullOrWhiteSpace(card.Name))
            throw PaymentException.BadRequest("The cardholder name is required.");

        var billing = card.BillingAddress;
        if (billing is null || string.IsNullOrWhiteSpace(billing.Line1) || string.IsNullOrWhiteSpace(billing.City) ||
            string.IsNullOrWhiteSpace(billing.Region) || string.IsNullOrWhiteSpace(billing.PostalCode) ||
            string.IsNullOrWhiteSpace(billing.Country))
            throw PaymentException.BadRequest(
                "The card billing address must include line1, city, region, postal code and country.");
    }

    private static CardDetails ToCardDetails(CardPaymentRequest card) =>
        new(card.Number, card.ExpirationMonth, card.ExpirationYear, card.SecurityCode, card.Name,
            new BillingAddress(card.BillingAddress.Line1, card.BillingAddress.Line2, card.BillingAddress.City,
                card.BillingAddress.Region, card.BillingAddress.PostalCode, card.BillingAddress.Country));

    private static long ToMinorUnits(decimal amount) =>
        checked((long)decimal.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private static decimal FromMinorUnits(long amount) => amount / 100m;

    private static void AddFinixId(Dictionary<string, int> map, string? finixId, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(finixId))
            map.TryAdd(finixId, orderId);
    }

    private static void AddMissingFinixId(List<string> missing, HashSet<string> recordIds, string? finixId)
    {
        if (!string.IsNullOrWhiteSpace(finixId) && !recordIds.Contains(finixId))
            missing.Add(finixId);
    }

    private static PayOrderResponse MapPayResponse(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        Amount = payment.Amount,
        Currency = payment.Currency
    };

    private static FulfilOrderResponse MapFulfilResponse(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        CaptureTransferId = payment.CaptureTransferId,
        CaptureState = payment.CaptureState,
        CapturedAmount = payment.CapturedAmount ?? 0m,
        Fee = payment.Fee ?? 0m,
        NetAmount = payment.NetAmount ?? 0m,
        Currency = payment.Currency
    };

    private static CancelOrderResponse MapCancelResponse(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState
    };

    private static RefundOrderResponse MapRefundResponse(OrderPayment payment, PaymentRefund refund) => new()
    {
        RefundId = refund.FinixTransferId,
        OrderId = payment.OrderId,
        Status = payment.Status.ToString(),
        RefundAmount = refund.Amount,
        RefundedAmount = payment.RefundedAmount,
        RemainingRefundable = payment.RefundableAmount,
        RefundState = refund.State,
        Currency = payment.Currency
    };
}
