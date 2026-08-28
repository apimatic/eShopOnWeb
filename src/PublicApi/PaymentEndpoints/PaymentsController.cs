using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.Infrastructure.Payments;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class PaymentsController : ControllerBase
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly Regex RefundKeyPattern = new("^[A-Za-z0-9._:-]{1,108}$", RegexOptions.Compiled);
    private static readonly Regex ExpiryPattern = new("^[0-9]{4}-(0[1-9]|1[0-2])$", RegexOptions.Compiled);

    private readonly CatalogContext _db;
    private readonly IPayPalClient _paypal;
    private readonly PayPalOptions _options;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(CatalogContext db, IPayPalClient paypal, IOptions<PayPalOptions> options,
        ILogger<PaymentsController> logger)
    {
        _db = db;
        _paypal = paypal;
        _options = options.Value;
        _logger = logger;
    }

    [HttpPost("api/orders")]
    public async Task<ActionResult<OrderCreatedResponse>> PlaceOrder(PlaceOrderRequest request,
        CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        if (request.Items.Count == 0) return ValidationProblem("At least one catalog item is required.");
        if (request.Items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0))
            return ValidationProblem("Catalog item IDs and quantities must be positive.");
        if (request.ShippingAddress == null || HasBlankAddress(request.ShippingAddress))
            return ValidationProblem("A complete shipping address is required.");

        _options.EnsureValid();
        var requested = request.Items.GroupBy(x => x.CatalogItemId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantity));
        if (requested.Values.Any(x => x > 999)) return ValidationProblem("An item quantity cannot exceed 999.");

        var catalogItems = await _db.CatalogItems.AsNoTracking()
            .Where(x => requested.Keys.Contains(x.Id)).ToListAsync(cancellationToken);
        var missingIds = requested.Keys.Except(catalogItems.Select(x => x.Id)).ToArray();
        if (missingIds.Length > 0)
            return ValidationProblem($"Catalog item(s) not found: {string.Join(", ", missingIds)}.");

        var items = catalogItems.Select(item => new OrderItem(
            new CatalogItemOrdered(item.Id, item.Name, item.PictureUri), item.Price, requested[item.Id])).ToList();
        var address = request.ShippingAddress;
        var order = new Order(buyerId,
            new Address(address.Street, address.City, address.State, address.Country, address.PostalCode), items);
        order.InitializePayment(_options.Currency);
        if (order.Total() <= 0m || decimal.Round(order.Total(), 2) != order.Total())
            return ValidationProblem("The catalog-derived order total must be positive and have at most two decimal places.");

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return Created($"/api/orders/{order.Id}",
            new OrderCreatedResponse(order.Id, order.Status.ToString(), order.Total(), order.Payment!.Currency));
    }

    [HttpPost("api/orders/{orderId:int}/pay")]
    public async Task<ActionResult<OrderResponse>> Pay(int orderId, PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrder(orderId, cancellationToken);
            if (order == null || order.BuyerId != BuyerId()) return NotFound();
            if (order.Payment == null) return ConflictProblem("payment_not_initialized", "This legacy order has no payment.");
            if (order.Status == OrderStatus.Authorized) return Ok(ToResponse(order));
            if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.AuthorizationExpired))
                return ConflictProblem("order_not_payable", $"An order in state '{order.Status}' cannot be paid.");
            if ((request.Card == null) == !request.PaymentMethodId.HasValue)
                return ValidationProblem("Supply exactly one of card or paymentMethodId.");

            string sourceType;
            int? paymentMethodId = null;
            string? vaultId = null;
            PayPalCard? card = null;
            if (request.PaymentMethodId.HasValue)
            {
                var saved = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
                    x.Id == request.PaymentMethodId.Value && x.BuyerId == order.BuyerId && x.DeletedAt == null,
                    cancellationToken);
                if (saved == null) return NotFound();
                sourceType = "SavedCard";
                paymentMethodId = saved.Id;
                vaultId = saved.PayPalTokenId;
            }
            else
            {
                var validation = ValidateCard(request.Card!);
                if (validation != null) return ValidationProblem(validation);
                sourceType = "Card";
                card = ToPayPalCard(request.Card!);
            }

            var payment = order.Payment;
            var authorization = payment.CurrentAuthorization;
            if (authorization == null || order.Status == OrderStatus.AuthorizationExpired ||
                string.Equals(authorization.PayPalAuthorizationStatus, "DENIED", StringComparison.OrdinalIgnoreCase))
            {
                authorization = payment.BeginAuthorization(sourceType, paymentMethodId);
                await _db.SaveChangesAsync(cancellationToken);
            }

            try
            {
                if (string.IsNullOrWhiteSpace(authorization.PayPalOrderId))
                {
                    var paypalOrder = await _paypal.CreateOrderAsync(order.Total(), payment.Currency,
                        authorization.ExternalReference, authorization.CreateOrderRequestId, cancellationToken);
                    authorization.RecordOrder(paypalOrder.Id, paypalOrder.Status);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                var result = await _paypal.AuthorizeOrderAsync(authorization.PayPalOrderId!, card,
                    vaultId, authorization.AuthorizeRequestId, cancellationToken);
                EnsureMoneyMatches(order.Total(), payment.Currency, result.Amount, result.Currency, "authorization");
                authorization.RecordAuthorization(result.OrderStatus, result.AuthorizationId,
                    result.AuthorizationStatus, result.Amount, result.Currency, result.CreatedAt, result.ExpiresAt);
                if (!string.Equals(result.AuthorizationStatus, "CREATED", StringComparison.OrdinalIgnoreCase))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return ConflictProblem("authorization_not_held",
                        $"PayPal returned authorization status '{result.AuthorizationStatus}'. No fulfilment capture can proceed.");
                }
                order.MarkAuthorized();
                await _db.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse(order));
            }
            catch (PayPalApiException ex)
            {
                if ((int)ex.StatusCode < 500 && ex.StatusCode != HttpStatusCode.TooManyRequests)
                {
                    authorization.RecordStatus("DENIED");
                    await _db.SaveChangesAsync(cancellationToken);
                }
                return PayPalProblem(ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("api/orders/{orderId:int}/fulfil")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Fulfil(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrder(orderId, cancellationToken);
            if (order == null) return NotFound();
            if (order.Payment == null) return ConflictProblem("payment_not_initialized", "This legacy order has no payment.");
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return Ok(ToResponse(order));
            if (order.Status != OrderStatus.Authorized)
                return ConflictProblem("order_not_authorized", $"An order in state '{order.Status}' cannot be fulfilled.");

            var payment = order.Payment;
            var authorization = payment.CurrentAuthorization;
            if (authorization?.PayPalAuthorizationId == null || !authorization.AuthorizedAt.HasValue)
                return ConflictProblem("authorization_missing", "The PayPal authorization ID or timestamp is missing.");

            try
            {
                var age = DateTimeOffset.UtcNow - authorization.AuthorizedAt.Value;
                if (age >= TimeSpan.FromDays(29))
                {
                    order.MarkAuthorizationExpired();
                    await _db.SaveChangesAsync(cancellationToken);
                    return ConflictProblem("authorization_cannot_be_renewed",
                        "The PayPal authorization is outside its reauthorization window. Ask the shopper to call the pay endpoint again with a card or saved paymentMethodId.");
                }
                if (age > TimeSpan.FromDays(3))
                {
                    var renewed = await _paypal.ReauthorizeAsync(authorization.PayPalAuthorizationId,
                        order.Total(), payment.Currency, authorization.ReauthorizeRequestId, cancellationToken);
                    EnsureMoneyMatches(order.Total(), payment.Currency, renewed.Amount, renewed.Currency, "reauthorization");
                    authorization.RecordReauthorization(renewed.AuthorizationId, renewed.AuthorizationStatus,
                        renewed.Amount, renewed.Currency, renewed.CreatedAt, renewed.ExpiresAt);
                    await _db.SaveChangesAsync(cancellationToken);
                }

                PayPalCaptureResult capture;
                if (payment.PayPalCaptureId == null)
                {
                    capture = await _paypal.CaptureAsync(authorization.PayPalAuthorizationId!, order.Total(),
                        payment.Currency, payment.CaptureRequestId, cancellationToken);
                }
                else
                {
                    capture = await _paypal.GetCaptureAsync(payment.PayPalCaptureId, cancellationToken);
                }
                EnsureMoneyMatches(order.Total(), payment.Currency, capture.Amount, capture.Currency, "capture");
                payment.RecordCapture(capture.Id, capture.Status, capture.Amount, capture.Fee,
                    capture.NetAmount, capture.CreatedAt);
                if (!string.Equals(capture.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                {
                    await _db.SaveChangesAsync(cancellationToken);
                    return ConflictProblem("capture_not_completed",
                        $"PayPal capture '{capture.Id}' is '{capture.Status}'. Retry fulfilment after resolving that PayPal status; the order was not marked fulfilled.");
                }
                authorization.RecordStatus("CAPTURED");
                order.MarkFulfilled();
                await _db.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse(order));
            }
            catch (PayPalApiException ex)
            {
                return PayPalProblem(ex, "PayPal could not renew or capture this authorization. Review the PayPal issue and ask the shopper to re-authorize if the authorization is no longer renewable.");
            }
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("api/orders/{orderId:int}/cancel")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<OrderResponse>> Cancel(int orderId, CancellationToken cancellationToken)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrder(orderId, cancellationToken);
            if (order == null) return NotFound();
            if (order.Status == OrderStatus.Cancelled) return Ok(ToResponse(order));
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return ConflictProblem("order_already_captured", "A captured order must be refunded, not cancelled.");
            if (order.Payment == null) return ConflictProblem("payment_not_initialized", "This legacy order has no payment.");

            try
            {
                var authorization = order.Payment.CurrentAuthorization;
                if (authorization?.PayPalAuthorizationId != null && order.Status == OrderStatus.Authorized)
                {
                    await _paypal.VoidAsync(authorization.PayPalAuthorizationId,
                        order.Payment.VoidRequestId, cancellationToken);
                    order.Payment.RecordVoid("VOIDED");
                }
                order.MarkCancelled();
                await _db.SaveChangesAsync(cancellationToken);
                return Ok(ToResponse(order));
            }
            catch (PayPalApiException ex)
            {
                return PayPalProblem(ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpPost("api/orders/{orderId:int}/refunds")]
    public async Task<ActionResult<RefundCreatedResponse>> Refund(int orderId,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromBody] RefundOrderRequest? request, CancellationToken cancellationToken)
    {
        if (idempotencyKey == null || !RefundKeyPattern.IsMatch(idempotencyKey))
            return ValidationProblem("Idempotency-Key is required and must be 1-108 letters, digits, dots, underscores, colons, or hyphens.");

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrder(orderId, cancellationToken);
            if (order == null || order.BuyerId != BuyerId()) return NotFound();
            var payment = order.Payment;
            if (payment == null) return ConflictProblem("payment_not_initialized", "This legacy order has no payment.");

            var existing = payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing != null)
                return Ok(new RefundCreatedResponse(existing.PayPalRefundId, existing.Status, existing.Amount,
                    payment.RefundedAmount, payment.RefundableAmount, payment.Currency));
            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
                return ConflictProblem("order_not_refundable", $"An order in state '{order.Status}' cannot be refunded.");
            if (payment.PayPalCaptureId == null)
                return ConflictProblem("capture_missing", "The PayPal capture ID is missing.");

            var amount = request?.Amount ?? payment.RefundableAmount;
            if (amount <= 0m || decimal.Round(amount, 2) != amount || amount > payment.RefundableAmount)
                return ValidationProblem($"Refund amount must be positive, have at most two decimal places, and not exceed {payment.RefundableAmount:0.00} {payment.Currency}.");

            try
            {
                var result = await _paypal.RefundAsync(payment.PayPalCaptureId, amount,
                    payment.Currency, idempotencyKey, cancellationToken);
                EnsureMoneyMatches(amount, payment.Currency, result.Amount, result.Currency, "refund");
                var refund = payment.RecordRefund(idempotencyKey, result.Id, result.Status,
                    result.Amount, result.CreatedAt);
                if (payment.RefundableAmount == 0m) order.MarkRefunded();
                else order.MarkPartiallyRefunded();
                await _db.SaveChangesAsync(cancellationToken);
                return Created($"/api/orders/{orderId}/refunds/{refund.PayPalRefundId}",
                    new RefundCreatedResponse(refund.PayPalRefundId, refund.Status, refund.Amount,
                        payment.RefundedAmount, payment.RefundableAmount, payment.Currency));
            }
            catch (PayPalApiException ex)
            {
                return PayPalProblem(ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    [HttpGet("api/my-orders")]
    public async Task<ActionResult<IReadOnlyCollection<OrderResponse>>> MyOrders(CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var orders = await OrderQuery().AsNoTracking().Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return Ok(orders.Select(ToResponse).ToArray());
    }

    [HttpPost("api/payment-methods")]
    public async Task<ActionResult<PaymentMethodCreatedResponse>> SavePaymentMethod(
        SavePaymentMethodRequest request, CancellationToken cancellationToken)
    {
        if (request.Card == null) return ValidationProblem("Card is required.");
        var validation = ValidateCard(request.Card);
        if (validation != null) return ValidationProblem(validation);
        var buyerId = BuyerId();
        var merchantCustomerId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId)))
            .ToLowerInvariant();
        try
        {
            var saved = await _paypal.SaveCardAsync(ToPayPalCard(request.Card), merchantCustomerId,
                $"eshop-setup-{Guid.NewGuid():N}", $"eshop-token-{Guid.NewGuid():N}", cancellationToken);
            var method = new SavedPaymentMethod(buyerId, saved.TokenId, saved.CustomerId,
                saved.Brand, saved.LastDigits, saved.Expiry);
            _db.SavedPaymentMethods.Add(method);
            await _db.SaveChangesAsync(cancellationToken);
            return Created($"/api/payment-methods/{method.Id}",
                new PaymentMethodCreatedResponse(method.Id, method.Brand, method.LastDigits, method.Expiry));
        }
        catch (PayPalApiException ex)
        {
            return PayPalProblem(ex);
        }
    }

    [HttpGet("api/payment-methods")]
    public async Task<ActionResult<IReadOnlyCollection<PaymentMethodResponse>>> PaymentMethods(
        CancellationToken cancellationToken)
    {
        var buyerId = BuyerId();
        var methods = await _db.SavedPaymentMethods.AsNoTracking()
            .Where(x => x.BuyerId == buyerId && x.DeletedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new PaymentMethodResponse(x.Id, x.Brand, x.LastDigits, x.Expiry, x.CreatedAt))
            .ToListAsync(cancellationToken);
        return Ok(methods);
    }

    [HttpDelete("api/payment-methods/{paymentMethodId:int}")]
    public async Task<IActionResult> DeletePaymentMethod(int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await _db.SavedPaymentMethods.SingleOrDefaultAsync(x =>
            x.Id == paymentMethodId && x.BuyerId == BuyerId() && x.DeletedAt == null, cancellationToken);
        if (method == null) return NotFound();
        try
        {
            await _paypal.DeletePaymentTokenAsync(method.PayPalTokenId, cancellationToken);
            method.Delete();
            await _db.SaveChangesAsync(cancellationToken);
            return NoContent();
        }
        catch (PayPalApiException ex)
        {
            return PayPalProblem(ex);
        }
    }

    [HttpGet("api/reconciliation")]
    [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
        AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public async Task<ActionResult<ReconciliationResponse>> Reconciliation([FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from == default || to == default || to <= from)
            return ValidationProblem("from and to must be valid ISO-8601 date-times, and to must be after from.");
        try
        {
            var paypalTransactions = await _paypal.SearchTransactionsAsync(from, to, cancellationToken);
            var orders = await OrderQuery().AsNoTracking().Where(x => x.Payment != null &&
                ((x.OrderDate >= from && x.OrderDate <= to) ||
                 (x.Payment!.LastActivityAt >= from && x.Payment.LastActivityAt <= to) ||
                 x.Payment.Authorizations.Any(a => a.AuthorizedAt >= from && a.AuthorizedAt <= to) ||
                 x.Payment.Refunds.Any(r => r.CreatedAt >= from && r.CreatedAt <= to)))
                .ToListAsync(cancellationToken);

            var entries = new List<ReconciliationEntry>();
            var matchedOrderIds = new HashSet<int>();
            foreach (var transaction in paypalTransactions)
            {
                var order = orders.FirstOrDefault(x => TransactionMatches(x, transaction));
                if (order != null) matchedOrderIds.Add(order.Id);
                entries.Add(new ReconciliationEntry(order == null ? "PayPalOnly" : "Matched",
                    order?.Id, order?.Status.ToString(), transaction.TransactionId,
                    transaction.PayPalReferenceId, transaction.EventCode, transaction.Status,
                    transaction.InitiatedAt, transaction.Amount, transaction.Fee, transaction.Currency,
                    order?.Payment?.Authorizations.FirstOrDefault(a =>
                        a.ExternalReference == transaction.InvoiceId || a.ExternalReference == transaction.CustomField)
                        ?.ExternalReference));
            }

            foreach (var order in orders.Where(x => !matchedOrderIds.Contains(x.Id)))
            {
                entries.Add(new ReconciliationEntry("EShopOnly", order.Id, order.Status.ToString(),
                    null, null, null, null, null, null, null, order.Payment!.Currency,
                    order.Payment.CurrentAuthorization?.ExternalReference));
            }
            return Ok(new ReconciliationResponse(from, to, paypalTransactions.Count, orders.Count, entries));
        }
        catch (PayPalApiException ex)
        {
            return PayPalProblem(ex);
        }
    }

    private IQueryable<Order> OrderQuery() => _db.Orders
        .Include(x => x.OrderItems).ThenInclude(x => x.ItemOrdered)
        .Include(x => x.Payment).ThenInclude(x => x!.Authorizations)
        .Include(x => x.Payment).ThenInclude(x => x!.Refunds);

    private Task<Order?> LoadOrder(int orderId, CancellationToken cancellationToken) =>
        OrderQuery().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);

    private string BuyerId() => User.Identity?.Name
        ?? throw new InvalidOperationException("The authenticated token has no name claim.");

    private static OrderResponse ToResponse(Order order)
    {
        PaymentResponse? payment = null;
        if (order.Payment != null)
        {
            var current = order.Payment.CurrentAuthorization;
            var authorization = current == null ? null : new AuthorizationResponse(
                current.PayPalOrderId, current.PayPalOrderStatus, current.PayPalAuthorizationId,
                current.PayPalAuthorizationStatus, current.AuthorizedAmount, current.AuthorizedAt,
                current.ExpiresAt);
            payment = new PaymentResponse(order.Payment.Currency, authorization,
                order.Payment.PayPalCaptureId, order.Payment.PayPalCaptureStatus,
                order.Payment.CapturedAmount, order.Payment.PayPalFee, order.Payment.NetAmount,
                order.Payment.RefundedAmount, order.Payment.RefundableAmount,
                order.Payment.Refunds.Select(x => new RefundResponse(x.PayPalRefundId, x.Status,
                    x.Amount, x.CreatedAt)).ToArray());
        }
        return new OrderResponse(order.Id, order.OrderDate, order.Status.ToString(), order.Total(),
            order.OrderItems.Select(x => new OrderItemResponse(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToArray(), payment);
    }

    private static bool TransactionMatches(Order order, PayPalTransaction transaction)
    {
        var payment = order.Payment!;
        if (payment.PayPalCaptureId == transaction.TransactionId) return true;
        if (payment.Refunds.Any(x => x.PayPalRefundId == transaction.TransactionId)) return true;
        return payment.Authorizations.Any(x => x.PayPalAuthorizationId == transaction.TransactionId ||
            x.PayPalOrderId == transaction.TransactionId || x.PayPalOrderId == transaction.PayPalReferenceId ||
            x.ExternalReference == transaction.InvoiceId || x.ExternalReference == transaction.CustomField);
    }

    private static PayPalCard ToPayPalCard(CardRequest card) => new(card.Name,
        new string(card.Number.Where(char.IsDigit).ToArray()), card.Expiry, card.SecurityCode,
        new PayPalBillingAddress(card.BillingAddress!.AddressLine1, card.BillingAddress.AddressLine2,
            card.BillingAddress.City, card.BillingAddress.State, card.BillingAddress.PostalCode,
            card.BillingAddress.CountryCode));

    private static string? ValidateCard(CardRequest card)
    {
        var digits = new string(card.Number.Where(char.IsDigit).ToArray());
        if (string.IsNullOrWhiteSpace(card.Name) || digits.Length is < 13 or > 19 ||
            !card.SecurityCode.All(char.IsDigit) || card.SecurityCode.Length is < 3 or > 4 ||
            !ExpiryPattern.IsMatch(card.Expiry))
            return "Card name, a 13-19 digit number, YYYY-MM expiry, and 3-4 digit security code are required.";
        if (!DateTime.TryParseExact(card.Expiry + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var expiry) || expiry.AddMonths(1) <= DateTime.UtcNow.Date)
            return "Card expiry must be in the future.";
        if (card.BillingAddress == null || string.IsNullOrWhiteSpace(card.BillingAddress.AddressLine1) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.City) ||
            string.IsNullOrWhiteSpace(card.BillingAddress.PostalCode) ||
            card.BillingAddress.CountryCode.Length != 2)
            return "Billing address line 1, city, postal code, and a two-letter countryCode are required.";
        return null;
    }

    private static bool HasBlankAddress(ShippingAddressRequest address) =>
        string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City) ||
        string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.PostalCode);

    private static void EnsureMoneyMatches(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (expectedAmount != actualAmount ||
            !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"PayPal {operation} amount did not match the order total and currency.");
    }

    private ObjectResult ValidationProblem(string detail) => Problem(statusCode: StatusCodes.Status400BadRequest,
        title: "Invalid request", detail: detail);

    private static ObjectResult ConflictProblem(string code, string detail)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Payment state conflict",
            Detail = detail
        };
        problem.Extensions["code"] = code;
        return new ObjectResult(problem) { StatusCode = problem.Status };
    }

    private ObjectResult PayPalProblem(PayPalApiException exception, string? operatorDetail = null)
    {
        _logger.LogWarning("PayPal operation failed with {Name} and debug ID {DebugId}.",
            exception.Name, exception.DebugId);
        var status = exception.RequiresPayerAction ? StatusCodes.Status422UnprocessableEntity :
            (int)exception.StatusCode is >= 400 and < 500 ? (int)exception.StatusCode : StatusCodes.Status502BadGateway;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = exception.Name,
            Detail = operatorDetail == null ? exception.Message :
                $"{operatorDetail} PayPal said: {exception.Message}"
        };
        problem.Extensions["debugId"] = exception.DebugId;
        problem.Extensions["issues"] = exception.Issues;
        return new ObjectResult(problem) { StatusCode = status };
    }
}
