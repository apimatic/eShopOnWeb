using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed class PaymentWorkflowService
{
    private readonly CatalogContext _context;
    private readonly IPayPalClient _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly string _currency;

    public PaymentWorkflowService(CatalogContext context, IPayPalClient payPal,
        PaymentOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _context = context;
        _payPal = payPal;
        _operationLock = operationLock;
        _currency = options.Value.Currency.ToUpperInvariant();
    }

    public async Task<OrderResponse> PlaceOrderAsync(string buyerId, PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            throw BadRequest("An order must contain at least one catalog item.");
        }

        var address = ValidateShippingAddress(request.ShippingAddress);
        var quantities = new Dictionary<int, int>();
        foreach (var item in request.Items)
        {
            if (item.CatalogItemId <= 0 || item.Quantity <= 0 || item.Quantity > 1000)
            {
                throw BadRequest("Catalog item IDs must be positive and quantities must be between 1 and 1000.");
            }

            quantities[item.CatalogItemId] = quantities.GetValueOrDefault(item.CatalogItemId) + item.Quantity;
            if (quantities[item.CatalogItemId] > 1000)
            {
                throw BadRequest("The combined quantity for a catalog item cannot exceed 1000.");
            }
        }

        var catalogItems = await _context.CatalogItems
            .Where(x => quantities.Keys.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var missing = quantities.Keys.Except(catalogItems.Select(x => x.Id)).ToList();
        if (missing.Count > 0)
        {
            throw BadRequest($"Catalog item IDs were not found: {string.Join(", ", missing)}.");
        }

        var orderItems = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
            item.Price, quantities[item.Id])).ToList();
        var order = new Order(buyerId, address, orderItems);
        order.InitializePayment(_currency);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return MapOrder(order);
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        var orders = await OrdersWithPayment()
            .AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate)
            .ToListAsync(cancellationToken);
        return orders.Select(MapOrder).ToList();
    }

    public async Task<OrderResponse> PayAsync(string buyerId, int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order does not have a payment record.");

        if (order.Status == OrderStatus.Authorized || payment.Status == PaymentStatus.AuthorizationPending)
        {
            return MapOrder(order);
        }

        if (order.Status != OrderStatus.AwaitingPayment ||
            order.FulfilmentStatus != FulfilmentStatus.Unfulfilled)
        {
            throw Conflict($"An order in state {order.Status} cannot be paid.");
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = request.PaymentMethodId.HasValue;
        if (hasCard == hasSavedCard)
        {
            throw BadRequest("Provide exactly one of card or paymentMethodId.");
        }

        PayPalCard? card = null;
        string? vaultId = null;
        if (request.Card is not null)
        {
            card = ValidateCard(request.Card);
        }
        else
        {
            var savedCard = await _context.PaymentMethods.SingleOrDefaultAsync(
                x => x.Id == request.PaymentMethodId && x.BuyerId == buyerId, cancellationToken);
            if (savedCard is null)
            {
                throw NotFound("The saved payment method was not found.");
            }

            vaultId = savedCard.PayPalVaultId;
        }

        var total = ToMoney(order.Total());
        if (payment.PayPalOrderId is null)
        {
            var paypalOrder = await _payPal.CreateOrderAsync(total, payment.Currency,
                payment.InvoiceId, $"eshop-order-{order.Id}", payment.CreateOrderRequestId,
                cancellationToken);
            payment.RecordPayPalOrder(paypalOrder.Id, paypalOrder.Status);
            await _context.SaveChangesAsync(cancellationToken);
        }

        var authorization = await _payPal.AuthorizeOrderAsync(payment.PayPalOrderId!, card,
            vaultId, payment.AuthorizeRequestId, cancellationToken);
        if (authorization.RequiresPayerAction)
        {
            throw new ApiProblemException(422, "Browser approval required",
                "PayPal requires an interactive cardholder challenge. This API-only integration cannot continue that payment.");
        }

        EnsureMoney(total, payment.Currency, authorization.Amount, authorization.Currency,
            "authorization");
        if (authorization.PayPalOrderStatus is not null)
        {
            payment.RecordPayPalOrder(payment.PayPalOrderId!, authorization.PayPalOrderStatus);
        }
        payment.RecordAuthorization(authorization.Id, authorization.Status, authorization.Amount,
            authorization.CreateTime, authorization.ExpirationTime);
        if (authorization.Status == "CREATED")
        {
            order.MarkAuthorized();
        }

        await _context.SaveChangesAsync(cancellationToken);
        if (authorization.Status is not ("CREATED" or "PENDING"))
        {
            throw new ApiProblemException(422, "Payment was not authorized",
                $"PayPal returned authorization status {authorization.Status}.");
        }

        return MapOrder(order);
    }

    public async Task<OrderResponse> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order does not have a payment record.");

        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled)
        {
            return MapOrder(order);
        }

        if (payment.CaptureId is not null)
        {
            var existingCapture = await _payPal.GetCaptureAsync(payment.CaptureId, cancellationToken);
            RecordCapture(order, payment, existingCapture);
            await _context.SaveChangesAsync(cancellationToken);
            return MapOrder(order);
        }

        if (order.Status != OrderStatus.Authorized || payment.AuthorizationId is null)
        {
            throw Conflict("The order must have a current authorization before it can be fulfilled.");
        }

        var now = DateTimeOffset.UtcNow;
        var originalTime = payment.OriginalAuthorizationTime ?? payment.AuthorizationTime;
        if (originalTime.HasValue && now >= originalTime.Value.AddDays(29))
        {
            order.RequireNewPayment();
            await _context.SaveChangesAsync(cancellationToken);
            throw Conflict("The authorization is over 29 days old and PayPal cannot renew it. Ask the shopper to call the pay endpoint again, then retry fulfilment.");
        }

        var authorizationTime = payment.AuthorizationTime ?? originalTime;
        if (authorizationTime.HasValue && now >= authorizationTime.Value.AddDays(3))
        {
            try
            {
                var renewed = await _payPal.ReauthorizeAsync(payment.AuthorizationId,
                    ToMoney(order.Total()), payment.Currency, payment.ReauthorizeRequestId,
                    cancellationToken);
                EnsureMoney(ToMoney(order.Total()), payment.Currency, renewed.Amount,
                    renewed.Currency, "reauthorization");
                payment.RecordReauthorization(renewed.Id, renewed.Status, renewed.Amount,
                    renewed.CreateTime, renewed.ExpirationTime);
                await _context.SaveChangesAsync(cancellationToken);
                if (renewed.Status != "CREATED")
                {
                    throw Conflict($"PayPal returned reauthorization status {renewed.Status}. Ask the shopper to provide payment again.");
                }
            }
            catch (PayPalApiException ex) when (ex.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict)
            {
                order.RequireNewPayment();
                await _context.SaveChangesAsync(cancellationToken);
                var debug = string.IsNullOrWhiteSpace(ex.DebugId) ? string.Empty : $" PayPal debug ID: {ex.DebugId}.";
                throw Conflict("PayPal can no longer renew this authorization. Ask the shopper to call the pay endpoint again, then retry fulfilment." + debug);
            }
        }

        var capture = await _payPal.CaptureAsync(payment.AuthorizationId!, ToMoney(order.Total()),
            payment.Currency, payment.CaptureRequestId, cancellationToken);
        RecordCapture(order, payment, capture);
        await _context.SaveChangesAsync(cancellationToken);
        return MapOrder(order);
    }

    public async Task<OrderResponse> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await AnyOrder(orderId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order does not have a payment record.");

        if (order.Status == OrderStatus.Cancelled)
        {
            return MapOrder(order);
        }

        if (order.FulfilmentStatus == FulfilmentStatus.Fulfilled || payment.CaptureId is not null)
        {
            throw Conflict("A captured or fulfilled order cannot be cancelled. Refund it instead.");
        }

        if (payment.AuthorizationId is not null && payment.AuthorizationStatus != "VOIDED")
        {
            var status = await _payPal.VoidAsync(payment.AuthorizationId, payment.VoidRequestId,
                cancellationToken);
            payment.RecordVoid(status);
        }

        order.MarkCancelled();
        await _context.SaveChangesAsync(cancellationToken);
        return MapOrder(order);
    }

    public async Task<RefundCreatedResponse> RefundAsync(string buyerId, int orderId,
        RefundOrderRequest request, CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync($"order:{orderId}", cancellationToken);
        var order = await OwnedOrder(orderId, buyerId, cancellationToken);
        var payment = order.Payment ?? throw Conflict("This order does not have a payment record.");
        if (order.FulfilmentStatus != FulfilmentStatus.Fulfilled || payment.CaptureId is null ||
            payment.CapturedAmount is null)
        {
            throw Conflict("Only a captured, fulfilled order can be refunded.");
        }

        var key = request.IdempotencyKey?.Trim() ?? string.Empty;
        if (key.Length is < 1 or > 128)
        {
            throw BadRequest("idempotencyKey is required and cannot exceed 128 characters.");
        }

        if (request.Note?.Length > 255)
        {
            throw BadRequest("The refund note cannot exceed 255 characters.");
        }

        var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == key);
        if (existing is not null)
        {
            if (request.Amount.HasValue && ToMoney(request.Amount.Value) != existing.Amount)
            {
                throw Conflict("That idempotency key was already used with a different refund amount.");
            }

            if (existing.PayPalRefundId is not null && existing.Status == "PENDING")
            {
                var refreshed = await _payPal.GetRefundAsync(existing.PayPalRefundId, cancellationToken);
                existing.RecordResult(refreshed.Id, refreshed.Status, refreshed.Amount,
                    refreshed.CreateTime, refreshed.UpdateTime);
                payment.RefreshRefundStatus();
                order.UpdateRefundStatus(payment.CompletedRefundAmount());
                await _context.SaveChangesAsync(cancellationToken);
            }

            if (existing.PayPalRefundId is not null)
            {
                return MapRefundCreated(payment, existing);
            }
        }

        var remaining = ToMoney(payment.CapturedAmount.Value - payment.ReservedRefundAmount());
        if (remaining <= 0)
        {
            throw Conflict("The captured payment has no refundable amount remaining.");
        }

        var requestedAmount = request.Amount.HasValue ? ToMoney(request.Amount.Value) : remaining;
        if (requestedAmount <= 0 || requestedAmount > remaining)
        {
            throw BadRequest($"Refund amount must be positive and cannot exceed {remaining:0.00} {payment.Currency}.");
        }

        existing ??= payment.AddRefund(key, Guid.NewGuid().ToString("N"), requestedAmount);
        await _context.SaveChangesAsync(cancellationToken);

        PayPalRefundResult paypalRefund;
        try
        {
            paypalRefund = await _payPal.RefundAsync(payment.CaptureId,
                request.Amount.HasValue ? requestedAmount : null, payment.Currency,
                existing.PayPalRequestId, $"eshop-order-{order.Id}-refund", request.Note,
                cancellationToken);
        }
        catch (PayPalApiException ex) when ((int)ex.StatusCode is >= 400 and < 500 &&
                                             ex.StatusCode != HttpStatusCode.TooManyRequests)
        {
            existing.RecordFailure();
            payment.RefreshRefundStatus();
            await _context.SaveChangesAsync(cancellationToken);
            throw;
        }
        EnsureMoney(requestedAmount, payment.Currency, paypalRefund.Amount, paypalRefund.Currency,
            "refund");
        existing.RecordResult(paypalRefund.Id, paypalRefund.Status, paypalRefund.Amount,
            paypalRefund.CreateTime, paypalRefund.UpdateTime);
        payment.RefreshRefundStatus();
        order.UpdateRefundStatus(payment.CompletedRefundAmount());
        await _context.SaveChangesAsync(cancellationToken);
        return MapRefundCreated(payment, existing);
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId,
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        var card = ValidateCard(request.Card ?? throw BadRequest("card is required."));
        var saved = await _payPal.SaveCardAsync(card, Guid.NewGuid().ToString("N"), cancellationToken);
        var method = new PaymentMethod(buyerId, saved.Id, saved.Brand, saved.Last4, saved.Expiry,
            saved.CardholderName);
        _context.PaymentMethods.Add(method);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            await _payPal.DeleteSavedCardAsync(saved.Id, cancellationToken);
            throw;
        }

        return MapPaymentMethod(method);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
    {
        return await _context.PaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId)
            .OrderBy(x => x.Id)
            .Select(x => new PaymentMethodResponse
            {
                PaymentMethodId = x.Id,
                Brand = x.Brand,
                Last4 = x.Last4,
                Expiry = x.Expiry,
                CardholderName = x.CardholderName
            })
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken)
    {
        await using var operation = await _operationLock.AcquireAsync(
            $"payment-method:{paymentMethodId}", cancellationToken);
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(
            x => x.Id == paymentMethodId && x.BuyerId == buyerId, cancellationToken);
        if (method is null)
        {
            return;
        }

        await _payPal.DeleteSavedCardAsync(method.PayPalVaultId, cancellationToken);
        _context.PaymentMethods.Remove(method);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationResponse> ReconcileAsync(DateTimeOffset from,
        DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from >= to)
        {
            throw BadRequest("from must be earlier than to.");
        }

        if (from < DateTimeOffset.UtcNow.AddYears(-3))
        {
            throw BadRequest("PayPal transaction search only covers the previous three years.");
        }

        var paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        var orders = await OrdersWithPayment().AsNoTracking().ToListAsync(cancellationToken);
        var local = BuildLocalTransactions(orders, from, to);

        foreach (var transaction in paypal)
        {
            var matched = local.FirstOrDefault(x =>
                x.ExternalId == transaction.TransactionId ||
                x.ExternalId == transaction.ReferenceId ||
                x.InvoiceId == transaction.InvoiceId);
            if (matched is not null)
            {
                matched.MatchStatus = "Matched";
            }
        }

        var paypalRows = paypal.Select(transaction =>
        {
            var matched = local.FirstOrDefault(x =>
                x.ExternalId == transaction.TransactionId ||
                x.ExternalId == transaction.ReferenceId ||
                x.InvoiceId == transaction.InvoiceId);
            return new ReconciliationPayPalItem
            {
                TransactionId = transaction.TransactionId,
                ReferenceId = transaction.ReferenceId,
                EventCode = transaction.EventCode,
                InitiatedAt = transaction.InitiatedAt,
                Amount = transaction.Amount,
                Currency = transaction.Currency,
                Fee = transaction.Fee,
                Status = transaction.Status,
                InvoiceId = transaction.InvoiceId,
                OrderId = matched?.OrderId,
                MatchStatus = matched is null ? "PayPalOnly" : "Matched"
            };
        }).ToList();

        return new ReconciliationResponse
        {
            From = from,
            To = to,
            MatchedCount = paypalRows.Count(x => x.MatchStatus == "Matched"),
            PayPalOnlyCount = paypalRows.Count(x => x.MatchStatus == "PayPalOnly"),
            EShopOnlyCount = local.Count(x => x.MatchStatus == "EShopOnly"),
            PayPalTransactions = paypalRows,
            EShopTransactions = local
        };
    }

    private IQueryable<Order> OrdersWithPayment() => _context.Orders
        .Include(x => x.OrderItems)
        .Include(x => x.Payment)
        .ThenInclude(x => x!.Refunds);

    private async Task<Order> OwnedOrder(int orderId, string buyerId,
        CancellationToken cancellationToken) =>
        await OrdersWithPayment().SingleOrDefaultAsync(
            x => x.Id == orderId && x.BuyerId == buyerId, cancellationToken)
        ?? throw NotFound("The order was not found.");

    private async Task<Order> AnyOrder(int orderId, CancellationToken cancellationToken) =>
        await OrdersWithPayment().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
        ?? throw NotFound("The order was not found.");

    private static Address ValidateShippingAddress(ShippingAddressRequest? address)
    {
        if (address is null || string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
        {
            throw BadRequest("shippingAddress requires street, city, country and postalCode.");
        }

        return new Address(address.Street.Trim(), address.City.Trim(), address.State?.Trim() ?? string.Empty,
            address.Country.Trim(), address.PostalCode.Trim());
    }

    private static PayPalCard ValidateCard(CardRequest request)
    {
        var number = (request.Number ?? string.Empty).Replace(" ", string.Empty).Replace("-", string.Empty);
        if (number.Length is < 13 or > 19 || number.Any(x => !char.IsDigit(x)) ||
            string.IsNullOrWhiteSpace(request.Name) ||
            request.SecurityCode is null || request.SecurityCode.Length is < 3 or > 4 ||
            request.SecurityCode.Any(x => !char.IsDigit(x)))
        {
            throw BadRequest("Card name, a valid card number, and a 3- or 4-digit security code are required.");
        }

        if (!DateTime.TryParseExact(request.Expiry + "-01", "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry) ||
            expiry.AddMonths(1) <= DateTime.UtcNow.Date)
        {
            throw BadRequest("Card expiry must be a future month in yyyy-MM format.");
        }

        var address = request.BillingAddress;
        if (address is null || string.IsNullOrWhiteSpace(address.AddressLine1) ||
            string.IsNullOrWhiteSpace(address.City) || string.IsNullOrWhiteSpace(address.PostalCode) ||
            address.CountryCode?.Trim().Length != 2)
        {
            throw BadRequest("billingAddress requires addressLine1, city, postalCode and a two-letter countryCode.");
        }

        return new PayPalCard(request.Name.Trim(), number, request.Expiry,
            request.SecurityCode, new PayPalAddress(address.AddressLine1.Trim(),
                address.AddressLine2?.Trim(), address.City.Trim(), address.State?.Trim() ?? string.Empty,
                address.PostalCode.Trim(), address.CountryCode.Trim().ToUpperInvariant()));
    }

    private static void RecordCapture(Order order, OrderPayment payment, PayPalCaptureResult capture)
    {
        EnsureMoney(ToMoney(order.Total()), payment.Currency, capture.Amount, capture.Currency, "capture");
        payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.PayPalFee,
            capture.NetAmount, capture.CreateTime);
        if (capture.Status == "COMPLETED" && order.FulfilmentStatus != FulfilmentStatus.Fulfilled)
        {
            order.MarkFulfilled();
        }
        else if (capture.Status is not ("PENDING" or "COMPLETED"))
        {
            throw new ApiProblemException(409, "Capture did not complete",
                $"PayPal returned capture status {capture.Status}. Review the PayPal transaction before retrying fulfilment.");
        }
    }

    private static void EnsureMoney(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (expectedAmount != ToMoney(actualAmount) ||
            !expectedCurrency.Equals(actualCurrency, StringComparison.OrdinalIgnoreCase))
        {
            throw new ApiProblemException(502, "PayPal amount mismatch",
                $"PayPal reported an unexpected {operation} amount or currency. No further payment action was taken.");
        }
    }

    private static decimal ToMoney(decimal value)
    {
        var rounded = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded != value)
        {
            throw BadRequest("Amounts must not have more than two decimal places.");
        }

        return rounded;
    }

    private static OrderResponse MapOrder(Order order)
    {
        var payment = order.Payment;
        return new OrderResponse
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            OrderStatus = order.Status.ToString(),
            FulfilmentStatus = order.FulfilmentStatus.ToString(),
            Total = order.Total(),
            Currency = payment?.Currency ?? string.Empty,
            Items = order.OrderItems.Select(x => new OrderItemResponse
            {
                CatalogItemId = x.ItemOrdered.CatalogItemId,
                ProductName = x.ItemOrdered.ProductName,
                UnitPrice = x.UnitPrice,
                Quantity = x.Units
            }).ToList(),
            Payment = payment is null ? null : new PaymentResponse
            {
                Status = payment.Status.ToString(),
                PayPalOrderId = payment.PayPalOrderId,
                PayPalOrderStatus = payment.PayPalOrderStatus,
                AuthorizationId = payment.AuthorizationId,
                AuthorizationStatus = payment.AuthorizationStatus,
                AuthorizedAmount = payment.AuthorizedAmount,
                AuthorizationExpirationTime = payment.AuthorizationExpirationTime,
                CaptureId = payment.CaptureId,
                CaptureStatus = payment.CaptureStatus,
                CapturedAmount = payment.CapturedAmount,
                PayPalFee = payment.PayPalFee,
                NetAmount = payment.NetAmount,
                RefundedAmount = payment.CompletedRefundAmount(),
                Refunds = payment.Refunds.OrderBy(x => x.CreatedAt).Select(x => new RefundResponse
                {
                    RefundId = x.PayPalRefundId,
                    Status = x.Status,
                    Amount = x.Amount,
                    CreatedAt = x.CreatedAt
                }).ToList()
            }
        };
    }

    private static PaymentMethodResponse MapPaymentMethod(PaymentMethod method) => new()
    {
        PaymentMethodId = method.Id,
        Brand = method.Brand,
        Last4 = method.Last4,
        Expiry = method.Expiry,
        CardholderName = method.CardholderName
    };

    private static RefundCreatedResponse MapRefundCreated(OrderPayment payment, PaymentRefund refund) => new()
    {
        RefundId = refund.PayPalRefundId!,
        Status = refund.Status,
        Amount = refund.Amount,
        Currency = payment.Currency,
        RemainingRefundableAmount = ToMoney((payment.CapturedAmount ?? 0) - payment.ReservedRefundAmount())
    };

    private static List<ReconciliationEShopItem> BuildLocalTransactions(IEnumerable<Order> orders,
        DateTimeOffset from, DateTimeOffset to)
    {
        var result = new List<ReconciliationEShopItem>();
        foreach (var order in orders.Where(x => x.Payment is not null))
        {
            var payment = order.Payment!;
            Add(payment.PayPalOrderId, "Order", payment.PayPalOrderStatus, order.OrderDate,
                order.Total());
            Add(payment.AuthorizationId, "Authorization", payment.AuthorizationStatus,
                payment.AuthorizationTime, payment.AuthorizedAmount);
            Add(payment.CaptureId, "Capture", payment.CaptureStatus, payment.CaptureTime,
                payment.CapturedAmount);
            foreach (var refund in payment.Refunds)
            {
                Add(refund.PayPalRefundId, "Refund", refund.Status,
                    refund.UpdatedAt ?? refund.CreatedAt, refund.Amount);
            }

            void Add(string? externalId, string kind, string? status, DateTimeOffset? occurredAt,
                decimal? amount)
            {
                if (string.IsNullOrWhiteSpace(externalId) || !occurredAt.HasValue ||
                    occurredAt.Value < from || occurredAt.Value > to)
                {
                    return;
                }

                result.Add(new ReconciliationEShopItem
                {
                    OrderId = order.Id,
                    Kind = kind,
                    ExternalId = externalId,
                    Status = status ?? string.Empty,
                    OccurredAt = occurredAt,
                    Amount = amount ?? 0,
                    Currency = payment.Currency,
                    InvoiceId = payment.InvoiceId,
                    MatchStatus = "EShopOnly"
                });
            }
        }

        return result.OrderBy(x => x.OccurredAt).ToList();
    }

    private static ApiProblemException BadRequest(string detail) => new(400, "Invalid request", detail);
    private static ApiProblemException NotFound(string detail) => new(404, "Not found", detail);
    private static ApiProblemException Conflict(string detail) => new(409, "Payment state conflict", detail);
}
