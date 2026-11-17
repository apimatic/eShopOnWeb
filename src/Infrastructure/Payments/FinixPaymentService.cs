using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FinixApiReference;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using AppAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using FinixAddress = FinixApiReference.Models.Address;
using FinixBuyer = FinixApiReference.Models.Buyer;
using FinixIdentity = FinixApiReference.Models.Identity;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class FinixPaymentService : IPaymentService
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> OrderLocks = new();
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuyerLocks = new(StringComparer.Ordinal);

    private readonly CatalogContext _db;
    private readonly FinixApiReferenceClient _finix;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(CatalogContext db,
        FinixApiReferenceClient finix,
        IOptions<FinixOptions> options,
        ILogger<FinixPaymentService> logger)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<Order> CreateOrderAsync(string buyerId,
        IReadOnlyList<CreateOrderItem> items,
        AppAddress shippingAddress,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        if (items.Any(item => item.CatalogItemId <= 0 || item.Quantity <= 0))
        {
            throw new PaymentValidationException("Every order item needs a positive catalogItemId and quantity.");
        }

        var catalogItemIds = items.Select(item => item.CatalogItemId).Distinct().ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(item => catalogItemIds.Contains(item.Id))
            .ToListAsync(cancellationToken);

        var orderItems = new List<OrderItem>();
        foreach (var requested in items)
        {
            var catalogItem = catalogItems.FirstOrDefault(item => item.Id == requested.CatalogItemId);
            if (catalogItem is null)
            {
                throw new NotFoundException($"Catalog item {requested.CatalogItemId} was not found.");
            }

            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var ordered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            orderItems.Add(new OrderItem(ordered, catalogItem.Price, requested.Quantity));
        }

        var order = new Order(buyerId, shippingAddress, orderItems);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    public async Task<Order> PayAsync(string buyerId,
        int orderId,
        PayOrderCommand command,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
            {
                throw new NotFoundException($"Order {orderId} was not found.");
            }

            if (order.Status is OrderStatus.Authorized or OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                throw new PaymentConflictException($"Order {orderId} was cancelled and can no longer be paid.");
            }

            if (order.Status != OrderStatus.AwaitingPayment)
            {
                throw new PaymentConflictException($"Order {orderId} is not awaiting payment (status: {order.Status}).");
            }

            if (order.TotalAmountCents <= 0)
            {
                throw new PaymentValidationException("The order total must be greater than zero.");
            }

            var (instrumentId, paymentMethodId) = await ResolvePaymentSourceAsync(buyerId, command, cancellationToken);
            var idempotencyId = $"eshop-auth-{order.GetOrCreatePaymentIdempotencyKey()}-{order.PaymentAttempt}";
            var currency = _options.Currency!;

            await _db.SaveChangesAsync(cancellationToken);

            Authorization authorization;
            try
            {
                authorization = await AuthorizeAsync(order,
                    instrumentId,
                    command.Card?.SecurityCode,
                    currency,
                    idempotencyId,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                var recovered = await TryFindAuthorizationByIdempotencyAsync(idempotencyId, cancellationToken);
                if (recovered is null ||
                    (recovered.Amount.HasValue && recovered.Amount.Value != order.TotalAmountCents))
                {
                    order.MarkPaymentFailed(Describe(ex), incrementAttempt: false);
                    await _db.SaveChangesAsync(cancellationToken);
                    throw new PaymentGatewayException(
                        $"Finix could not authorize order {order.Id}: {Describe(ex)}", ex);
                }

                authorization = recovered;
            }

            var state = authorization.State?.Value;
            if (!string.Equals(state, "SUCCEEDED", StringComparison.OrdinalIgnoreCase))
            {
                order.MarkPaymentFailed(
                    authorization.FailureMessage ?? $"Finix authorization ended in state {state ?? "UNKNOWN"}.");
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentGatewayException(
                    $"Finix declined the authorization for order {order.Id}: " +
                    $"{authorization.FailureMessage ?? $"state {state ?? "UNKNOWN"}"}");
            }

            order.MarkAuthorized(authorization.Id!,
                state,
                currency,
                order.TotalAmountCents,
                instrumentId,
                paymentMethodId);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order is null)
            {
                throw new NotFoundException($"Order {orderId} was not found.");
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                return order;
            }

            if (order.Status != OrderStatus.Authorized || string.IsNullOrWhiteSpace(order.PaymentAuthorizationId))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} is not authorized and cannot be fulfilled (status: {order.Status}).");
            }

            Authorization captured;
            try
            {
                captured = await _finix.AuthorizationsApi.CaptureAuthorization(
                    new CaptureAuthorizationRequest
                    {
                        AuthorizationId = order.PaymentAuthorizationId!,
                        Body = new CaptureAuthorization
                        {
                            CaptureAmount = order.PaymentAmountCents
                        }
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                var current = await TryGetAuthorizationAsync(order.PaymentAuthorizationId!, cancellationToken);
                var state = current?.State?.Value ?? order.PaymentAuthorizationState ?? "UNKNOWN";
                throw new PaymentConflictException(
                    $"Authorization {order.PaymentAuthorizationId} could not be captured because Finix reports state " +
                    $"{state}. The hold may have expired, been voided, or already been captured. " +
                    $"Operator action: cancel the order or ask the shopper to authorize again. Finix said: {Describe(ex)}");
            }

            if (string.IsNullOrWhiteSpace(captured.Transfer))
            {
                throw new PaymentGatewayException(
                    $"Finix captured authorization {order.PaymentAuthorizationId} but did not return a transfer id " +
                    $"(state: {captured.State?.Value ?? "UNKNOWN"}).");
            }

            Transfer transfer;
            try
            {
                transfer = await WaitForCaptureToSettleAsync(captured.Transfer!, cancellationToken);
            }
            catch (Exception ex)
            {
                throw new PaymentGatewayException(
                    $"Finix captured authorization {order.PaymentAuthorizationId} as transfer {captured.Transfer}, " +
                    $"but the transfer could not be read: {Describe(ex)}", ex);
            }

            var capturedAmount = transfer.Amount ?? order.PaymentAmountCents;
            var fee = transfer.Fee ?? 0;
            var net = capturedAmount - fee;

            order.MarkFulfilled(captured.Transfer!,
                transfer.State?.Value,
                capturedAmount,
                fee,
                net);
            await _db.SaveChangesAsync(cancellationToken);
            return order;
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order is null)
            {
                throw new NotFoundException($"Order {orderId} was not found.");
            }

            if (order.Status == OrderStatus.Cancelled)
            {
                return order;
            }

            if (order.Status is OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded)
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has already been fulfilled. Use the refunds endpoint instead of cancelling.");
            }

            if (order.Status == OrderStatus.AwaitingPayment || string.IsNullOrWhiteSpace(order.PaymentAuthorizationId))
            {
                order.MarkCancelled(order.PaymentAuthorizationState, order.PaymentVoidState);
                await _db.SaveChangesAsync(cancellationToken);
                return order;
            }

            try
            {
                var voided = await _finix.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = order.PaymentAuthorizationId!,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: cancellationToken);

                order.MarkCancelled(voided.State?.Value, voided.VoidState?.Value);
                await _db.SaveChangesAsync(cancellationToken);
                return order;
            }
            catch (Exception ex)
            {
                var current = await TryGetAuthorizationAsync(order.PaymentAuthorizationId!, cancellationToken);
                if (current is not null &&
                    (current.IsVoid == true ||
                     string.Equals(current.State?.Value, "CANCELED", StringComparison.OrdinalIgnoreCase)))
                {
                    order.MarkCancelled(current.State?.Value, current.VoidState?.Value);
                    await _db.SaveChangesAsync(cancellationToken);
                    return order;
                }

                throw new PaymentGatewayException(
                    $"Finix could not release authorization {order.PaymentAuthorizationId} for order {orderId}: " +
                    $"{Describe(ex)}", ex);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<(Order Order, OrderRefund Refund)> RefundAsync(int orderId,
        long? amountCents,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw new PaymentValidationException("A non-empty idempotency key is required for refunds.");
        }

        var gate = OrderLocks.GetOrAdd(orderId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var order = await LoadOrderAsync(orderId, cancellationToken);
            if (order is null)
            {
                throw new NotFoundException($"Order {orderId} was not found.");
            }

            if (order.Status is not (OrderStatus.Fulfilled or OrderStatus.PartiallyRefunded or OrderStatus.Refunded))
            {
                throw new PaymentConflictException(
                    $"Order {orderId} has not been fulfilled and cannot be refunded (status: {order.Status}).");
            }

            if (string.IsNullOrWhiteSpace(order.PaymentCaptureTransferId))
            {
                throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
            }

            var existing = order.Refunds.FirstOrDefault(refund =>
                string.Equals(refund.IdempotencyKey, idempotencyKey, StringComparison.Ordinal));
            if (existing is not null)
            {
                return (order, existing);
            }

            var requested = amountCents ?? order.RefundableAmountCents;
            if (requested <= 0)
            {
                throw new PaymentValidationException("The refund amount must be greater than zero.");
            }

            if (requested > order.RefundableAmountCents)
            {
                throw new PaymentValidationException(
                    $"Refund of {requested} cents exceeds the remaining refundable amount of " +
                    $"{order.RefundableAmountCents} cents for order {orderId}.");
            }

            if (requested > int.MaxValue)
            {
                throw new PaymentValidationException("The refund amount is too large for a single refund.");
            }

            var finixIdempotencyKey = $"eshop-refund-{order.Id}-{idempotencyKey}";

            Transfer reversal;
            try
            {
                var body = new RefundTransfer
                {
                    RefundAmount = (int)requested
                };
                body.AdditionalProperties.Set("idempotency_id", finixIdempotencyKey);

                reversal = await _finix.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = order.PaymentCaptureTransferId!,
                        Body = body
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                var recovered = await TryFindReversalByIdempotencyAsync(
                    order.PaymentCaptureTransferId!, finixIdempotencyKey, cancellationToken);
                if (recovered is null)
                {
                    throw new PaymentGatewayException(
                        $"Finix could not refund order {orderId}: {Describe(ex)}", ex);
                }

                reversal = recovered;
            }

            if (string.Equals(reversal.State?.Value, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentGatewayException(
                    $"Finix rejected the refund for order {orderId}: " +
                    $"{reversal.FailureMessage ?? "state FAILED"}");
            }

            if (string.IsNullOrWhiteSpace(reversal.Id))
            {
                throw new PaymentGatewayException("Finix did not return a refund transfer id.");
            }

            var refund = new OrderRefund(order.Id,
                reversal.Id!,
                requested,
                idempotencyKey,
                reversal.State?.Value);
            _db.OrderRefunds.Add(refund);
            order.AddRefund(refund);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException)
            {
                var duplicate = await _db.OrderRefunds.AsNoTracking().FirstOrDefaultAsync(
                    stored => stored.OrderId == order.Id &&
                              stored.IdempotencyKey == idempotencyKey,
                    cancellationToken);
                if (duplicate is not null)
                {
                    return (order, duplicate);
                }

                throw;
            }

            return (order, refund);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<Order>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.Orders
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.ItemOrdered)
            .Include(order => order.Refunds)
            .Where(order => order.BuyerId == buyerId)
            .OrderByDescending(order => order.OrderDate)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReconciliationReport> GetReconciliationAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (from > to)
        {
            throw new PaymentValidationException("The 'from' value must be before or equal to 'to'.");
        }

        var report = new ReconciliationReport { From = from, To = to };

        List<Authorization> authorizations;
        List<Transfer> transfers;
        try
        {
            authorizations = await ListAllAuthorizationsAsync(from, to, cancellationToken);
            transfers = await ListAllTransfersAsync(from, to, cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException(
                $"Finix reconciliation query failed: {Describe(ex)}", ex);
        }

        var orders = await _db.Orders
            .Include(order => order.Refunds)
            .ToListAsync(cancellationToken);

        var ordersByAuthorization = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.PaymentAuthorizationId))
            .GroupBy(order => order.PaymentAuthorizationId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var ordersByCapture = orders
            .Where(order => !string.IsNullOrWhiteSpace(order.PaymentCaptureTransferId))
            .GroupBy(order => order.PaymentCaptureTransferId!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var ordersByRefund = orders
            .SelectMany(order => order.Refunds.Select(refund => new { Order = order, Refund = refund }))
            .GroupBy(pair => pair.Refund.FinixTransferId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        var seenAuthorizationIds = new HashSet<string>(StringComparer.Ordinal);
        var seenTransferIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authorization in authorizations)
        {
            if (string.IsNullOrWhiteSpace(authorization.Id))
            {
                continue;
            }

            seenAuthorizationIds.Add(authorization.Id!);
            ordersByAuthorization.TryGetValue(authorization.Id!, out var order);

            var record = new ReconciliationTransaction
            {
                Id = authorization.Id!,
                Kind = "Authorization",
                Type = "AUTHORIZATION",
                State = authorization.State?.Value,
                AmountCents = authorization.Amount ?? 0,
                Currency = authorization.Currency?.Value,
                CreatedAt = authorization.CreatedAt,
                OrderId = order?.Id,
                MatchKind = order is null ? null : "authorization"
            };

            report.FinixTransactions.Add(record);
            if (order is null)
            {
                report.UnmatchedFinixTransactions.Add(record);
            }
        }

        foreach (var transfer in transfers)
        {
            if (string.IsNullOrWhiteSpace(transfer.Id))
            {
                continue;
            }

            seenTransferIds.Add(transfer.Id!);

            Order? order = null;
            string? matchKind = null;
            if (ordersByCapture.TryGetValue(transfer.Id!, out var captureOrder))
            {
                order = captureOrder;
                matchKind = "capture";
            }
            else if (ordersByRefund.TryGetValue(transfer.Id!, out var refundMatch))
            {
                order = refundMatch.Order;
                matchKind = "refund";
            }

            var record = new ReconciliationTransaction
            {
                Id = transfer.Id!,
                Kind = matchKind == "refund" ? "Refund" : matchKind == "capture" ? "Capture" : "Transfer",
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                AmountCents = transfer.Amount ?? 0,
                Currency = transfer.Currency?.Value,
                CreatedAt = transfer.CreatedAt,
                OrderId = order?.Id,
                MatchKind = matchKind
            };

            report.FinixTransactions.Add(record);
            if (order is null)
            {
                report.UnmatchedFinixTransactions.Add(record);
            }
        }

        foreach (var order in orders)
        {
            var inRange = (order.OrderDate >= from && order.OrderDate <= to) ||
                          (order.AuthorizedAt is { } authorizedAt && authorizedAt >= from && authorizedAt <= to) ||
                          (order.FulfilledAt is { } fulfilledAt && fulfilledAt >= from && fulfilledAt <= to);
            if (!inRange)
            {
                continue;
            }

            var missing = new List<string>();
            if (!string.IsNullOrWhiteSpace(order.PaymentAuthorizationId) &&
                !seenAuthorizationIds.Contains(order.PaymentAuthorizationId!))
            {
                missing.Add($"authorization {order.PaymentAuthorizationId}");
            }

            if (!string.IsNullOrWhiteSpace(order.PaymentCaptureTransferId) &&
                !seenTransferIds.Contains(order.PaymentCaptureTransferId!))
            {
                missing.Add($"capture {order.PaymentCaptureTransferId}");
            }

            foreach (var refund in order.Refunds)
            {
                if (!seenTransferIds.Contains(refund.FinixTransferId))
                {
                    missing.Add($"refund {refund.FinixTransferId}");
                }
            }

            if (missing.Count == 0)
            {
                continue;
            }

            report.UnmatchedOrders.Add(new ReconciliationOrder
            {
                OrderId = order.Id,
                BuyerId = order.BuyerId,
                Status = order.Status.ToString(),
                AuthorizationId = order.PaymentAuthorizationId,
                CaptureTransferId = order.PaymentCaptureTransferId,
                RefundTransferIds = order.Refunds.Select(refund => refund.FinixTransferId).ToList(),
                Reason = $"eShop has payment state Finix did not return in the range: {string.Join(", ", missing)}."
            });
        }

        report.FinixTransactions = report.FinixTransactions
            .OrderBy(transaction => transaction.CreatedAt)
            .ToList();

        return report;
    }

    public async Task<PaymentMethod> SavePaymentMethodAsync(string buyerId,
        CardDetails card,
        string? alias,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        ValidateCard(card);

        var instrument = await CreateCardInstrumentAsync(buyerId, card, cancellationToken);

        var paymentMethod = new PaymentMethod(buyerId,
            instrument.Id!,
            instrument.Brand?.Value,
            instrument.LastFour,
            instrument.ExpirationMonth,
            instrument.ExpirationYear,
            instrument.Name ?? card.Name,
            alias);
        _db.PaymentMethods.Add(paymentMethod);
        await _db.SaveChangesAsync(cancellationToken);
        return paymentMethod;
    }

    public async Task<IReadOnlyList<PaymentMethod>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        return await _db.PaymentMethods
            .Where(method => method.BuyerId == buyerId && method.IsActive)
            .OrderByDescending(method => method.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task DeletePaymentMethodAsync(string buyerId,
        int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var paymentMethod = await _db.PaymentMethods.FirstOrDefaultAsync(
            method => method.Id == paymentMethodId && method.BuyerId == buyerId && method.IsActive,
            cancellationToken);

        if (paymentMethod is null)
        {
            throw new NotFoundException($"Saved payment method {paymentMethodId} was not found.");
        }

        try
        {
            await _finix.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentMethod.FinixPaymentInstrumentId,
                    Body = new PaymentInstrumentDisableAPaymentInstrument { Enabled = false }
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Finix could not disable payment instrument {PaymentInstrumentId}; disabling it locally only.",
                paymentMethod.FinixPaymentInstrumentId);
        }

        paymentMethod.Deactivate();
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string InstrumentId, int? PaymentMethodId)> ResolvePaymentSourceAsync(
        string buyerId,
        PayOrderCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentMethodId.HasValue)
        {
            var paymentMethod = await _db.PaymentMethods.FirstOrDefaultAsync(
                method => method.Id == command.PaymentMethodId.Value &&
                          method.BuyerId == buyerId &&
                          method.IsActive,
                cancellationToken);

            if (paymentMethod is null)
            {
                throw new NotFoundException(
                    $"Saved payment method {command.PaymentMethodId.Value} was not found for this shopper.");
            }

            return (paymentMethod.FinixPaymentInstrumentId, paymentMethod.Id);
        }

        if (command.Card is not null)
        {
            ValidateCard(command.Card);
            var instrument = await CreateCardInstrumentAsync(buyerId, command.Card, cancellationToken);
            return (instrument.Id!, null);
        }

        throw new PaymentValidationException(
            "Provide either a saved paymentMethodId or one-off card details to pay for the order.");
    }

    private async Task<PaymentInstrumentCard> CreateCardInstrumentAsync(string buyerId,
        CardDetails card,
        CancellationToken cancellationToken)
    {
        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);

        var request = new PaymentInstrumentCard1
        {
            Identity = identityId,
            Number = DigitsOnly(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = NormalizeExpirationYear(card.ExpirationYear),
            SecurityCode = card.SecurityCode,
            Name = string.IsNullOrWhiteSpace(card.Name) ? "eShop Shopper" : card.Name,
            Address = MapAddress(card.Address)
        };

        CreatePaymentInstrument response;
        try
        {
            response = await _finix.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest { Body = request },
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            throw new PaymentGatewayException($"Finix could not tokenize the card: {Describe(ex)}", ex);
        }

        if (!response.TryGetPaymentInstrumentCard(out var instrument) ||
            string.IsNullOrWhiteSpace(instrument.Id))
        {
            throw new PaymentGatewayException("Finix did not return a usable card payment instrument.");
        }

        return instrument;
    }

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        var existing = await _db.ShopperPaymentProfiles
            .FirstOrDefaultAsync(profile => profile.BuyerId == buyerId, cancellationToken);
        if (existing is not null)
        {
            return existing.FinixIdentityId;
        }

        var gate = BuyerLocks.GetOrAdd(buyerId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            existing = await _db.ShopperPaymentProfiles
                .FirstOrDefaultAsync(profile => profile.BuyerId == buyerId, cancellationToken);
            if (existing is not null)
            {
                return existing.FinixIdentityId;
            }

            FinixIdentity identity;
            try
            {
                identity = await _finix.Identities.CreateIdentity(
                    new CreateIdentityRequest
                    {
                        Body = new FinixBuyer
                        {
                            Entity = new Entity5 { Email = buyerId },
                            IdentityRoles = new[] { IdentityRole5.Buyer },
                            Type = Type10.Personal
                        }
                    },
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                throw new PaymentGatewayException(
                    $"Finix could not create a buyer identity for the shopper: {Describe(ex)}", ex);
            }

            if (string.IsNullOrWhiteSpace(identity.Id))
            {
                throw new PaymentGatewayException("Finix did not return a buyer identity id.");
            }

            var profile = new ShopperPaymentProfile(buyerId, identity.Id!);
            _db.ShopperPaymentProfiles.Add(profile);
            await _db.SaveChangesAsync(cancellationToken);
            return profile.FinixIdentityId;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<Authorization> AuthorizeAsync(Order order,
        string instrumentId,
        string? securityCode,
        string currency,
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        var body = new AuthorizationAllFields
        {
            Amount = order.TotalAmountCents,
            Currency = ParseCurrency(currency),
            Merchant = _options.MerchantId!,
            Source = instrumentId,
            IdempotencyId = idempotencyId,
            SecurityCode = securityCode,
            Tags = new Dictionary<string, string>
            {
                ["eshop_order_id"] = order.Id.ToString()
            }
        };

        return await _finix.AuthorizationsApi.CreateAuthorization(
            new CreateAuthorizationRequest { Body = body },
            cancellationToken: cancellationToken);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders
            .Include(order => order.OrderItems)
                .ThenInclude(item => item.ItemOrdered)
            .Include(order => order.Refunds)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);

    private async Task<Authorization?> TryFindAuthorizationByIdempotencyAsync(
        string idempotencyId,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await _finix.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    IdempotencyId = idempotencyId,
                    Limit = 1
                },
                cancellationToken: cancellationToken);

            return result.Embedded?.Authorizations?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up Finix authorization by idempotency id.");
            return null;
        }
    }

    private async Task<Transfer> WaitForCaptureToSettleAsync(string transferId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        Transfer? transfer = null;

        while (true)
        {
            transfer = await _finix.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: cancellationToken);

            var state = transfer.State?.Value;
            if (!string.Equals(state, "PENDING", StringComparison.OrdinalIgnoreCase) ||
                DateTimeOffset.UtcNow >= deadline)
            {
                return transfer;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
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
            _logger.LogWarning(ex, "Could not read Finix authorization {AuthorizationId}.", authorizationId);
            return null;
        }
    }

    private async Task<Transfer?> TryFindReversalByIdempotencyAsync(string parentTransferId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        try
        {
            string? cursor = null;
            do
            {
                var response = await _finix.TransfersApi.ListTransferReversals(
                    new ListTransferReversalsRequest
                    {
                        TransferId = parentTransferId,
                        AfterCursor = cursor,
                        Limit = 100
                    },
                    cancellationToken: cancellationToken);

                var found = response.Embedded?.Transfers?
                    .FirstOrDefault(transfer =>
                        string.Equals(transfer.IdempotencyId, idempotencyKey, StringComparison.Ordinal));
                if (found is not null)
                {
                    return found;
                }

                cursor = response.Page?.NextCursor;
            }
            while (!string.IsNullOrWhiteSpace(cursor));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not look up Finix reversal by idempotency id.");
        }

        return null;
    }

    private async Task<List<Authorization>> ListAllAuthorizationsAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<Authorization>();
        string? cursor = null;
        do
        {
            var response = await _finix.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    AfterCursor = cursor,
                    Limit = 100
                },
                cancellationToken: cancellationToken);

            if (response.Embedded?.Authorizations is not null)
            {
                results.AddRange(response.Embedded.Authorizations.Where(authorization =>
                    authorization.CreatedAt is { } createdAt &&
                    createdAt >= from &&
                    createdAt <= to));
            }

            cursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private async Task<List<Transfer>> ListAllTransfersAsync(DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var results = new List<Transfer>();
        string? cursor = null;
        do
        {
            var response = await _finix.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    AfterCursor = cursor,
                    Limit = 100
                },
                cancellationToken: cancellationToken);

            if (response.Embedded?.Transfers is not null)
            {
                results.AddRange(response.Embedded.Transfers.Where(transfer =>
                    transfer.CreatedAt is { } createdAt &&
                    createdAt >= from &&
                    createdAt <= to));
            }

            cursor = response.Page?.NextCursor;
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        return results;
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new PaymentGatewayException(
                "Finix is not configured. Set Finix:Username, Finix:Password, Finix:MerchantId, " +
                "Finix:Environment and Finix:Currency (for example via user-secrets or the " +
                "FINIX_USERNAME, FINIX_PASSWORD, FINIX_MERCHANT_ID, FINIX_ENVIRONMENT and FINIX_CURRENCY " +
                "environment variables).");
        }
    }

    private static void ValidateCard(CardDetails card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || DigitsOnly(card.Number).Length < 12)
        {
            throw new PaymentValidationException("A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("The card expiration month must be between 1 and 12.");
        }

        var year = NormalizeExpirationYear(card.ExpirationYear);
        var now = DateTimeOffset.UtcNow;
        if (year < now.Year || (year == now.Year && card.ExpirationMonth < now.Month))
        {
            throw new PaymentValidationException("The card expiration date is in the past.");
        }
    }

    private static int NormalizeExpirationYear(int year) => year < 100 ? 2000 + year : year;

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static FinixAddress? MapAddress(CardBillingAddress? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Region) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
        {
            return null;
        }

        return new FinixAddress
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = ParseCountry(string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country)
        };
    }

    private static Currency ParseCurrency(string currency) =>
        JsonSerializer.Deserialize<Currency>(JsonSerializer.Serialize(currency))
        ?? throw new PaymentValidationException($"The configured currency '{currency}' is not valid.");

    private static Country ParseCountry(string country) =>
        JsonSerializer.Deserialize<Country>(JsonSerializer.Serialize(country))
        ?? throw new PaymentValidationException($"The country '{country}' is not valid.");

    private static string Describe(Exception exception)
    {
        if (exception is ApiException apiException)
        {
            var errorProperty = apiException.GetType().GetProperty("Error", BindingFlags.Public | BindingFlags.Instance);
            var error = errorProperty?.GetValue(apiException);
            if (error is not null)
            {
                var genericMethod = error.GetType().GetMethod("TryGetGenericError",
                    BindingFlags.Public | BindingFlags.Instance);
                if (genericMethod is not null)
                {
                    var parameters = new object?[] { null };
                    if (genericMethod.Invoke(error, parameters) is true &&
                        parameters[0] is GenericError genericError)
                    {
                        var messages = genericError.Embedded.Errors?
                            .Select(finixError =>
                                string.IsNullOrWhiteSpace(finixError.Code)
                                    ? finixError.Message
                                    : $"{finixError.Code}: {finixError.Message}")
                            .Where(message => !string.IsNullOrWhiteSpace(message))
                            .ToArray();

                        if (messages is { Length: > 0 })
                        {
                            return $"{apiException.Message} {string.Join("; ", messages)}";
                        }
                    }
                }

                var rawMethod = error.GetType().GetMethod("TryGetRawError",
                    BindingFlags.Public | BindingFlags.Instance);
                if (rawMethod is not null)
                {
                    var parameters = new object?[] { null };
                    if (rawMethod.Invoke(error, parameters) is true &&
                        parameters[0] is FinixApiReference.Core.ErrorResponse.RawError rawError)
                    {
                        var body = rawError.ReadAsString();
                        if (!string.IsNullOrWhiteSpace(body))
                        {
                            return $"{apiException.Message} {body}";
                        }
                    }
                }

                return $"{apiException.Message} {error}";
            }
        }

        return exception.Message;
    }
}
