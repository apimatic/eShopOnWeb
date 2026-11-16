using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Core.Hooks;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using EshopAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;
using FinixAddress = FinixApiReference.Models.Address;
using FinixAuthorization = FinixApiReference.Models.Authorization;
using FinixIdentity = FinixApiReference.Models.Identity;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>
/// The single boundary between eShop orders and Finix. All SDK calls, claims,
/// idempotency and unknown-outcome re-reads live here; endpoints stay thin.
/// </summary>
public class FinixPaymentService
{
    private const int MaxListPages = 100;
    private const int PageLimit = 100;
    private static readonly SemaphoreSlim RefundLock = new(1, 1);

    private readonly CatalogContext _db;
    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentService> _logger;

    public FinixPaymentService(
        CatalogContext db,
        FinixApiReferenceClient client,
        FinixOptions options,
        ILogger<FinixPaymentService> logger)
    {
        _db = db;
        _client = client;
        _options = options;
        _logger = logger;
    }

    // ---------------------------------------------------------------- orders

    public async Task<Order> CreateOrderAsync(
        string buyerId,
        IReadOnlyList<OrderLineInput> lines,
        ShippingAddressInput? shipping,
        CancellationToken ct)
    {
        if (lines is null || lines.Count == 0)
            throw new FinixPaymentException(400, "ORDER_EMPTY", "An order must contain at least one catalog item.");

        var grouped = lines
            .GroupBy(l => l.CatalogItemId)
            .Select(g => new { CatalogItemId = g.Key, Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        if (grouped.Any(g => g.Quantity <= 0))
            throw new FinixPaymentException(400, "INVALID_QUANTITY", "Every order line must have a positive quantity.");

        var ids = grouped.Select(g => g.CatalogItemId).ToList();
        var catalogItems = await _db.CatalogItems
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var missing = ids.Where(id => !catalogItems.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new FinixPaymentException(400, "CATALOG_ITEM_NOT_FOUND", $"Unknown catalog item id(s): {string.Join(", ", missing)}.");

        var items = grouped.Select(g =>
        {
            var item = catalogItems[g.CatalogItemId];
            return new OrderItem(
                new CatalogItemOrdered(item.Id, item.Name, item.PictureUri),
                item.Price,
                g.Quantity);
        }).ToList();

        var address = new EshopAddress(
            string.IsNullOrWhiteSpace(shipping?.Street) ? "123 Main Street" : shipping!.Street!,
            string.IsNullOrWhiteSpace(shipping?.City) ? "Seattle" : shipping!.City!,
            string.IsNullOrWhiteSpace(shipping?.State) ? "WA" : shipping!.State!,
            string.IsNullOrWhiteSpace(shipping?.Country) ? "USA" : shipping!.Country!,
            string.IsNullOrWhiteSpace(shipping?.ZipCode) ? "98101" : shipping!.ZipCode!);

        var order = new Order(buyerId, address, items);
        order.SetCurrency(_options.FinixCurrency.Value);
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Order {OrderId} created for {BuyerId}, awaiting payment (total cents {TotalCents}).",
            order.Id, buyerId, ToCents(order.Total()));
        return order;
    }

    public Task<Order?> GetOrderForBuyerAsync(int orderId, string buyerId, CancellationToken ct) =>
        _db.Orders.Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.BuyerId == buyerId, ct);

    public Task<Order?> GetOrderAsync(int orderId, CancellationToken ct) =>
        _db.Orders.Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

    public async Task<IReadOnlyList<Order>> GetOrdersForBuyerAsync(string buyerId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.OrderItems)
            .Where(o => o.BuyerId == buyerId)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

    public async Task<Order> PayAsync(
        string buyerId,
        int orderId,
        int? paymentMethodId,
        CardInput? card,
        CancellationToken ct)
    {
        var order = await GetOrderForBuyerAsync(orderId, buyerId, ct)
            ?? throw new FinixPaymentException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found for this shopper.");

        if (order.PaymentStatus == PaymentStatus.Cancelled)
            throw new FinixPaymentException(409, "ORDER_CANCELLED", "A cancelled order cannot be paid.");
        if (order.IsFulfilled)
            throw new FinixPaymentException(409, "ORDER_ALREADY_FULFILLED", "The order has already been fulfilled and its payment captured.");
        if (order.IsAuthorized)
            return order;

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
            throw new FinixPaymentException(400, "INVALID_ORDER_TOTAL", "The order total must be greater than zero.");

        string sourcePaymentInstrumentId;
        int? usedPaymentMethodId = null;

        if (paymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
                p => p.Id == paymentMethodId.Value && p.BuyerId == buyerId && p.IsActive, ct)
                ?? throw new FinixPaymentException(404, "PAYMENT_METHOD_NOT_FOUND",
                    $"Saved payment method {paymentMethodId.Value} was not found for this shopper.");

            sourcePaymentInstrumentId = saved.FinixPaymentInstrumentId;
            usedPaymentMethodId = saved.Id;
        }
        else if (card is not null)
        {
            var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, ct);
            try
            {
                var instrument = await CreatePaymentInstrumentAsync(identityId, card, ct);
                sourcePaymentInstrumentId = instrument.Id!;
            }
            catch (SdkConnectionException ex)
            {
                var recovered = await TryRecoverAsync(() => FindPaymentInstrumentAsync(identityId, card, ct));
                if (recovered?.Id is null)
                    throw UnknownOutcome("pay", orderId, ex);
                sourcePaymentInstrumentId = recovered.Id;
            }
            catch (ApiException ex)
            {
                throw MapApiException(ex);
            }
        }
        else
        {
            throw new FinixPaymentException(400, "PAYMENT_SOURCE_REQUIRED",
                "Supply either card details or a saved paymentMethodId.");
        }

        var idempotencyId = AuthorizationIdempotencyId(order);
        var claim = await TryClaimAsync($"pay:{orderId}", "authorize", ct, allowRetryFailed: true);
        if (claim is null)
        {
            order = await ReloadOrderAsync(order, ct);
            if (order.IsAuthorized || order.IsFulfilled)
                return order;

            var existingAuthorization = await FindAuthorizationByIdempotencyAsync(idempotencyId, ct);
            if (existingAuthorization?.Id is not null)
            {
                ApplyAuthorization(order, existingAuthorization, amountCents);
                await _db.SaveChangesAsync(ct);
                return order;
            }

            throw new FinixPaymentException(409, "PAYMENT_IN_PROGRESS",
                "A payment for this order is already in progress. Retry shortly.");
        }

        try
        {
            var body = new AuthorizationAllFields
            {
                Amount = amountCents,
                Currency = _options.FinixCurrency,
                Merchant = _options.MerchantId,
                Source = sourcePaymentInstrumentId,
                IdempotencyId = idempotencyId,
                Tags = new Dictionary<string, string> { ["eshop_order_id"] = orderId.ToString() }
            };

            var authorization = await _client.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = AuthorizationsRequest.AuthorizationAllFields(body)
                },
                cancellationToken: ct);

            if (authorization?.Id is null)
                throw new FinixPaymentException(502, "AUTHORIZATION_NO_ID",
                    "Finix did not return an authorization id.");

            ApplyAuthorization(order, authorization, amountCents);
            claim.ReferenceId = authorization.Id;
            claim.CompletedAt = DateTimeOffset.UtcNow;
            claim.Status = order.PaymentStatus == PaymentStatus.PaymentFailed ? "Failed" : "Succeeded";
            claim.Detail = order.PaymentStatus == PaymentStatus.PaymentFailed
                ? $"{order.PaymentFailureCode}: {order.PaymentFailureMessage}"
                : null;
            await _db.SaveChangesAsync(ct);

            if (order.PaymentStatus == PaymentStatus.PaymentFailed)
            {
                throw new FinixPaymentException(402, "AUTHORIZATION_DECLINED",
                    $"Finix declined the authorization ({order.PaymentFailureCode}): {order.PaymentFailureMessage}");
            }

            _logger.LogInformation("Order {OrderId} authorized: authorization {AuthorizationId}, amount {AmountCents} {Currency}.",
                orderId, authorization.Id, amountCents, _options.FinixCurrency.Value);
            return order;
        }
        catch (SdkConnectionException ex)
        {
            var recovered = await TryRecoverAsync(() => FindAuthorizationByIdempotencyAsync(idempotencyId, ct));
            if (recovered?.Id is not null)
            {
                ApplyAuthorization(order, recovered, amountCents);
                claim.ReferenceId = recovered.Id;
                claim.Status = order.PaymentStatus == PaymentStatus.PaymentFailed ? "Failed" : "Succeeded";
                claim.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return order;
            }

            claim.Status = "Unknown";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw UnknownOutcome("authorize", orderId, ex);
        }
        catch (ApiException ex)
        {
            var mapped = MapApiException(ex);
            claim.Status = "Failed";
            claim.Detail = mapped.Message;
            await _db.SaveChangesAsync(ct);
            throw mapped;
        }
        catch (FinixPaymentException ex)
        {
            claim.Status = "Failed";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<Order> FulfilAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct)
            ?? throw new FinixPaymentException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found.");

        if (order.IsFulfilled)
            return order;
        if (order.PaymentStatus == PaymentStatus.Cancelled)
            throw new FinixPaymentException(409, "ORDER_CANCELLED", "A cancelled order cannot be fulfilled.");
        if (!order.IsAuthorized || string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
            throw new FinixPaymentException(409, "ORDER_NOT_AUTHORIZED",
                "The order has no active authorization. Pay for the order before fulfilling it.");

        var authorizationId = order.FinixAuthorizationId!;

        // Recover a capture that Finix already performed (e.g. an earlier response was lost).
        FinixAuthorization? preRead;
        try
        {
            preRead = await GetAuthorizationAsync(authorizationId, ct);
            if (preRead?.Transfer is not null)
            {
                var existingTransfer = await GetTransferAsync(preRead.Transfer, ct);
                if (existingTransfer?.Id is not null)
                {
                    ApplyCapture(order, existingTransfer);
                    await _db.SaveChangesAsync(ct);
                    return order;
                }
            }
        }
        catch (ApiException ex)
        {
            throw MapApiException(ex);
        }
        catch (SdkConnectionException)
        {
            preRead = null;
        }
        if (preRead is not null && IsNotCapturable(preRead))
            throw NotCapturable(authorizationId, preRead);

        var claim = await TryClaimAsync($"capture:{orderId}", "capture", ct, allowRetryFailed: true);
        if (claim is null)
        {
            order = await ReloadOrderAsync(order, ct);
            if (order.IsFulfilled)
                return order;

            var authorization = await GetAuthorizationAsync(authorizationId, ct);
            if (authorization?.Transfer is not null)
            {
                var transfer = await GetTransferAsync(authorization.Transfer, ct);
                if (transfer?.Id is not null)
                {
                    ApplyCapture(order, transfer);
                    await _db.SaveChangesAsync(ct);
                    return order;
                }
            }
            throw new FinixPaymentException(409, "CAPTURE_IN_PROGRESS",
                "A capture for this order is already in progress. Retry shortly.");
        }

        var captureAmount = order.AuthorizedAmountCents ?? ToCents(order.Total());
        try
        {
            var captured = await _client.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = authorizationId,
                    Body = AuthorizationsRequest1.CaptureAuthorization(
                        new CaptureAuthorization { CaptureAmount = captureAmount })
                },
                cancellationToken: ct);

            var captureTransferId = captured?.Transfer;
            if (string.IsNullOrWhiteSpace(captureTransferId))
            {
                var refreshed = await GetAuthorizationAsync(authorizationId, ct);
                captureTransferId = refreshed?.Transfer;
            }

            if (string.IsNullOrWhiteSpace(captureTransferId))
                throw new FinixPaymentException(502, "CAPTURE_NO_TRANSFER_ID",
                    "Finix accepted the capture but did not return a capture transfer id.");

            var transfer = await GetTransferAsync(captureTransferId, ct)
                ?? throw new FinixPaymentException(502, "CAPTURE_TRANSFER_UNREADABLE",
                    $"Finix capture transfer {captureTransferId} could not be read.");

            ApplyCapture(order, transfer);
            claim.ReferenceId = captureTransferId;
            claim.Status = "Succeeded";
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Order {OrderId} fulfilled: capture {CaptureTransferId}, amount {Captured}, fee {Fee}, net {Net}.",
                orderId, captureTransferId, order.CapturedAmountCents, order.FinixFeeCents, order.NetProceedsCents);
            return order;
        }
        catch (SdkConnectionException ex)
        {
            var authorization = await TryRecoverAsync(() => GetAuthorizationAsync(authorizationId, ct));
            if (authorization?.Transfer is not null)
            {
                var transfer = await TryRecoverAsync(() => GetTransferAsync(authorization.Transfer, ct));
                if (transfer?.Id is not null)
                {
                    ApplyCapture(order, transfer);
                    claim.ReferenceId = transfer.Id;
                    claim.Status = "Succeeded";
                    claim.CompletedAt = DateTimeOffset.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    return order;
                }
            }

            claim.Status = "Unknown";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw UnknownOutcome("capture", orderId, ex);
        }
        catch (ApiException ex)
        {
            var mapped = MapApiException(ex);
            claim.Status = "Failed";
            claim.Detail = mapped.Message;
            await _db.SaveChangesAsync(ct);

            var authorization = await TryRecoverAsync(() => GetAuthorizationAsync(authorizationId, ct));
            if (authorization is not null && IsNotCapturable(authorization))
                throw NotCapturable(authorizationId, authorization);
            throw mapped;
        }
        catch (FinixPaymentException ex)
        {
            claim.Status = "Failed";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<Order> CancelAsync(int orderId, CancellationToken ct)
    {
        var order = await LoadOrderAsync(orderId, ct)
            ?? throw new FinixPaymentException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found.");

        if (order.PaymentStatus == PaymentStatus.Cancelled)
            return order;
        if (order.IsFulfilled)
            throw new FinixPaymentException(409, "ORDER_ALREADY_FULFILLED",
                "The payment has already been captured. Use the refunds endpoint to return the money.");

        if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            order.MarkCancelled();
            await _db.SaveChangesAsync(ct);
            return order;
        }

        var authorizationId = order.FinixAuthorizationId!;
        var claim = await TryClaimAsync($"void:{orderId}", "void", ct, allowRetryFailed: true);
        if (claim is null)
        {
            order = await ReloadOrderAsync(order, ct);
            if (order.PaymentStatus == PaymentStatus.Cancelled)
                return order;

            var current = await TryRecoverAsync(() => GetAuthorizationAsync(authorizationId, ct));
            if (current is not null && (current.IsVoid == true || current.State == State1.Canceled))
            {
                order.MarkCancelled();
                await _db.SaveChangesAsync(ct);
                return order;
            }
            throw new FinixPaymentException(409, "CANCEL_IN_PROGRESS",
                "A cancellation for this order is already in progress. Retry shortly.");
        }

        try
        {
            var authorization = await _client.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = authorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: ct);

            order.MarkCancelled();
            claim.ReferenceId = authorizationId;
            claim.Status = "Succeeded";
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Order {OrderId} cancelled: authorization {AuthorizationId} voided.",
                orderId, authorizationId);
            return order;
        }
        catch (SdkConnectionException ex)
        {
            var current = await TryRecoverAsync(() => GetAuthorizationAsync(authorizationId, ct));
            if (current is not null && (current.IsVoid == true || current.State == State1.Canceled))
            {
                order.MarkCancelled();
                claim.Status = "Succeeded";
                claim.ReferenceId = authorizationId;
                claim.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return order;
            }

            claim.Status = "Unknown";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw UnknownOutcome("void", orderId, ex);
        }
        catch (ApiException ex)
        {
            var mapped = MapApiException(ex);
            var current = await TryRecoverAsync(() => GetAuthorizationAsync(authorizationId, ct));
            if (current is not null && (current.IsVoid == true || current.State == State1.Canceled))
            {
                order.MarkCancelled();
                claim.Status = "Succeeded";
                claim.ReferenceId = authorizationId;
                claim.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return order;
            }

            claim.Status = "Failed";
            claim.Detail = mapped.Message;
            await _db.SaveChangesAsync(ct);
            throw mapped;
        }
        catch (FinixPaymentException ex)
        {
            claim.Status = "Failed";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<RefundRecord> RefundAsync(
        int orderId,
        long? amountCents,
        string idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new FinixPaymentException(400, "IDEMPOTENCY_KEY_REQUIRED",
                "A caller-supplied idempotencyKey is required for refunds.");
        if (idempotencyKey.Length > 128)
            throw new FinixPaymentException(400, "IDEMPOTENCY_KEY_TOO_LONG",
                "idempotencyKey must be 128 characters or fewer.");

        await RefundLock.WaitAsync(ct);
        try
        {
            var order = await LoadOrderAsync(orderId, ct)
                ?? throw new FinixPaymentException(404, "ORDER_NOT_FOUND", $"Order {orderId} was not found.");

            if (!order.IsFulfilled || string.IsNullOrWhiteSpace(order.CaptureTransferId))
                throw new FinixPaymentException(409, "ORDER_NOT_CAPTURED",
                    "Only a fulfilled order with a captured payment can be refunded.");

            var claimKey = $"refund:{orderId}:{idempotencyKey}";
            var existing = await _db.RefundRecords.AsNoTracking()
                .FirstOrDefaultAsync(r => r.ClaimKey == claimKey, ct);
            if (existing is not null)
            {
                if (existing.State != "UNKNOWN")
                    return existing;

                var recovered = await TryRecoverAsync(
                    () => FindRefundAsync(order.CaptureTransferId!, existing.AmountCents, existing.CreatedAt, ct));
                if (recovered?.Id is not null)
                {
                    var tracked = await _db.RefundRecords
                        .FirstAsync(r => r.ClaimKey == claimKey, ct);
                    tracked.FinixRefundTransferId = recovered.Id;
                    tracked.State = recovered.State?.Value;
                    order.RecordRefund(existing.AmountCents);
                    await _db.SaveChangesAsync(ct);
                    return tracked;
                }

                throw new FinixPaymentException(503, "FINIX_OUTCOME_UNKNOWN",
                    "The earlier refund attempt may or may not have completed, and Finix could not confirm it yet. " +
                    "Retry the same request later; the stored refund claim prevents a second refund.");
            }

            var remaining = order.RemainingRefundableCents;
            if (remaining <= 0)
                throw new FinixPaymentException(409, "ORDER_FULLY_REFUNDED",
                    "The captured amount has already been fully refunded.");

            var amount = amountCents ?? remaining;
            if (amount <= 0)
                throw new FinixPaymentException(400, "INVALID_REFUND_AMOUNT",
                    "The refund amount must be greater than zero.");
            if (amount > remaining)
                throw new FinixPaymentException(409, "REFUND_EXCEEDS_CAPTURED",
                    $"The refund amount {amount} exceeds the remaining refundable amount {remaining}.");
            if (amount > int.MaxValue)
                throw new FinixPaymentException(400, "REFUND_AMOUNT_TOO_LARGE",
                    "The refund amount is too large for the Finix refund request.");

            var record = new RefundRecord
            {
                ClaimKey = claimKey,
                OrderId = orderId,
                IdempotencyKey = idempotencyKey,
                AmountCents = amount
            };
            _db.RefundRecords.Add(record);
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(record).State = EntityState.Detached;
                var raced = await _db.RefundRecords.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.ClaimKey == claimKey, ct);
                if (raced is not null)
                    return raced;
                throw new FinixPaymentException(409, "REFUND_IN_PROGRESS",
                    "A refund with this idempotency key is already in progress. Retry shortly.");
            }

            try
            {
                // Finix allows a full reversal while the capture transfer is still PENDING,
                // but a partial reversal requires the capture to have SUCCEEDED. Operator
                // refunds are asynchronous anyway, so wait (bounded) for settlement.
                if (amount < order.CapturedAmountCents)
                {
                    var capture = await GetTransferAsync(order.CaptureTransferId!, ct);
                    if (capture?.State == State11.Pending)
                        capture = await WaitForCaptureSettledAsync(order.CaptureTransferId!, ct);

                    if (capture?.State == State11.Failed || capture?.State == State11.Canceled)
                        throw new FinixPaymentException(409, "CAPTURE_NOT_REFUNDABLE",
                            $"The capture transfer {order.CaptureTransferId} is {capture.State?.Value} at Finix and cannot be refunded.");

                    if (capture?.State == State11.Pending)
                        throw new FinixPaymentException(409, "CAPTURE_PENDING",
                            $"The capture transfer {order.CaptureTransferId} is still PENDING at Finix. " +
                            "Partial refunds are allowed once it reaches SUCCEEDED; retry shortly. " +
                            "A full refund is allowed while the capture is pending.");
                }

                var transfer = await _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = order.CaptureTransferId,
                        Body = TransfersReversalsRequest.RefundTransfer(
                            new RefundTransfer { RefundAmount = (int)amount })
                    },
                    cancellationToken: ct);

                if (transfer?.Id is null)
                    throw new FinixPaymentException(502, "REFUND_NO_ID",
                        "Finix did not return a refund transfer id.");

                record.FinixRefundTransferId = transfer.Id;
                record.State = transfer.State?.Value;
                record.FailureCode = transfer.FailureCode;
                record.FailureMessage = transfer.FailureMessage;

                if (transfer.State == State11.Failed)
                {
                    await _db.SaveChangesAsync(ct);
                    throw new FinixPaymentException(402, "REFUND_FAILED",
                        $"Finix refused the refund ({transfer.FailureCode}): {transfer.FailureMessage}");
                }

                order.RecordRefund(amount);
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation("Order {OrderId} refunded {AmountCents} cents: refund transfer {RefundId}.",
                    orderId, amount, transfer.Id);
                return record;
            }
            catch (SdkConnectionException ex)
            {
                var recovered = await TryRecoverAsync(() => FindRefundAsync(order.CaptureTransferId!, amount, record.CreatedAt, ct));
                if (recovered?.Id is not null)
                {
                    record.FinixRefundTransferId = recovered.Id;
                    record.State = recovered.State?.Value;
                    order.RecordRefund(amount);
                    await _db.SaveChangesAsync(ct);
                    return record;
                }

                record.State = "UNKNOWN";
                record.FailureMessage = ex.Message;
                await _db.SaveChangesAsync(ct);
                throw UnknownOutcome("refund", orderId, ex);
            }
            catch (ApiException ex)
            {
                var mapped = MapApiException(ex);
                _db.RefundRecords.Remove(record);
                await _db.SaveChangesAsync(ct);
                throw mapped;
            }
            catch (FinixPaymentException ex)
            {
                _db.RefundRecords.Remove(record);
                await _db.SaveChangesAsync(ct);
                throw;
            }
        }
        finally
        {
            RefundLock.Release();
        }
    }

    // ----------------------------------------------------------- saved cards

    public async Task<SavedPaymentMethod> SaveCardAsync(string buyerId, CardInput card, CancellationToken ct)
    {
        ValidateCardInput(card);
        var identityId = await GetOrCreateBuyerIdentityAsync(buyerId, ct);

        var cardHash = HashCard(card);
        var claimKey = $"card:{buyerId}:{cardHash}";
        var claim = await TryClaimAsync(claimKey, "create-card", ct, allowRetryFailed: true);
        if (claim is null)
        {
            var prior = await _db.FinixOperationClaims.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClaimKey == claimKey, ct);
            if (prior?.ReferenceId is not null)
            {
                var existing = await _db.SavedPaymentMethods
                    .FirstOrDefaultAsync(p => p.BuyerId == buyerId &&
                                              p.FinixPaymentInstrumentId == prior.ReferenceId &&
                                              p.IsActive, ct);
                if (existing is not null)
                    return existing;
            }

            throw new FinixPaymentException(409, "CARD_SAVE_IN_PROGRESS",
                "This card is already being saved. Retry shortly.");
        }

        try
        {
            var instrument = await CreatePaymentInstrumentAsync(identityId, card, ct);
            var saved = new SavedPaymentMethod(
                buyerId,
                instrument.Id!,
                instrument.Brand?.Value,
                instrument.LastFour,
                instrument.ExpirationMonth,
                instrument.ExpirationYear,
                instrument.Name,
                instrument.Fingerprint);

            _db.SavedPaymentMethods.Add(saved);
            claim.ReferenceId = instrument.Id;
            claim.Status = "Succeeded";
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Shopper {BuyerId} saved payment instrument {PaymentInstrumentId} ({Brand} ****{LastFour}).",
                buyerId, instrument.Id, instrument.Brand?.Value, instrument.LastFour);
            return saved;
        }
        catch (SdkConnectionException ex)
        {
            var recovered = await TryRecoverAsync(() => FindPaymentInstrumentAsync(identityId, card, ct));
            if (recovered?.Id is not null)
            {
                var saved = new SavedPaymentMethod(
                    buyerId,
                    recovered.Id,
                    recovered.Brand?.Value,
                    recovered.LastFour,
                    recovered.ExpirationMonth,
                    recovered.ExpirationYear,
                    recovered.Name,
                    recovered.Fingerprint);

                _db.SavedPaymentMethods.Add(saved);
                claim.ReferenceId = recovered.Id;
                claim.Status = "Succeeded";
                claim.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return saved;
            }

            claim.Status = "Unknown";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw UnknownOutcome("create-card", 0, ex);
        }
        catch (ApiException ex)
        {
            var mapped = MapApiException(ex);
            claim.Status = "Failed";
            claim.Detail = mapped.Message;
            await _db.SaveChangesAsync(ct);
            throw mapped;
        }
        catch (FinixPaymentException ex)
        {
            claim.Status = "Failed";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetCardsAsync(string buyerId, CancellationToken ct) =>
        await _db.SavedPaymentMethods
            .Where(p => p.BuyerId == buyerId && p.IsActive)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(ct);

    public async Task DeleteCardAsync(string buyerId, int paymentMethodId, CancellationToken ct)
    {
        var saved = await _db.SavedPaymentMethods.FirstOrDefaultAsync(
            p => p.Id == paymentMethodId && p.BuyerId == buyerId, ct)
            ?? throw new FinixPaymentException(404, "PAYMENT_METHOD_NOT_FOUND",
                $"Saved payment method {paymentMethodId} was not found for this shopper.");

        try
        {
            await _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = saved.FinixPaymentInstrumentId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                },
                cancellationToken: ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone at Finix: removing the local row is still correct.
        }
        catch (SdkConnectionException ex)
        {
            var instrument = await TryRecoverAsync(() => GetPaymentInstrumentAsync(saved.FinixPaymentInstrumentId, ct));
            if (instrument is null || instrument.Enabled != false)
                throw UnknownOutcome("disable-card", saved.Id, ex);
        }
        catch (ApiException ex)
        {
            throw MapApiException(ex);
        }

        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Shopper {BuyerId} removed saved payment method {PaymentMethodId}.",
            buyerId, paymentMethodId);
    }

    // -------------------------------------------------------- reconciliation

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken ct)
    {
        if (to < from)
            throw new FinixPaymentException(400, "INVALID_DATE_RANGE", "'to' must be on or after 'from'.");

        var report = new ReconciliationReport { From = from, To = to };
        string? cursor = null;

        try
        {
            do
            {
                var page = await _client.TransfersApi.ListTransfers(
                    new ListTransfersRequest
                    {
                        CreatedAtGte = from,
                        CreatedAtLte = to,
                        Limit = PageLimit,
                        AfterCursor = cursor
                    },
                    requestOptions: new RequestOptions
                    {
                        Hooks = [SdkHook.OnRequest((request, _) => NormalizeDateRangeQuery(request))]
                    },
                    cancellationToken: ct);

                report.PagesFetched++;
                if (page?.Embedded?.Transfers is { } transfers)
                    report.Transactions.AddRange(transfers.Select(ToReconciliationTransaction));

                cursor = page?.Page?.NextCursor;
                if (report.PagesFetched >= MaxListPages && !string.IsNullOrEmpty(cursor))
                {
                    report.Truncated = true;
                    break;
                }
            }
            while (!string.IsNullOrEmpty(cursor));
        }
        catch (ApiException ex)
        {
            throw MapApiException(ex);
        }
        catch (SdkConnectionException ex)
        {
            throw UnknownOutcome("reconciliation", 0, ex);
        }

        var localOrders = await _db.Orders
            .Where(o => o.CaptureTransferId != null)
            .ToListAsync(ct);
        var localRefunds = await _db.RefundRecords
            .Where(r => r.FinixRefundTransferId != null)
            .ToListAsync(ct);

        var ordersByTransfer = localOrders
            .Where(o => o.CaptureTransferId is not null)
            .ToDictionary(o => o.CaptureTransferId!, o => o);
        var refundsByTransfer = localRefunds
            .Where(r => r.FinixRefundTransferId is not null)
            .ToDictionary(r => r.FinixRefundTransferId!, r => r);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var transaction in report.Transactions)
        {
            if (string.IsNullOrEmpty(transaction.TransferId))
                continue;
            seen.Add(transaction.TransferId);

            if (ordersByTransfer.TryGetValue(transaction.TransferId, out var order))
            {
                transaction.MatchedOrderId = order.Id;
                transaction.MatchKind = "capture";
            }
            else if (refundsByTransfer.TryGetValue(transaction.TransferId, out var refund))
            {
                transaction.MatchedOrderId = refund.OrderId;
                transaction.MatchedRefundId = refund.FinixRefundTransferId;
                transaction.MatchKind = "refund";
            }
        }

        foreach (var order in localOrders)
        {
            if (order.CaptureTransferId is null || seen.Contains(order.CaptureTransferId))
                continue;
            report.UnmatchedEshop.Add(new UnmatchedEshopTransaction
            {
                Kind = "capture",
                OrderId = order.Id,
                TransferId = order.CaptureTransferId,
                AmountCents = order.CapturedAmountCents ?? 0
            });
        }

        foreach (var refund in localRefunds)
        {
            if (refund.FinixRefundTransferId is null || seen.Contains(refund.FinixRefundTransferId))
                continue;
            report.UnmatchedEshop.Add(new UnmatchedEshopTransaction
            {
                Kind = "refund",
                OrderId = refund.OrderId,
                RefundId = refund.FinixRefundTransferId,
                TransferId = refund.FinixRefundTransferId,
                AmountCents = refund.AmountCents
            });
        }

        return report;
    }

    // ------------------------------------------------------------- Finix I/O

    private async Task<string> GetOrCreateBuyerIdentityAsync(string buyerId, CancellationToken ct)
    {
        var profile = await _db.FinixBuyerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, ct);
        if (profile is not null)
            return profile.FinixIdentityId;

        var claim = await TryClaimAsync($"identity:{buyerId}", "identity", ct, allowRetryFailed: true);
        if (claim is null)
        {
            profile = await _db.FinixBuyerProfiles.AsNoTracking()
                .FirstOrDefaultAsync(p => p.BuyerId == buyerId, ct);
            if (profile is not null)
                return profile.FinixIdentityId;

            throw new FinixPaymentException(409, "IDENTITY_IN_PROGRESS",
                "The shopper's Finix identity is already being created. Retry shortly.");
        }

        try
        {
            var buyer = new Buyer
            {
                Entity = new Entity5 { Email = buyerId },
                IdentityRoles = [IdentityRole5.Buyer],
                Type = Type10.Personal,
                Tags = new Dictionary<string, string> { ["eshop_buyer_id"] = buyerId }
            };

            var identity = await _client.Identities.CreateIdentity(
                new CreateIdentityRequest { Body = IdentitiesRequest.Buyer(buyer) },
                cancellationToken: ct);

            if (identity?.Id is null)
                throw new FinixPaymentException(502, "IDENTITY_NO_ID",
                    "Finix did not return an identity id.");

            _db.FinixBuyerProfiles.Add(new FinixBuyerProfile
            {
                BuyerId = buyerId,
                FinixIdentityId = identity.Id
            });
            claim.ReferenceId = identity.Id;
            claim.Status = "Succeeded";
            claim.CompletedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return identity.Id;
        }
        catch (SdkConnectionException ex)
        {
            var recovered = await TryRecoverAsync(() => FindIdentityAsync(buyerId, ct));
            if (recovered?.Id is not null)
            {
                _db.FinixBuyerProfiles.Add(new FinixBuyerProfile
                {
                    BuyerId = buyerId,
                    FinixIdentityId = recovered.Id
                });
                claim.ReferenceId = recovered.Id;
                claim.Status = "Succeeded";
                claim.CompletedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return recovered.Id;
            }

            claim.Status = "Unknown";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw UnknownOutcome("identity", 0, ex);
        }
        catch (ApiException ex)
        {
            var mapped = MapApiException(ex);
            claim.Status = "Failed";
            claim.Detail = mapped.Message;
            await _db.SaveChangesAsync(ct);
            throw mapped;
        }
        catch (FinixPaymentException ex)
        {
            claim.Status = "Failed";
            claim.Detail = ex.Message;
            await _db.SaveChangesAsync(ct);
            throw;
        }
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(
        string identityId,
        CardInput card,
        CancellationToken ct)
    {
        ValidateCardInput(card);

        var body = new PaymentInstrumentCard1
        {
            Number = DigitsOnly(card.Number),
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identityId,
            Name = card.Name,
            SecurityCode = card.SecurityCode,
            Address = MapAddress(card.Address)
        };

        var result = await _client.PaymentInstrumentsApi.CreatePaymentInstrument(
            new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(body)
            },
            cancellationToken: ct);

        if (!result.TryGetPaymentInstrumentCard(out var instrument) || instrument?.Id is null)
            throw new FinixPaymentException(502, "PAYMENT_INSTRUMENT_NO_ID",
                "Finix did not return a payment instrument id.");

        return instrument;
    }

    private async Task<FinixAuthorization?> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _client.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId },
                cancellationToken: ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Transfer?> GetTransferAsync(string transferId, CancellationToken ct)
    {
        try
        {
            return await _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: ct);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<Transfer?> WaitForCaptureSettledAsync(string transferId, CancellationToken ct)
    {
        Transfer? transfer = null;
        for (var attempt = 0; attempt < 32; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            transfer = await GetTransferAsync(transferId, ct);
            if (transfer?.State != State11.Pending)
                return transfer;
        }
        return transfer;
    }

    private async Task<PaymentInstrumentCard?> GetPaymentInstrumentAsync(string paymentInstrumentId, CancellationToken ct)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: ct);
            return response.TryGetPaymentInstrumentCard(out var card) ? card : null;
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<FinixAuthorization?> FindAuthorizationByIdempotencyAsync(string idempotencyId, CancellationToken ct)
    {
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < MaxListPages; pageNumber++)
        {
            var page = await _client.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    IdempotencyId = idempotencyId,
                    Limit = PageLimit,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            var match = page?.Embedded?.Authorizations?
                .FirstOrDefault(a => string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
            if (match is not null)
                return match;

            cursor = page?.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                return null;
        }
        return null;
    }

    private async Task<FinixIdentity?> FindIdentityAsync(string buyerId, CancellationToken ct)
    {
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < MaxListPages; pageNumber++)
        {
            var page = await _client.Identities.ListIdentities(
                new ListIdentitiesRequest
                {
                    TagsKey = "eshop_buyer_id",
                    TagsValue = buyerId,
                    Limit = PageLimit,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            var match = page?.Embedded?.Identities?.FirstOrDefault();
            if (match is not null)
                return match;

            cursor = page?.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                return null;
        }
        return null;
    }

    private async Task<PaymentInstrumentCard?> FindPaymentInstrumentAsync(
        string identityId,
        CardInput card,
        CancellationToken ct)
    {
        var lastFour = DigitsOnly(card.Number);
        lastFour = lastFour.Length >= 4 ? lastFour[^4..] : lastFour;
        string? cursor = null;

        for (var pageNumber = 0; pageNumber < MaxListPages; pageNumber++)
        {
            var page = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                new ListPaymentInstrumentsRequest
                {
                    OwnerIdentityId = identityId,
                    Last4 = lastFour,
                    Limit = PageLimit,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            var instruments = page?.Embedded?.PaymentInstruments;
            if (instruments is not null)
            {
                foreach (var item in instruments)
                {
                    if (!item.TryGetPaymentInstrumentCard(out var candidate) || candidate is null)
                        continue;
                    if (candidate.ExpirationMonth != card.ExpirationMonth ||
                        candidate.ExpirationYear != card.ExpirationYear)
                        continue;
                    return candidate;
                }
            }

            cursor = page?.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                return null;
        }
        return null;
    }

    private async Task<Transfer?> FindRefundAsync(
        string captureTransferId,
        long amountCents,
        DateTimeOffset notBefore,
        CancellationToken ct)
    {
        string? cursor = null;
        for (var pageNumber = 0; pageNumber < MaxListPages; pageNumber++)
        {
            var page = await _client.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    ParentTransferId = captureTransferId,
                    Limit = PageLimit,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            var match = page?.Embedded?.Transfers?
                .Where(t => t.Type == Type31.Reversal && t.Amount == amountCents &&
                            (t.CreatedAt is null || t.CreatedAt >= notBefore.AddSeconds(-5)))
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();
            if (match is not null)
                return match;

            cursor = page?.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
                return null;
        }
        return null;
    }

    // ------------------------------------------------------------- helpers

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken ct) =>
        await _db.Orders.Include(o => o.OrderItems).FirstOrDefaultAsync(o => o.Id == orderId, ct);

    private async Task<Order> ReloadOrderAsync(Order order, CancellationToken ct)
    {
        _db.ChangeTracker.Clear();
        var reloaded = await LoadOrderAsync(order.Id, ct);
        if (reloaded is null)
            throw new FinixPaymentException(404, "ORDER_NOT_FOUND", $"Order {order.Id} was not found.");
        return reloaded;
    }

    private async Task<T?> TryRecoverAsync<T>(Func<Task<T?>> read) where T : class
    {
        try
        {
            return await read();
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex, "Finix recovery read failed; the outcome remains unknown.");
            return null;
        }
    }

    private async Task<FinixOperationClaim?> TryClaimAsync(
        string claimKey,
        string operation,
        CancellationToken ct,
        bool allowRetryFailed)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var claim = new FinixOperationClaim
            {
                ClaimKey = claimKey,
                Operation = operation,
                Status = "Pending"
            };
            _db.FinixOperationClaims.Add(claim);
            try
            {
                await _db.SaveChangesAsync(ct);
                return claim;
            }
            catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException or ArgumentException)
            {
                _db.Entry(claim).State = EntityState.Detached;
            }

            if (!allowRetryFailed)
                return null;

            var existing = await _db.FinixOperationClaims.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClaimKey == claimKey, ct);
            if (existing is null)
                continue;

            if (existing.Status == "Failed")
            {
                _db.FinixOperationClaims.Remove(existing);
                try
                {
                    await _db.SaveChangesAsync(ct);
                }
                catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
                {
                    _db.Entry(existing).State = EntityState.Detached;
                }
                continue;
            }

            return null;
        }

        return null;
    }

    private void ApplyAuthorization(Order order, FinixAuthorization authorization, long requestedAmountCents)
    {
        if (authorization.Id is null)
            throw new FinixPaymentException(502, "AUTHORIZATION_NO_ID",
                "Finix did not return an authorization id.");

        order.MarkAuthorized(
            authorization.Id,
            authorization.State?.Value,
            authorization.Amount ?? requestedAmountCents,
            authorization.Currency?.Value ?? _options.FinixCurrency.Value);

        if (authorization.State == State1.Failed)
            order.MarkPaymentFailed(authorization.FailureCode, authorization.FailureMessage);
    }

    private static void ApplyCapture(Order order, Transfer transfer)
    {
        if (transfer.Id is null)
            throw new FinixPaymentException(502, "CAPTURE_NO_ID", "Finix did not return a capture transfer id.");

        var amount = transfer.Amount ?? order.AuthorizedAmountCents ?? ToCents(order.Total());
        var fee = transfer.Fee ?? 0;
        order.MarkFulfilled(transfer.Id, transfer.State?.Value, amount, fee, amount - fee);
    }

    private static bool IsNotCapturable(FinixAuthorization authorization) =>
        authorization.IsVoid == true ||
        authorization.State == State1.Canceled ||
        authorization.State == State1.Failed;

    private static FinixPaymentException NotCapturable(string authorizationId, FinixAuthorization authorization)
    {
        var reason = authorization.IsVoid == true
            ? "the hold was voided"
            : authorization.State == State1.Canceled
                ? "the hold was canceled"
                : authorization.State == State1.Failed
                    ? "the authorization failed"
                    : "the hold is no longer capturable";
        var failure = string.IsNullOrWhiteSpace(authorization.FailureMessage)
            ? string.Empty
            : $" Finix said: {authorization.FailureMessage}";

        return new FinixPaymentException(409, "AUTHORIZATION_NOT_CAPTURABLE",
            $"The authorization {authorizationId} can no longer be captured ({reason}; " +
            $"state={authorization.State?.Value ?? "UNKNOWN"}, is_void={authorization.IsVoid}, " +
            $"failure_code={authorization.FailureCode ?? "none"}).{failure} " +
            "Operator action: cancel the order to release any remaining hold, or ask the shopper to pay again.");
    }

    private static FinixPaymentException UnknownOutcome(string operation, int orderId, SdkConnectionException ex) =>
        new(503, "FINIX_OUTCOME_UNKNOWN",
            $"The Finix {operation} call for order {orderId} may or may not have completed; the connection failed before a response was read. " +
            "Retry the same request: the service re-reads Finix and the stored claim prevents a duplicate write.",
            ex);

    private static ReconciliationTransaction ToReconciliationTransaction(Transfer transfer) => new()
    {
        TransferId = transfer.Id ?? string.Empty,
        Type = transfer.Type?.Value,
        State = transfer.State?.Value,
        AmountCents = transfer.Amount,
        FeeCents = transfer.Fee,
        Currency = transfer.Currency?.Value,
        CreatedAt = transfer.CreatedAt,
        ParentTransferId = transfer.ParentTransfer
    };

    private FinixPaymentException MapApiException(ApiException ex)
    {
        if (ex is ResponseDeserializationException)
        {
            var rawStatus = (int)ex.StatusCode;
            var httpStatus = rawStatus is >= 200 and < 300
                ? 502
                : rawStatus is 401 or 403 ? 502 : rawStatus;
            return new FinixPaymentException(httpStatus, "FINIX_RESPONSE_UNREADABLE",
                "Finix returned a response that could not be read.", ex)
            {
                ProviderStatusCode = rawStatus
            };
        }

        var (code, message, logref) = ExtractGenericError(ex);
        var providerStatus = (int)ex.StatusCode;
        var mappedStatus = providerStatus switch
        {
            401 or 403 => 502,
            429 => 503,
            >= 400 and < 500 => providerStatus,
            _ => 502
        };

        var text = string.IsNullOrWhiteSpace(message)
            ? $"Finix rejected the request with HTTP {providerStatus}."
            : message!;

        _logger.LogWarning("Finix call failed: HTTP {Status}, code {Code}, logref {LogRef}: {Message}",
            providerStatus, code, logref, text);

        return new FinixPaymentException(mappedStatus, code ?? "FINIX_ERROR", text, ex)
        {
            ProviderStatusCode = providerStatus,
            LogRef = logref
        };
    }

    private static (string? Code, string? Message, string? LogRef) ExtractGenericError(ApiException ex)
    {
        GenericError? generic = ex switch
        {
            ApiException<FinixApiReference.Errors.CreateIdentityError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.CreatePaymentInstrumentError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.GetPaymentInstrumentError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.ListPaymentInstrumentsError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.UpdatePaymentInstrumentError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.CreateAuthorizationError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.GetAuthorizationError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.ListAuthorizationsError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.CaptureAuthorizationError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.VoidAuthorizationError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.GetTransferError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.CreateTransferReversalError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.ListTransfersError> e
                when e.Error.TryGetGenericError(out var g) => g,
            ApiException<FinixApiReference.Errors.ListIdentitiesError> e
                when e.Error.TryGetGenericError(out var g) => g,
            _ => null
        };

        var error = generic?.Embedded?.Errors?.FirstOrDefault();
        if (error is not null)
            return (error.Code, error.Message, error.Logref);

        var raw = TryGetRawError(ex);
        return (null, raw?.ReadAsString(), null);
    }

    private static RawError? TryGetRawError(ApiException ex)
    {
        ApiError? apiError = ex switch
        {
            ApiException<FinixApiReference.Errors.CreateIdentityError> e => e.Error,
            ApiException<FinixApiReference.Errors.CreatePaymentInstrumentError> e => e.Error,
            ApiException<FinixApiReference.Errors.GetPaymentInstrumentError> e => e.Error,
            ApiException<FinixApiReference.Errors.ListPaymentInstrumentsError> e => e.Error,
            ApiException<FinixApiReference.Errors.UpdatePaymentInstrumentError> e => e.Error,
            ApiException<FinixApiReference.Errors.CreateAuthorizationError> e => e.Error,
            ApiException<FinixApiReference.Errors.GetAuthorizationError> e => e.Error,
            ApiException<FinixApiReference.Errors.ListAuthorizationsError> e => e.Error,
            ApiException<FinixApiReference.Errors.CaptureAuthorizationError> e => e.Error,
            ApiException<FinixApiReference.Errors.VoidAuthorizationError> e => e.Error,
            ApiException<FinixApiReference.Errors.GetTransferError> e => e.Error,
            ApiException<FinixApiReference.Errors.CreateTransferReversalError> e => e.Error,
            ApiException<FinixApiReference.Errors.ListTransfersError> e => e.Error,
            ApiException<FinixApiReference.Errors.ListIdentitiesError> e => e.Error,
            _ => null
        };

        return apiError is not null && apiError.TryGetRawError(out var raw) ? raw : null;
    }

    private static void ValidateCardInput(CardInput card)
    {
        if (string.IsNullOrWhiteSpace(card.Number) || DigitsOnly(card.Number).Length < 12)
            throw new FinixPaymentException(400, "INVALID_CARD_NUMBER", "A valid card number is required.");
        if (card.ExpirationMonth is < 1 or > 12)
            throw new FinixPaymentException(400, "INVALID_EXPIRATION_MONTH", "expirationMonth must be between 1 and 12.");
        if (card.ExpirationYear < 2000 || card.ExpirationYear > 2100)
            throw new FinixPaymentException(400, "INVALID_EXPIRATION_YEAR", "expirationYear must be a four-digit year.");
        if (string.IsNullOrWhiteSpace(card.Name))
            throw new FinixPaymentException(400, "CARDHOLDER_NAME_REQUIRED", "The cardholder name is required.");
    }

    private static FinixAddress? MapAddress(CardAddressInput? address)
    {
        if (address is null)
            return null;

        if (string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Region) ||
            string.IsNullOrWhiteSpace(address.PostalCode) ||
            string.IsNullOrWhiteSpace(address.Country))
        {
            throw new FinixPaymentException(400, "INVALID_CARD_ADDRESS",
                "A card address requires line1, city, region, postalCode and country.");
        }

        var countryCode = address.Country.Trim().ToUpperInvariant();
        if (!Country.TryGetKnownValue(countryCode, out var country))
            throw new FinixPaymentException(400, "INVALID_CARD_ADDRESS_COUNTRY",
                $"'{address.Country}' is not a recognised ISO-3166 alpha-3 country code.");

        return new FinixAddress
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = country
        };
    }

    private static string DigitsOnly(string value) =>
        new(value.Where(char.IsDigit).ToArray());

    private static string AuthorizationIdempotencyId(Order order) =>
        $"eshop-order-{order.Id}-{order.OrderDate.UtcTicks}-auth";

    /// <summary>
    /// Finix's date filters expect <c>yyyy-MM-ddTHH:mm:ss</c> without an offset, while the
    /// SDK serializes <see cref="DateTimeOffset"/> with one. Rewrite only the date-range keys
    /// on the reconciliation call, leaving every other parameter untouched.
    /// </summary>
    private static void NormalizeDateRangeQuery(System.Net.Http.HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null || string.IsNullOrEmpty(uri.Query) || uri.Query.Length <= 1)
            return;

        var pairs = uri.Query.TrimStart('?').Split('&');
        var changed = false;
        for (var i = 0; i < pairs.Length; i++)
        {
            var separator = pairs[i].IndexOf('=');
            if (separator <= 0)
                continue;

            var key = Uri.UnescapeDataString(pairs[i][..separator]);
            if (!key.StartsWith("created_at.", StringComparison.Ordinal))
                continue;

            var rawValue = Uri.UnescapeDataString(pairs[i][(separator + 1)..]);
            if (!DateTimeOffset.TryParse(rawValue, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var parsed))
                continue;

            var normalized = parsed.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            pairs[i] = $"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(normalized)}";
            changed = true;
        }

        if (changed)
        {
            var builder = new UriBuilder(uri) { Query = string.Join("&", pairs) };
            request.RequestUri = builder.Uri;
        }
    }

    private static string HashCard(CardInput card)
    {
        var material = $"{DigitsOnly(card.Number)}|{card.ExpirationMonth}|{card.ExpirationYear}|{card.Name.Trim().ToUpperInvariant()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(bytes);
    }

    private static long ToCents(decimal amount) =>
        (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
