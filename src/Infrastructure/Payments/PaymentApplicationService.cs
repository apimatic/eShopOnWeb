using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixAddress = FinixApiReference.Models.Address;
using FinixBuyer = FinixApiReference.Models.Buyer;
using FinixCard = FinixApiReference.Models.PaymentInstrumentCard;
using FinixIdentity = FinixApiReference.Models.Identity;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public sealed class PaymentApplicationService : IPaymentApplicationService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly FinixApiReferenceClient _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<PaymentApplicationService> _logger;

    public PaymentApplicationService(CatalogContext db, FinixApiReferenceClient finix,
        IOptions<FinixOptions> options, ILogger<PaymentApplicationService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    private string CurrencyCode => _options.Currency.Trim().ToUpperInvariant();

    private Currency FinixCurrency => CurrencyCode == "CAD" ? Currency.Cad : Currency.Usd;

    public async Task<OrderResponse> CreateOrderAsync(string buyerId, CreateOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentOperationException(401, "unauthenticated", "A signed-in shopper is required.");

        if (command.Items is null || command.Items.Count == 0)
            throw new PaymentOperationException(400, "empty_order", "An order must contain at least one catalog item.");

        var requested = command.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        if (requested.Any(kv => kv.Key <= 0 || kv.Value <= 0))
            throw new PaymentOperationException(400, "invalid_quantity",
                "Catalog item ids and quantities must be positive integers.");

        var catalogItems = await _db.CatalogItems
            .Where(c => requested.Keys.Contains(c.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != requested.Count)
        {
            var found = catalogItems.Select(c => c.Id).ToHashSet();
            var missing = requested.Keys.Where(id => !found.Contains(id));
            throw new PaymentOperationException(400, "catalog_item_not_found",
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var (catalogItemId, quantity) in requested)
        {
            var item = catalogItems.First(c => c.Id == catalogItemId);
            var ordered = new CatalogItemOrdered(item.Id, item.Name, item.PictureUri);
            orderItems.Add(new OrderItem(ordered, item.Price, quantity));
        }

        var address = BuildOrderAddress(command.ShippingAddress);
        var order = new Order(buyerId, address, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        var totalInCents = ToCents(order.Total());
        var payment = new OrderPayment(order.Id, buyerId, totalInCents, CurrencyCode);
        _db.OrderPayments.Add(payment);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created order {OrderId} for {BuyerId} awaiting payment of {Amount} {Currency}.",
            order.Id, buyerId, totalInCents, CurrencyCode);

        return MapOrder(order, payment);
    }

    public async Task<PaymentResponse> AuthorizeOrderAsync(string buyerId, int orderId,
        PayOrderCommand command, CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders
                .Include(o => o.OrderItems)
                .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

            if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
                throw new PaymentOperationException(404, "order_not_found", $"Order {orderId} was not found.");

            var payment = await GetPaymentAsync(orderId, cancellationToken);
            if (payment is null)
            {
                payment = new OrderPayment(order.Id, order.BuyerId, ToCents(order.Total()), CurrencyCode);
                _db.OrderPayments.Add(payment);
                await _db.SaveChangesAsync(cancellationToken);
            }

            if (payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured
                or PaymentStatus.PartiallyRefunded or PaymentStatus.Refunded)
            {
                return MapPayment(payment);
            }

            if (payment.Status == PaymentStatus.Canceled)
                throw new PaymentOperationException(409, "order_canceled",
                    "This order has been canceled and can no longer be paid.");

            if (command.PaymentMethodId.HasValue && command.Card is not null)
                throw new PaymentOperationException(400, "ambiguous_payment_source",
                    "Provide either card details or a saved paymentMethodId, not both.");

            string source;
            if (command.PaymentMethodId.HasValue)
            {
                var card = await _db.SavedCards.FirstOrDefaultAsync(
                    c => c.Id == command.PaymentMethodId.Value && c.BuyerId == buyerId, cancellationToken);

                if (card is null)
                    throw new PaymentOperationException(404, "payment_method_not_found",
                        $"Saved payment method {command.PaymentMethodId.Value} was not found.");

                source = card.FinixPaymentInstrumentId;
            }
            else if (command.Card is not null)
            {
                var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, command.Card.Name, cancellationToken);
                var instrument = await CreatePaymentInstrumentAsync(identityId, command.Card, cancellationToken);
                source = instrument.Id!;
            }
            else
            {
                throw new PaymentOperationException(400, "payment_source_required",
                    "Provide card details or the id of one of your saved payment methods.");
            }

            var idempotencyKey =
                $"eshop-{payment.PaymentKey}-authorize-{payment.AuthorizationAttempt}";

            Authorization? authorization = null;
            try
            {
                var body = new AuthorizationMinimalFields
                {
                    Amount = payment.AmountInCents,
                    Currency = FinixCurrency,
                    Merchant = _options.MerchantId,
                    Source = source
                };
                body.AdditionalProperties.Set("idempotency_id", idempotencyKey);
                body.AdditionalProperties.Set("tags", new Dictionary<string, string>
                {
                    ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                    ["buyer_id"] = buyerId
                });

                authorization = await _finix.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest
                    {
                        Body = AuthorizationsRequest.AuthorizationMinimalFields(body)
                    }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                authorization = await TryFindAuthorizationByIdempotencyKeyAsync(idempotencyKey,
                    cancellationToken);

                if (authorization is null)
                {
                    payment.MarkAuthorizationFailed("finix_authorization_error", DescribeFinixError(ex));
                    await _db.SaveChangesAsync(cancellationToken);
                    throw FinixFailure("Authorization", ex);
                }
            }

            if (authorization is null)
            {
                payment.MarkAuthorizationFailed("missing_authorization", "Finix did not return an authorization.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(502, "missing_authorization",
                    "Finix did not return an authorization.");
            }

            if (authorization.State == State1.Failed || authorization.State == State1.Canceled)
            {
                payment.MarkAuthorizationFailed(authorization.FailureCode,
                    authorization.FailureMessage ?? "Finix declined the card authorization.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(402, "authorization_declined",
                    authorization.FailureMessage ?? "Finix declined the card authorization.");
            }

            if (string.IsNullOrWhiteSpace(authorization.Id))
            {
                payment.MarkAuthorizationFailed("missing_authorization_id",
                    "Finix did not return an authorization id.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(502, "missing_authorization_id",
                    "Finix did not return an authorization id.");
            }

            payment.MarkAuthorized(authorization.Id, authorization.State?.Value, authorization.ExpiresAt);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Authorized order {OrderId} with Finix authorization {AuthorizationId}.",
                orderId, authorization.Id);

            return MapPayment(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentResponse> FulfilOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await GetPaymentAsync(orderId, cancellationToken);
            if (payment is null)
                throw new PaymentOperationException(404, "payment_not_found",
                    $"No payment exists for order {orderId}.");

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                return MapPayment(payment);
            }

            if (payment.Status == PaymentStatus.Canceled)
                throw new PaymentOperationException(409, "order_canceled",
                    "The order was canceled before fulfilment; there is no hold to capture.");

            if (payment.Status is PaymentStatus.AwaitingPayment or PaymentStatus.AuthorizationFailed)
                throw new PaymentOperationException(409, "payment_not_authorized",
                    "The order has no successful authorization. Ask the shopper to pay before fulfilling.");

            if (payment.Status == PaymentStatus.AuthorizationExpired)
                throw new PaymentOperationException(409, "authorization_not_capturable",
                    "The authorization can no longer be captured. Cancel the order or ask the shopper to pay again.");

            if (string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
                throw new PaymentOperationException(409, "authorization_missing",
                    "The order has no Finix authorization id. Ask the shopper to pay again.");

            var authorization = await GetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
            if (!IsCapturable(authorization, out var reason))
            {
                payment.MarkAuthorizationExpired(reason);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(409, "authorization_not_capturable", reason!);
            }

            Authorization captured;
            try
            {
                var captureBody = new CaptureAuthorization { CaptureAmount = payment.AmountInCents };
                captureBody.AdditionalProperties.Set("tags", new Dictionary<string, string>
                {
                    ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture)
                });

                captured = await _finix.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = payment.FinixAuthorizationId!,
                        Body = AuthorizationsRequest1.CaptureAuthorization(captureBody)
                    }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                var current = await TryGetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                if (current?.Transfer is { Length: > 0 })
                {
                    captured = current;
                }
                else
                {
                    if (current is not null && !IsCapturable(current, out var captureReason))
                    {
                        payment.MarkAuthorizationExpired(captureReason);
                        await _db.SaveChangesAsync(cancellationToken);
                        throw new PaymentOperationException(409, "authorization_not_capturable", captureReason!);
                    }

                    throw FinixFailure("Capture", ex);
                }
            }

            var transferId = captured.Transfer;
            if (string.IsNullOrWhiteSpace(transferId))
            {
                throw new PaymentOperationException(502, "missing_capture_transfer",
                    "Finix accepted the capture but did not return a capture transfer id. Retry fulfilment.");
            }

            var transfer = await GetTransferAsync(transferId!, cancellationToken);

            var capturedAmount = transfer.Amount ?? captured.Amount ?? payment.AmountInCents;
            var fee = transfer.Fee ?? 0;
            var net = capturedAmount - fee;

            payment.MarkCaptured(transferId!, transfer.State?.Value ?? captured.State?.Value,
                capturedAmount, fee, net);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Captured order {OrderId} with transfer {TransferId}: amount {Amount}, fee {Fee}, net {Net}.",
                orderId, transferId, capturedAmount, fee, net);

            return MapPayment(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PaymentResponse> CancelOrderAsync(int orderId,
        CancellationToken cancellationToken = default)
    {
        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var payment = await GetPaymentAsync(orderId, cancellationToken);
            if (payment is null)
                throw new PaymentOperationException(404, "payment_not_found",
                    $"No payment exists for order {orderId}.");

            if (payment.Status == PaymentStatus.Canceled)
                return MapPayment(payment);

            if (payment.Status is PaymentStatus.Captured or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Refunded)
            {
                throw new PaymentOperationException(409, "already_captured",
                    "The payment has already been captured. Use the refunds endpoint instead of canceling.");
            }

            if (payment.Status == PaymentStatus.Authorized && !string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
            {
                try
                {
                    var voided = await _finix.AuthorizationsApi.VoidAuthorization(
                        new VoidAuthorizationRequest
                        {
                            AuthorizationIdVoidTo = payment.FinixAuthorizationId!,
                            Body = new VoidAnAuthorization { VoidMe = true }
                        }, cancellationToken: cancellationToken);

                    payment.MarkCanceled(voided.VoidState?.Value ?? voided.State?.Value);
                }
                catch (Exception ex)
                {
                    var current = await TryGetAuthorizationAsync(payment.FinixAuthorizationId!, cancellationToken);
                    if (current is not null &&
                        (current.IsVoid == true || current.State == State1.Canceled))
                    {
                        payment.MarkCanceled(current.VoidState?.Value ?? "SUCCEEDED");
                    }
                    else
                    {
                        throw FinixFailure("Void", ex);
                    }
                }
            }
            else
            {
                payment.MarkCanceled(null);
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Canceled order {OrderId}; the authorization hold was released.", orderId);
            return MapPayment(payment);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<RefundOperationResponse> RefundOrderAsync(string buyerId, bool isAdministrator,
        int orderId, RefundCommand command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new PaymentOperationException(400, "idempotency_key_required",
                "A caller-supplied idempotencyKey is required for refunds.");

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);
            if (order is null)
                throw new PaymentOperationException(404, "order_not_found", $"Order {orderId} was not found.");

            if (!isAdministrator && !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
                throw new PaymentOperationException(404, "order_not_found", $"Order {orderId} was not found.");

            var payment = await GetPaymentAsync(orderId, cancellationToken);
            if (payment is null)
                throw new PaymentOperationException(404, "payment_not_found",
                    $"No payment exists for order {orderId}.");

            var existing = payment.Refunds.FirstOrDefault(r =>
                string.Equals(r.IdempotencyKey, command.IdempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
                return MapRefund(payment, existing);

            if (payment.Status is not (PaymentStatus.Captured or PaymentStatus.PartiallyRefunded))
            {
                throw new PaymentOperationException(409, "not_refundable",
                    payment.Status == PaymentStatus.Refunded
                        ? "The captured payment has already been fully refunded."
                        : "Only a captured payment can be refunded. Fulfil the order first.");
            }

            if (string.IsNullOrWhiteSpace(payment.FinixTransferId))
                throw new PaymentOperationException(409, "capture_missing",
                    "The payment has no Finix capture transfer id, so it cannot be refunded.");

            var captured = payment.CapturedAmountInCents ?? payment.AmountInCents;
            var remaining = captured - payment.RefundedAmountInCents;
            if (remaining <= 0)
                throw new PaymentOperationException(409, "not_refundable",
                    "The captured payment has already been fully refunded.");

            var amount = command.AmountInCents ?? remaining;
            if (amount <= 0)
                throw new PaymentOperationException(400, "invalid_refund_amount",
                    "The refund amount must be greater than zero.");

            if (amount > remaining)
                throw new PaymentOperationException(409, "refund_exceeds_capture",
                    $"The refund amount ({amount} cents) exceeds the refundable balance ({remaining} cents).");

            if (amount > int.MaxValue)
                throw new PaymentOperationException(400, "refund_amount_too_large",
                    "The refund amount exceeds the supported maximum.");

            if (amount < captured)
            {
                var settlement = await GetTransferAsync(payment.FinixTransferId!, cancellationToken);
                var attempts = 0;
                while (settlement.State == State11.Pending && attempts < 60)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    settlement = await GetTransferAsync(payment.FinixTransferId!, cancellationToken);
                    attempts++;
                }

                payment.SetCaptureState(settlement.State?.Value);
                await _db.SaveChangesAsync(cancellationToken);

                if (settlement.State != State11.Succeeded)
                {
                    throw new PaymentOperationException(409, "capture_not_settled",
                        $"Finix capture {payment.FinixTransferId} is in state "
                        + $"'{settlement.State?.Value ?? "UNKNOWN"}'. Partial refunds require a succeeded "
                        + "capture; retry shortly, or refund the full captured amount.");
                }
            }

            Transfer refundTransfer;
            var finixIdempotencyKey =
                $"eshop-{payment.PaymentKey}-refund-{SanitizeKey(command.IdempotencyKey)}";
            try
            {
                var body = new RefundTransfer { RefundAmount = (int)amount };
                body.AdditionalProperties.Set("idempotency_id", finixIdempotencyKey);
                body.AdditionalProperties.Set("tags", new Dictionary<string, string>
                {
                    ["order_id"] = orderId.ToString(CultureInfo.InvariantCulture),
                    ["refund_key"] = command.IdempotencyKey
                });

                refundTransfer = await _finix.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = payment.FinixTransferId!,
                        Body = TransfersReversalsRequest.RefundTransfer(body)
                    }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                var recovered = await TryFindReversalAsync(payment.FinixTransferId!,
                    finixIdempotencyKey, cancellationToken);
                if (recovered is null)
                    throw FinixFailure("Refund", ex);
                refundTransfer = recovered;
            }

            var refund = new PaymentRefund(payment.Id, amount, command.IdempotencyKey,
                refundTransfer.Id, refundTransfer.State?.Value);
            payment.AddRefund(refund);
            payment.ApplyRefund(amount);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refunded {Amount} cents on order {OrderId} (Finix refund {RefundId}).",
                amount, orderId, refundTransfer.Id);

            return MapRefund(payment, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<OrderResponse>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var orders = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ThenByDescending(o => o.Id)
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return Array.Empty<OrderResponse>();

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .Include(p => p.Refunds)
            .Where(p => orderIds.Contains(p.OrderId))
            .ToDictionaryAsync(p => p.OrderId, cancellationToken);

        return orders
            .Select(order => MapOrder(order, payments.TryGetValue(order.Id, out var payment) ? payment : null))
            .ToList();
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new PaymentOperationException(400, "invalid_date_range",
                "The from date-time must be before or equal to the to date-time.");

        var finixTransactions = new List<ReconciliationTransaction>();

        // The SDK's created_at filters are serialized with fractional seconds and a trailing Z,
        // while Finix's list endpoints require yyyy-MM-ddTHH:mm:ss. Fetch every page and apply the
        // requested range to the resource timestamps here instead.
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            ListAuthorizations page;
            try
            {
                page = await _finix.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    Limit = 100,
                    Merchant = _options.MerchantId,
                    AfterCursor = cursor
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw FinixFailure("List Finix authorizations", ex);
            }

            foreach (var auth in page.Embedded?.Authorizations ?? Array.Empty<Authorization>())
            {
                if (auth.CreatedAt.HasValue && (auth.CreatedAt < from || auth.CreatedAt > to))
                    continue;

                finixTransactions.Add(new ReconciliationTransaction(
                    auth.Id ?? string.Empty,
                    "AUTHORIZATION",
                    null,
                    auth.State?.Value,
                    auth.Amount,
                    auth.Currency?.Value,
                    auth.CreatedAt,
                    null,
                    false,
                    null));
            }

            cursor = page.Page?.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor!));

        cursor = null;
        seenCursors.Clear();
        do
        {
            ListTransfers page;
            try
            {
                page = await _finix.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    Limit = 100,
                    Merchant = _options.MerchantId,
                    AfterCursor = cursor
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw FinixFailure("List Finix transfers", ex);
            }

            foreach (var transfer in page.Embedded?.Transfers ?? Array.Empty<Transfer>())
            {
                if (transfer.CreatedAt.HasValue && (transfer.CreatedAt < from || transfer.CreatedAt > to))
                    continue;

                finixTransactions.Add(new ReconciliationTransaction(
                    transfer.Id ?? string.Empty,
                    "TRANSFER",
                    transfer.Type?.Value,
                    transfer.State?.Value,
                    transfer.Amount,
                    transfer.Currency?.Value,
                    transfer.CreatedAt,
                    null,
                    false,
                    null));
            }

            cursor = page.Page?.NextCursor;
        } while (!string.IsNullOrWhiteSpace(cursor) && seenCursors.Add(cursor!));

        var payments = await _db.OrderPayments
            .Include(p => p.Refunds)
            .ToListAsync(cancellationToken);

        var byAuthorization = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixAuthorizationId))
            .GroupBy(p => p.FinixAuthorizationId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var byCapture = payments
            .Where(p => !string.IsNullOrWhiteSpace(p.FinixTransferId))
            .GroupBy(p => p.FinixTransferId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var byRefund = payments
            .SelectMany(p => p.Refunds.Select(r => (Payment: p, Refund: r)))
            .Where(x => !string.IsNullOrWhiteSpace(x.Refund.FinixRefundId))
            .GroupBy(x => x.Refund.FinixRefundId!)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var matchedFinixIds = new HashSet<string>(StringComparer.Ordinal);
        var matchedTransactions = new List<ReconciliationTransaction>(finixTransactions.Count);

        foreach (var transaction in finixTransactions)
        {
            OrderPayment? payment = null;
            if (transaction.Kind == "AUTHORIZATION")
            {
                byAuthorization.TryGetValue(transaction.FinixId, out payment);
            }
            else
            {
                byCapture.TryGetValue(transaction.FinixId, out payment);
                if (payment is null && byRefund.TryGetValue(transaction.FinixId, out var refundMatch))
                    payment = refundMatch.Payment;
            }

            var orderId = payment?.OrderId;

            var matched = orderId.HasValue && payments.Any(p => p.OrderId == orderId.Value);
            if (matched)
                matchedFinixIds.Add(transaction.FinixId);

            matchedTransactions.Add(transaction with
            {
                OrderId = orderId,
                Matched = matched,
                Issue = matched
                    ? null
                    : "Finix transaction has no matching eShop order payment."
            });
        }

        var ordersInRange = await _db.Orders
            .Include(o => o.OrderItems)
            .Where(o => o.OrderDate >= from && o.OrderDate <= to)
            .OrderBy(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderReports = new List<ReconciliationOrder>(ordersInRange.Count);
        foreach (var order in ordersInRange)
        {
            var payment = payments.FirstOrDefault(p => p.OrderId == order.Id);
            var referenced = false;
            string? issue = null;

            if (payment is null)
            {
                issue = "eShop order has no payment record.";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(payment.FinixAuthorizationId))
                    referenced |= matchedFinixIds.Contains(payment.FinixAuthorizationId!);
                if (!string.IsNullOrWhiteSpace(payment.FinixTransferId))
                    referenced |= matchedFinixIds.Contains(payment.FinixTransferId!);
                foreach (var refund in payment.Refunds)
                {
                    if (!string.IsNullOrWhiteSpace(refund.FinixRefundId))
                        referenced |= matchedFinixIds.Contains(refund.FinixRefundId!);
                }

                if (!referenced)
                {
                    issue = payment.Status == PaymentStatus.AwaitingPayment
                        ? "eShop order is awaiting payment; Finix has no transaction."
                        : "eShop order has Finix identifiers that were not returned for this date range.";
                }
            }

            orderReports.Add(new ReconciliationOrder(
                order.Id,
                order.BuyerId,
                order.OrderDate,
                order.Total(),
                ToCents(order.Total()),
                payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
                payment?.FinixAuthorizationId,
                payment?.FinixTransferId,
                payment?.RefundedAmountInCents ?? 0,
                referenced,
                issue));
        }

        return new ReconciliationReport(
            from,
            to,
            CurrencyCode,
            matchedTransactions,
            orderReports,
            matchedTransactions.Where(t => !t.Matched).ToList(),
            orderReports.Where(o => !o.Matched).ToList());
    }

    public async Task<PaymentMethodResponse> SavePaymentMethodAsync(string buyerId, SaveCardCommand command,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
            throw new PaymentOperationException(401, "unauthenticated", "A signed-in shopper is required.");

        var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, command.Card.Name, cancellationToken);
        var instrument = await CreatePaymentInstrumentAsync(identityId, command.Card, cancellationToken);

        var saved = new SavedCard(buyerId, instrument.Id!, identityId, command.Alias,
            instrument.Brand?.Value, instrument.LastFour, instrument.ExpirationMonth,
            instrument.ExpirationYear, instrument.CardType?.Value);

        _db.SavedCards.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Saved Finix payment instrument {InstrumentId} for {BuyerId}.",
            instrument.Id, buyerId);

        return MapPaymentMethod(saved);
    }

    public async Task<IReadOnlyList<PaymentMethodResponse>> ListPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        var cards = await _db.SavedCards
            .Where(c => c.BuyerId == buyerId)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync(cancellationToken);

        return cards.Select(MapPaymentMethod).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        var card = await _db.SavedCards.FirstOrDefaultAsync(
            c => c.Id == paymentMethodId && c.BuyerId == buyerId, cancellationToken);

        if (card is null)
            throw new PaymentOperationException(404, "payment_method_not_found",
                $"Saved payment method {paymentMethodId} was not found.");

        try
        {
            await _finix.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = card.FinixPaymentInstrumentId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                }, cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Finix payment instrument {InstrumentId} was already absent; removing local saved card.",
                card.FinixPaymentInstrumentId);
        }
        catch (Exception ex)
        {
            throw FinixFailure("Disable payment method", ex);
        }

        _db.SavedCards.Remove(card);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted saved payment method {PaymentMethodId} for {BuyerId}.",
            paymentMethodId, buyerId);
    }

    private async Task<OrderPayment?> GetPaymentAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderPayments
            .Include(p => p.Refunds)
            .FirstOrDefaultAsync(p => p.OrderId == orderId, cancellationToken);

    private async Task<string> GetOrCreateBuyerIdentityAsync(string buyerId, string? cardholderName,
        CancellationToken cancellationToken)
    {
        var gate = BuyerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await _db.FinixBuyerIdentities
                .FirstOrDefaultAsync(i => i.BuyerId == buyerId, cancellationToken);
            if (existing is not null)
                return existing.FinixIdentityId;

            var (firstName, lastName) = SplitName(cardholderName, buyerId);
            var email = buyerId.Contains('@') ? buyerId : "buyer@example.com";

            FinixIdentity identity;
            try
            {
                identity = await _finix.Identities.CreateIdentity(new CreateIdentityRequest
                {
                    Body = IdentitiesRequest.Buyer(new FinixBuyer
                    {
                        Entity = new Entity5
                        {
                            FirstName = firstName,
                            LastName = lastName,
                            Email = email
                        },
                        IdentityRoles = new[] { IdentityRole5.Buyer },
                        Type = Type10.Personal
                    })
                }, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw FinixFailure("Create buyer identity", ex);
            }

            if (string.IsNullOrWhiteSpace(identity.Id))
                throw new PaymentOperationException(502, "missing_identity_id",
                    "Finix did not return a buyer identity id.");

            var record = new FinixBuyerIdentity(buyerId, identity.Id!);
            _db.FinixBuyerIdentities.Add(record);
            await _db.SaveChangesAsync(cancellationToken);
            return record.FinixIdentityId;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<FinixCard> CreatePaymentInstrumentAsync(string identityId, CardDetails card,
        CancellationToken cancellationToken)
    {
        ValidateCard(card);

        var instrument = new PaymentInstrumentCard1
        {
            Number = DigitsOnly(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = NormalizeYear(card.ExpirationYear),
            SecurityCode = DigitsOnly(card.SecurityCode),
            Name = card.Name.Trim(),
            Identity = identityId,
            Address = BuildFinixAddress(card)
        };

        CreatePaymentInstrument response;
        try
        {
            response = await _finix.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(instrument)
                }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw FinixFailure("Create payment instrument", ex);
        }

        if (!response.TryGetPaymentInstrumentCard(out var created) || string.IsNullOrWhiteSpace(created?.Id))
            throw new PaymentOperationException(502, "missing_payment_instrument_id",
                "Finix did not return a payment instrument id.");

        return created!;
    }

    private async Task<Transfer> GetTransferAsync(string transferId, CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw FinixFailure("Fetch transfer", ex);
        }
    }

    private async Task<Authorization?> TryFindAuthorizationByIdempotencyKeyAsync(string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await _finix.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
            {
                IdempotencyId = idempotencyKey,
                Limit = 1,
                Merchant = _options.MerchantId
            }, cancellationToken: cancellationToken);

            return page.Embedded?.Authorizations?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up Finix authorization by idempotency key.");
            return null;
        }
    }

    private static string SanitizeKey(string value)
    {
        var sanitized = new string(value
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.')
            .ToArray());
        return sanitized.Length <= 80 ? sanitized : sanitized[..80];
    }

    private async Task<Authorization> GetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw FinixFailure("Fetch authorization", ex);
        }
    }

    private async Task<Authorization?> TryGetAuthorizationAsync(string authorizationId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _finix.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not re-fetch Finix authorization {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<Transfer?> TryFindReversalAsync(string transferId, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await _finix.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = transferId,
                        Limit = 100,
                        AfterCursor = cursor
                    }, cancellationToken: cancellationToken);

                var match = page.Embedded?.Transfers?
                    .FirstOrDefault(t => string.Equals(t.IdempotencyId, idempotencyKey, StringComparison.Ordinal));
                if (match is not null)
                    return match;

                cursor = page.Page?.NextCursor;
            } while (!string.IsNullOrWhiteSpace(cursor) && seen.Add(cursor!));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not list reversals for Finix transfer {TransferId}.", transferId);
        }

        return null;
    }

    private static bool IsCapturable(Authorization authorization, out string? reason)
    {
        reason = null;
        if (authorization.IsVoid == true || authorization.State == State1.Canceled)
        {
            reason = "The authorization hold was voided or canceled, so it can no longer be captured. "
                + "Cancel the order or ask the shopper to pay again.";
            return false;
        }

        if (authorization.State == State1.Failed)
        {
            reason = "The authorization failed at Finix, so it can no longer be captured. "
                + "Ask the shopper to pay again.";
            return false;
        }

        if (authorization.State != State1.Succeeded)
        {
            reason = $"The authorization is in state '{authorization.State?.Value ?? "UNKNOWN"}' and cannot be captured. "
                + "Ask the shopper to pay again.";
            return false;
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= DateTimeOffset.UtcNow)
        {
            reason = "The authorization hold has expired, so it can no longer be captured. "
                + "Cancel the order or ask the shopper to pay again.";
            return false;
        }

        return true;
    }

    private static PaymentOperationException FinixFailure(string operation, Exception exception)
    {
        var status = exception is ApiException api
            ? api.StatusCode switch
            {
                System.Net.HttpStatusCode.PaymentRequired => 402,
                System.Net.HttpStatusCode.Conflict => 409,
                System.Net.HttpStatusCode.UnprocessableEntity => 422,
                _ => 502
            }
            : 502;

        return new PaymentOperationException(status, "finix_error",
            $"{operation} failed at Finix: {DescribeFinixError(exception)}");
    }

    private static string DescribeFinixError(Exception exception)
    {
        if (exception is not ApiException)
            return exception.Message;

        var detail = exception.Message;
        try
        {
            var errorProperty = exception.GetType().GetProperty("Error");
            var errorObject = errorProperty?.GetValue(exception);
            if (errorObject is null)
                return detail;

            GenericError? genericError = null;
            var tryGetGeneric = errorObject.GetType().GetMethod("TryGetGenericError");
            if (tryGetGeneric is not null)
            {
                var arguments = new object?[] { null };
                if (tryGetGeneric.Invoke(errorObject, arguments) is true)
                    genericError = arguments[0] as GenericError;
            }

            if (genericError?.Embedded?.Errors is { Count: > 0 } errors)
            {
                var messages = errors
                    .Select(error => string.IsNullOrWhiteSpace(error.Code)
                        ? error.Message
                        : $"{error.Code}: {error.Message}")
                    .Where(message => !string.IsNullOrWhiteSpace(message));
                var joined = string.Join("; ", messages);
                if (!string.IsNullOrWhiteSpace(joined))
                    detail = $"{detail} {joined}";
            }
            else if (errorObject is ApiError apiError && apiError.TryGetRawError(out var rawError))
            {
                var body = rawError.ReadAsString();
                if (!string.IsNullOrWhiteSpace(body))
                    detail = $"{detail} {body}";
            }
        }
        catch
        {
            // Error bodies are best-effort diagnostics; never mask the original failure.
        }

        return detail.Length <= 1500 ? detail : detail[..1500];
    }

    private static OrderAddress BuildOrderAddress(ShippingAddressDetails? address)
    {
        if (address is null)
            return new OrderAddress("123 Main Street", "Seattle", "WA", "USA", "98101");

        if (string.IsNullOrWhiteSpace(address.Street) || string.IsNullOrWhiteSpace(address.City)
            || string.IsNullOrWhiteSpace(address.Country) || string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentOperationException(400, "invalid_shipping_address",
                "Shipping address requires street, city, country and zipCode.");
        }

        return new OrderAddress(address.Street.Trim(), address.City.Trim(),
            address.State?.Trim() ?? string.Empty, address.Country.Trim(), address.ZipCode.Trim());
    }

    private static FinixAddress? BuildFinixAddress(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.AddressLine1) || string.IsNullOrWhiteSpace(card.City)
            || string.IsNullOrWhiteSpace(card.PostalCode))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = card.AddressLine1.Trim(),
            City = card.City.Trim(),
            Region = string.IsNullOrWhiteSpace(card.Region) ? "WA" : card.Region.Trim(),
            PostalCode = card.PostalCode.Trim(),
            Country = ParseCountry(card.Country)
        };
    }

    private static Country ParseCountry(string? country)
    {
        var value = string.IsNullOrWhiteSpace(country) ? "USA" : country.Trim().ToUpperInvariant();
        if (value is "US" or "UNITED STATES") value = "USA";
        if (value is "CA" or "CANADA") value = "CAN";
        return Country.TryGetKnownValue(value, out var known) ? known! : Country.Usa;
    }

    private static void ValidateCard(CardDetails card)
    {
        var number = DigitsOnly(card.Number);
        if (number.Length is < 12 or > 19)
            throw new PaymentOperationException(400, "invalid_card_number",
                "The card number is invalid.");

        if (card.ExpirationMonth is < 1 or > 12)
            throw new PaymentOperationException(400, "invalid_expiration_month",
                "The card expiration month must be between 1 and 12.");

        var year = NormalizeYear(card.ExpirationYear);
        if (year < DateTime.UtcNow.Year)
            throw new PaymentOperationException(400, "expired_card",
                "The card expiration year is in the past.");

        var securityCode = DigitsOnly(card.SecurityCode);
        if (securityCode.Length is < 3 or > 4)
            throw new PaymentOperationException(400, "invalid_security_code",
                "The card security code must be 3 or 4 digits.");

        if (string.IsNullOrWhiteSpace(card.Name))
            throw new PaymentOperationException(400, "invalid_cardholder_name",
                "The cardholder name is required.");
    }

    private static int NormalizeYear(int year) => year < 100 ? 2000 + year : year;

    private static string DigitsOnly(string? value) =>
        new((value ?? string.Empty).Where(char.IsDigit).ToArray());

    private static (string FirstName, string LastName) SplitName(string? name, string buyerId)
    {
        var effective = string.IsNullOrWhiteSpace(name) ? buyerId.Split('@')[0] : name.Trim();
        var parts = effective.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => ("Shopper", "Shopper"),
            1 => (TrimTo(parts[0], 20), "Shopper"),
            _ => (TrimTo(parts[0], 20), TrimTo(parts[^1], 20))
        };
    }

    private static string TrimTo(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero);

    private static decimal FromCents(long amountInCents) => amountInCents / 100m;

    private static OrderResponse MapOrder(Order order, OrderPayment? payment) => new(
        order.Id,
        order.BuyerId,
        order.OrderDate,
        order.Total(),
        ToCents(order.Total()),
        payment?.Currency ?? string.Empty,
        payment?.Status.ToString() ?? PaymentStatus.AwaitingPayment.ToString(),
        order.OrderItems.Select(i => new OrderItemResponse(
            i.ItemOrdered.CatalogItemId,
            i.ItemOrdered.ProductName,
            i.UnitPrice,
            i.Units)).ToList(),
        payment is null ? null : MapPayment(payment));

    private static PaymentResponse MapPayment(OrderPayment payment) => new(
        payment.Id,
        payment.OrderId,
        payment.Status.ToString(),
        payment.Currency,
        payment.AmountInCents,
        FromCents(payment.AmountInCents),
        payment.FinixAuthorizationId,
        payment.AuthorizationState,
        payment.AuthorizedAt,
        payment.FinixTransferId,
        payment.CaptureState,
        payment.CapturedAmountInCents,
        payment.CapturedAmountInCents.HasValue ? FromCents(payment.CapturedAmountInCents.Value) : null,
        payment.FinixFeeInCents,
        payment.FinixFeeInCents.HasValue ? FromCents(payment.FinixFeeInCents.Value) : null,
        payment.NetAmountInCents,
        payment.NetAmountInCents.HasValue ? FromCents(payment.NetAmountInCents.Value) : null,
        payment.RefundedAmountInCents,
        FromCents(payment.RefundedAmountInCents),
        payment.Refunds
            .OrderBy(r => r.Id)
            .Select(MapRefund)
            .ToList(),
        payment.FailureCode,
        payment.FailureMessage);

    private static RefundResponse MapRefund(PaymentRefund refund) => new(
        refund.Id,
        refund.AmountInCents,
        FromCents(refund.AmountInCents),
        refund.IdempotencyKey,
        refund.FinixRefundId,
        refund.State,
        refund.CreatedAt);

    private static RefundOperationResponse MapRefund(OrderPayment payment, PaymentRefund refund) => new(
        refund.Id,
        payment.OrderId,
        MapRefund(refund),
        MapPayment(payment));

    private static PaymentMethodResponse MapPaymentMethod(SavedCard card) => new(
        card.Id,
        card.Brand,
        card.Last4,
        card.ExpirationMonth,
        card.ExpirationYear,
        card.CardType,
        card.Alias,
        card.CreatedAt);
}
