using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentApplicationService : IPaymentApplicationService
{
    private readonly CatalogContext _context;
    private readonly IPayPalGateway _payPal;
    private readonly PaymentOperationLock _operationLock;
    private readonly string _currency;

    public PaymentApplicationService(CatalogContext context, IPayPalGateway payPal,
        PaymentOperationLock operationLock, IOptions<PayPalOptions> options)
    {
        _context = context;
        _payPal = payPal;
        _operationLock = operationLock;
        _currency = options.Value.Currency.ToUpperInvariant();
    }

    public async Task<OrderView> CreateOrderAsync(string buyerId, IReadOnlyCollection<CreateOrderLine> items,
        ShippingAddressData shippingAddress, CancellationToken cancellationToken)
    {
        if (items.Count == 0) throw Workflow(HttpStatusCode.BadRequest, "empty_order", "At least one item is required.");
        if (items.Count > 100) throw Workflow(HttpStatusCode.BadRequest, "too_many_items", "An order can contain at most 100 lines.");
        if (items.Any(x => x.CatalogItemId <= 0 || x.Quantity <= 0 || x.Quantity > 100))
            throw Workflow(HttpStatusCode.BadRequest, "invalid_item", "Catalog item IDs and quantities must be positive; quantity cannot exceed 100.");
        if (items.GroupBy(x => x.CatalogItemId).Any(x => x.Count() > 1))
            throw Workflow(HttpStatusCode.BadRequest, "duplicate_item", "Each catalog item may appear only once.");

        var ids = items.Select(x => x.CatalogItemId).ToArray();
        var catalogItems = await _context.CatalogItems.Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        if (catalogItems.Count != ids.Length)
        {
            var missing = ids.Except(catalogItems.Select(x => x.Id));
            throw Workflow(HttpStatusCode.BadRequest, "catalog_item_not_found",
                $"Catalog item(s) {string.Join(", ", missing)} do not exist.");
        }

        var orderItems = items.Select(line =>
        {
            var catalog = catalogItems.Single(x => x.Id == line.CatalogItemId);
            var snapshot = new CatalogItemOrdered(catalog.Id, catalog.Name, catalog.PictureUri);
            return new OrderItem(snapshot, decimal.Round(catalog.Price, 2), line.Quantity);
        }).ToList();
        var address = new Address(shippingAddress.Street, shippingAddress.City, shippingAddress.State,
            shippingAddress.Country, shippingAddress.ZipCode);
        var order = new Order(buyerId, address, orderItems);
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return ToView(order);
    }

    public async Task<OrderView> PayAsync(string buyerId, int orderId, PaymentCardData? card,
        int? paymentMethodId, CancellationToken cancellationToken)
    {
        if ((card is null) == (paymentMethodId is null))
            throw Workflow(HttpStatusCode.BadRequest, "payment_source_required",
                "Supply either card or paymentMethodId, but not both.");

        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.Status == OrderStatus.Authorized) return ToView(order);
            if (order.Status != OrderStatus.AwaitingPayment)
                throw Workflow(HttpStatusCode.Conflict, "invalid_order_state", $"Order {orderId} cannot be paid while it is {order.Status}.");

            string? vaultId = null;
            if (paymentMethodId is not null)
            {
                var method = await _context.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId
                    && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
                if (method is null)
                    throw Workflow(HttpStatusCode.NotFound, "payment_method_not_found", "The saved payment method was not found.");
                vaultId = method.VaultId;
            }

            var payment = order.StartPayment(_currency, $"eshop-pay-{Guid.NewGuid():N}");
            await _context.SaveChangesAsync(cancellationToken);

            PayPalAuthorizationResult authorization;
            try
            {
                authorization = await _payPal.AuthorizeAsync(order.Id, order.Total(), _currency,
                    card, vaultId, payment.AuthorizationRequestId, cancellationToken);
            }
            catch (PayPalPayerActionRequiredException ex)
            {
                throw Workflow(HttpStatusCode.UnprocessableEntity, "payer_action_required", ex.Message, ex);
            }
            catch (PayPalException ex)
            {
                throw ProviderFailure("authorization_failed", "PayPal could not authorize this order.", ex);
            }

            EnsureAmount(order.Total(), _currency, authorization.Amount, authorization.Currency, "authorization");
            if (authorization.Status is not ("CREATED" or "PENDING"))
                throw Workflow(HttpStatusCode.UnprocessableEntity, "authorization_not_approved",
                    $"PayPal reported authorization status {authorization.Status}.");
            order.MarkAuthorized(authorization.PayPalOrderId, authorization.AuthorizationId,
                authorization.Status, authorization.CreatedAt, authorization.ExpiresAt);
            await _context.SaveChangesAsync(cancellationToken);
            return ToView(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                return ToView(order);
            if (order.Status != OrderStatus.Authorized || order.Payment?.AuthorizationId is null)
                throw Workflow(HttpStatusCode.Conflict, "order_not_authorized",
                    $"Order {orderId} must have an active authorization before fulfilment.");

            PayPalAuthorizationResult current;
            try
            {
                current = await _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
            }
            catch (PayPalException ex)
            {
                throw ProviderFailure("authorization_status_unavailable",
                    "PayPal authorization status could not be checked; retry fulfilment before shipping.", ex);
            }

            if (current.Status is "DENIED" or "EXPIRED" or "VOIDED")
                throw Workflow(HttpStatusCode.Conflict, "authorization_cannot_be_renewed",
                    $"PayPal reports the authorization as {current.Status}. Ask the shopper to pay again with a new order before fulfilment.");

            var authorizedAt = current.CreatedAt;
            var outsideHonorPeriod = authorizedAt <= DateTimeOffset.UtcNow.AddDays(-3);
            if (outsideHonorPeriod)
            {
                try
                {
                    current = await _payPal.ReauthorizeAsync(current.AuthorizationId, order.Total(), _currency,
                        ReauthorizationRequestId(order.Payment.AuthorizationRequestId, current.AuthorizationId), cancellationToken);
                    EnsureAmount(order.Total(), _currency, current.Amount, current.Currency, "reauthorization");
                    order.MarkReauthorized(current.AuthorizationId, current.Status, current.CreatedAt, current.ExpiresAt);
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch (PayPalException ex)
                {
                    throw Workflow(HttpStatusCode.Conflict, "authorization_cannot_be_renewed",
                        $"PayPal could not renew the stale authorization. Ask the shopper to pay again before shipping. {ex.Message}", ex);
                }
            }

            PayPalCaptureResult capture;
            try
            {
                capture = await _payPal.CaptureAsync(current.AuthorizationId, order.Id, order.Total(),
                    _currency, CaptureRequestId(order.Payment.AuthorizationRequestId), cancellationToken);
            }
            catch (PayPalException ex)
            {
                throw ProviderFailure("capture_failed", "PayPal did not capture the authorization; do not ship and retry fulfilment.", ex);
            }

            EnsureAmount(order.Total(), _currency, capture.Amount, capture.Currency, "capture");
            if (capture.Status is not ("COMPLETED" or "PENDING"))
                throw Workflow(HttpStatusCode.Conflict, "capture_not_completed",
                    $"PayPal reported capture status {capture.Status}; do not ship.");
            order.MarkFulfilled(capture.CaptureId, capture.Status, capture.Amount,
                capture.PayPalFee, capture.NetAmount, capture.CreatedAt);
            await _context.SaveChangesAsync(cancellationToken);
            return ToView(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<OrderView> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            if (order.Status == OrderStatus.Cancelled) return ToView(order);
            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
                throw Workflow(HttpStatusCode.Conflict, "already_fulfilled", "A fulfilled order must be refunded, not cancelled.");

            if (order.Payment?.AuthorizationId is null)
            {
                order.MarkCancelledWithoutAuthorization();
            }
            else
            {
                try
                {
                    var current = await _payPal.GetAuthorizationAsync(order.Payment.AuthorizationId, cancellationToken);
                    if (current.Status != "VOIDED")
                        await _payPal.VoidAsync(order.Payment.AuthorizationId, cancellationToken);
                    order.MarkCancelled("VOIDED");
                }
                catch (PayPalException ex)
                {
                    throw ProviderFailure("void_failed", "PayPal could not release the authorization; retry cancellation.", ex);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
            return ToView(order);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundResultView> RefundAsync(string buyerId, int orderId, decimal? amount,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 108
            || idempotencyKey.Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_' or '.')))
            throw Workflow(HttpStatusCode.BadRequest, "invalid_idempotency_key",
                "idempotencyKey must contain 1 to 108 ASCII letters, digits, hyphens, underscores, or periods.");

        var gate = _operationLock.For(orderId);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await FindOrderAsync(orderId, cancellationToken);
            EnsureOwner(order, buyerId);
            if (order.Payment?.CaptureId is null)
                throw Workflow(HttpStatusCode.Conflict, "order_not_refundable", "Only a captured order with refundable funds can be refunded.");

            var existing = order.Payment.Refunds.SingleOrDefault(x => x.IdempotencyKey == idempotencyKey);
            if (existing?.PayPalRefundId is not null)
            {
                if (amount is not null && decimal.Round(amount.Value, 2) != existing.Amount)
                    throw Workflow(HttpStatusCode.Conflict, "idempotency_key_reused",
                        "This idempotency key was already used for a different refund amount.");
                return new RefundResultView(existing.PayPalRefundId, existing.Status, existing.Amount,
                    order.Payment.CapturedAmount - order.Payment.RefundedAmount);
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded))
                throw Workflow(HttpStatusCode.Conflict, "order_not_refundable", "Only a captured order with refundable funds can be refunded.");

            var remaining = order.Payment.CapturedAmount - order.Payment.RefundedAmount;
            var requested = existing?.Amount ?? decimal.Round(amount ?? remaining, 2);
            if (requested <= 0 || requested > remaining)
                throw Workflow(HttpStatusCode.BadRequest, "refund_amount_exceeded",
                    $"Refund amount must be positive and cannot exceed {remaining:0.00} {_currency}.");

            var refund = existing ?? order.Payment.StartRefund(idempotencyKey, requested);
            await _context.SaveChangesAsync(cancellationToken);

            PayPalRefundResult result;
            try
            {
                result = await _payPal.RefundAsync(order.Payment.CaptureId, requested, _currency,
                    idempotencyKey, cancellationToken);
            }
            catch (PayPalException ex)
            {
                throw ProviderFailure("refund_failed", "PayPal did not complete the refund; retry with the same idempotency key.", ex);
            }

            EnsureAmount(requested, _currency, result.Amount, result.Currency, "refund");
            refund.Complete(result.RefundId, result.Status, result.CreatedAt);
            order.ApplyRefund(refund);
            await _context.SaveChangesAsync(cancellationToken);
            return new RefundResultView(result.RefundId, result.Status, result.Amount,
                order.Payment.CapturedAmount - order.Payment.RefundedAmount);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderView>> GetOrdersAsync(string buyerId, CancellationToken cancellationToken)
    {
        var orders = await OrderQuery().Where(x => x.BuyerId == buyerId)
            .OrderByDescending(x => x.OrderDate).ToListAsync(cancellationToken);
        return orders.Select(ToView).ToList();
    }

    public async Task<PaymentMethodView> SavePaymentMethodAsync(string buyerId, PaymentCardData card,
        CancellationToken cancellationToken)
    {
        var customerId = await _context.PaymentMethods.Where(x => x.BuyerId == buyerId && x.PayPalCustomerId != null)
            .Select(x => x.PayPalCustomerId).FirstOrDefaultAsync(cancellationToken);
        PayPalVaultResult saved;
        try
        {
            saved = await _payPal.SaveCardAsync(card, MerchantCustomerId(buyerId), customerId,
                "eshop-vault-" + Guid.NewGuid().ToString("N"), cancellationToken);
        }
        catch (PayPalPayerActionRequiredException ex)
        {
            throw Workflow(HttpStatusCode.UnprocessableEntity, "payer_action_required", ex.Message, ex);
        }
        catch (PayPalException ex)
        {
            throw ProviderFailure("vault_failed", "PayPal could not save this card.", ex);
        }

        var method = new PaymentMethod(buyerId, saved.VaultId, saved.CustomerId,
            saved.Brand, saved.Last4, saved.Expiry);
        _context.PaymentMethods.Add(method);
        await _context.SaveChangesAsync(cancellationToken);
        return ToView(method);
    }

    public async Task<IReadOnlyList<PaymentMethodView>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken)
        => await _context.PaymentMethods.Where(x => x.BuyerId == buyerId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAt).Select(x => new PaymentMethodView(x.Id, x.Brand, x.Last4, x.Expiry))
            .ToListAsync(cancellationToken);

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId, CancellationToken cancellationToken)
    {
        var method = await _context.PaymentMethods.SingleOrDefaultAsync(x => x.Id == paymentMethodId
            && x.BuyerId == buyerId && !x.IsDeleted, cancellationToken);
        if (method is null) throw Workflow(HttpStatusCode.NotFound, "payment_method_not_found", "The saved payment method was not found.");
        try
        {
            await _payPal.DeletePaymentTokenAsync(method.VaultId, cancellationToken);
        }
        catch (PayPalException ex)
        {
            throw ProviderFailure("vault_delete_failed", "PayPal could not remove this saved card; retry deletion.", ex);
        }
        method.Delete();
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ReconciliationView> ReconcileAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        if (from >= to) throw Workflow(HttpStatusCode.BadRequest, "invalid_date_range", "from must be earlier than to.");
        IReadOnlyList<PayPalTransaction> paypal;
        try
        {
            paypal = await _payPal.ListTransactionsAsync(from, to, cancellationToken);
        }
        catch (PayPalException ex)
        {
            throw ProviderFailure("reporting_failed", "PayPal transaction reporting could not be retrieved.", ex);
        }

        var orders = await OrderQuery().Where(x => x.Payment != null).ToListAsync(cancellationToken);
        var byAnyId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var order in orders)
        {
            AddId(byAnyId, order.Payment?.PayPalOrderId, order.Id);
            AddId(byAnyId, order.Payment?.AuthorizationId, order.Id);
            AddId(byAnyId, order.Payment?.CaptureId, order.Id);
            foreach (var refund in order.Payment?.Refunds ?? Array.Empty<PaymentRefund>())
                AddId(byAnyId, refund.PayPalRefundId, order.Id);
        }

        var paypalRows = paypal.Select(row =>
        {
            int? orderId = MatchOrder(row, byAnyId);
            return new ReconciliationPayPalRow(row.TransactionId, row.ReferenceId, row.EventCode,
                row.Status, row.InitiatedAt, row.Amount, row.Currency, row.Fee, row.InvoiceId,
                orderId, orderId is null ? "PayPalOnly" : "Matched");
        }).ToList();
        var paypalIds = paypal.SelectMany(x => new[] { x.TransactionId, x.ReferenceId })
            .Where(x => x is not null).Select(x => x!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var localRows = new List<ReconciliationLocalRow>();
        foreach (var order in orders)
        {
            var payment = order.Payment!;
            AddLocal(localRows, paypalIds, order.Id, "Authorization", payment.AuthorizationId,
                payment.AuthorizedAt, payment.Amount, payment.Currency, from, to);
            AddLocal(localRows, paypalIds, order.Id, "Capture", payment.CaptureId,
                payment.CapturedAt, payment.CapturedAmount, payment.Currency, from, to);
            foreach (var refund in payment.Refunds)
                AddLocal(localRows, paypalIds, order.Id, "Refund", refund.PayPalRefundId,
                    refund.CompletedAt, refund.Amount, payment.Currency, from, to);
        }

        return new ReconciliationView(from, to, paypalRows, localRows);
    }

    private IQueryable<Order> OrderQuery() => _context.Orders
        .Include(x => x.OrderItems)
        .Include(x => x.Payment)!.ThenInclude(x => x!.Refunds);

    private async Task<Order> FindOrderAsync(int orderId, CancellationToken cancellationToken)
        => await OrderQuery().SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken)
            ?? throw Workflow(HttpStatusCode.NotFound, "order_not_found", $"Order {orderId} was not found.");

    private OrderView ToView(Order order)
    {
        var payment = order.Payment is null ? null : new PaymentView(
            order.Payment.AuthorizationStatus,
            order.Payment.Currency,
            order.Payment.Amount,
            order.Payment.PayPalOrderId,
            order.Payment.AuthorizationId,
            order.Payment.AuthorizationExpiresAt,
            order.Payment.CaptureId,
            order.Payment.CaptureStatus,
            order.Payment.CapturedAmount,
            order.Payment.PayPalFee,
            order.Payment.NetAmount,
            order.Payment.RefundedAmount,
            order.Payment.Refunds.Select(x => new RefundView(x.PayPalRefundId ?? string.Empty,
                x.Status, x.Amount, x.CompletedAt)).ToList());
        return new OrderView(order.Id, order.OrderDate, order.Status.ToString(), order.Total(), _currency,
            order.OrderItems.Select(x => new OrderLineView(x.ItemOrdered.CatalogItemId,
                x.ItemOrdered.ProductName, x.UnitPrice, x.Units)).ToList(), payment);
    }

    private static PaymentMethodView ToView(PaymentMethod method)
        => new(method.Id, method.Brand, method.Last4, method.Expiry);

    private static void EnsureOwner(Order order, string buyerId)
    {
        if (!string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            throw Workflow(HttpStatusCode.NotFound, "order_not_found", $"Order {order.Id} was not found.");
    }

    private static void EnsureAmount(decimal expectedAmount, string expectedCurrency,
        decimal actualAmount, string actualCurrency, string operation)
    {
        if (decimal.Round(expectedAmount, 2) != decimal.Round(actualAmount, 2)
            || !string.Equals(expectedCurrency, actualCurrency, StringComparison.OrdinalIgnoreCase))
            throw Workflow(HttpStatusCode.BadGateway, "provider_amount_mismatch",
                $"PayPal's {operation} amount did not match the order. Manual review is required.");
    }

    private static string MerchantCustomerId(string buyerId)
        => "ESHOP-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(buyerId)))[..32];

    private static string CaptureRequestId(string authorizationRequestId)
        => authorizationRequestId.Replace("eshop-pay-", "eshop-capture-", StringComparison.Ordinal);

    private static string ReauthorizationRequestId(string authorizationRequestId, string authorizationId)
    {
        var value = authorizationRequestId.Replace("eshop-pay-", "eshop-reauth-", StringComparison.Ordinal)
            + "-" + authorizationId;
        return value[..Math.Min(108, value.Length)];
    }

    private static PaymentWorkflowException Workflow(HttpStatusCode status, string code,
        string message, Exception? inner = null) => new((int)status, code, message, inner);

    private static PaymentWorkflowException ProviderFailure(string code, string message, PayPalException ex)
        => Workflow(HttpStatusCode.BadGateway, code, $"{message} {ex.Message}", ex);

    private static void AddId(IDictionary<string, int> index, string? id, int orderId)
    {
        if (!string.IsNullOrWhiteSpace(id)) index[id] = orderId;
    }

    private static int? MatchOrder(PayPalTransaction transaction, IReadOnlyDictionary<string, int> index)
    {
        foreach (var candidate in new[] { transaction.TransactionId, transaction.ReferenceId })
            if (candidate is not null && index.TryGetValue(candidate, out var orderId)) return orderId;
        foreach (var marker in new[] { transaction.InvoiceId, transaction.CustomField })
        {
            if (marker is null || !marker.StartsWith("ESHOP-", StringComparison.OrdinalIgnoreCase)) continue;
            var idText = marker[6..].Split('-')[0];
            if (int.TryParse(idText, out var orderId)) return orderId;
        }
        return null;
    }

    private static void AddLocal(ICollection<ReconciliationLocalRow> rows, ISet<string> paypalIds,
        int orderId, string kind, string? paypalId, DateTimeOffset? occurredAt, decimal amount,
        string currency, DateTimeOffset from, DateTimeOffset to)
    {
        if (paypalId is null || occurredAt is null || occurredAt < from || occurredAt > to) return;
        rows.Add(new ReconciliationLocalRow(orderId, kind, paypalId, occurredAt.Value, amount,
            currency, paypalIds.Contains(paypalId) ? "Matched" : "EShopOnly"));
    }
}
