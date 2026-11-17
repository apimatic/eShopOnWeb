using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.ErrorResponse;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
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
using Authorization = FinixApiReference.Models.Authorization;

namespace Microsoft.eShopWeb.PublicApi.Finix;

/// <summary>
/// Owns the money-moving conversation with Finix and the local records that let a later request
/// act on the provider's state. Every write is preceded by a database claim; every ambiguous
/// outcome is re-read from Finix before it is reported.
/// </summary>
public sealed class FinixPaymentService
{
    private const int PageSize = 100;
    private const int MaxPages = 500;
    private static readonly TimeSpan ClaimTtl = TimeSpan.FromMinutes(2);

    private readonly FinixApiReferenceClient _finix;
    private readonly CatalogContext _db;
    private readonly FinixOptions _options;
    private readonly ILogger<FinixPaymentService> _logger;
    private readonly TimeProvider _time;

    public FinixPaymentService(
        FinixApiReferenceClient finix,
        CatalogContext db,
        FinixOptions options,
        ILogger<FinixPaymentService> logger,
        TimeProvider time)
    {
        _finix = finix;
        _db = db;
        _options = options;
        _logger = logger;
        _time = time;
    }

    private DateTimeOffset Now => _time.GetUtcNow();

    public Task<CreateOrderResponse> CreateOrderAsync(string shopperId, CreateOrderRequest request, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(30), ct, token => CreateOrderCoreAsync(shopperId, request, token));

    private async Task<CreateOrderResponse> CreateOrderCoreAsync(string shopperId, CreateOrderRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentValidationException("At least one catalog item is required.");
        }

        var address = request.ShipToAddress ?? throw new PaymentValidationException("ShipToAddress is required.");
        RequireAddress(address);

        var catalogIds = request.Items.Select(i => i.CatalogItemId).Distinct().ToArray();
        if (catalogIds.Any(id => id <= 0))
        {
            throw new PaymentValidationException("Every catalogItemId must be greater than zero.");
        }

        var catalogItems = await _db.CatalogItems
            .Where(item => catalogIds.Contains(item.Id))
            .ToListAsync(ct);

        if (catalogItems.Count != catalogIds.Length)
        {
            throw new PaymentValidationException("One or more catalog items do not exist.");
        }

        var orderItems = new List<OrderItem>();
        foreach (var line in request.Items)
        {
            if (line.Quantity <= 0)
            {
                throw new PaymentValidationException("Every quantity must be greater than zero.");
            }

            var catalogItem = catalogItems.First(item => item.Id == line.CatalogItemId);
            orderItems.Add(new OrderItem(
                new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, catalogItem.PictureUri),
                catalogItem.Price,
                line.Quantity));
        }

        var order = new Order(
            shopperId,
            new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(address.Street, address.City, address.State, address.Country, address.ZipCode),
            orderItems);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(ct);

        var total = order.Total();
        return new CreateOrderResponse
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            Total = total,
            TotalCents = ToCents(total),
            Currency = _options.Currency
        };
    }

    public Task<PayOrderResponse> PayOrderAsync(string shopperId, int orderId, PayOrderRequest request, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct, token => PayOrderCoreAsync(shopperId, orderId, request, token));

    private async Task<PayOrderResponse> PayOrderCoreAsync(string shopperId, int orderId, PayOrderRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);

        var order = await LoadOrderForShopperAsync(shopperId, orderId, ct);
        if (order.Status is OrderStatus.Fulfilled or OrderStatus.Cancelled)
        {
            throw new PaymentConflictException($"Order {orderId} is {order.Status} and cannot be paid.");
        }

        var amountCents = ToCents(order.Total());
        if (amountCents <= 0)
        {
            throw new PaymentValidationException("The order total must be greater than zero.");
        }

        var payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct);
        var isNewPayment = false;
        if (payment is null)
        {
            payment = new OrderPayment
            {
                OrderId = orderId,
                ShopperId = shopperId,
                Status = PaymentStatus.Authorizing,
                AmountCents = amountCents,
                Currency = _options.Currency,
                IdempotencyId = $"eshop-order-{orderId}-{Guid.NewGuid():N}",
                CreatedAt = Now,
                UpdatedAt = Now
            };

            _db.OrderPayments.Add(payment);
            try
            {
                await _db.SaveChangesAsync(ct);
                isNewPayment = true;
            }
            catch (DbUpdateException)
            {
                _db.Entry(payment).State = EntityState.Detached;
                payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct);
                if (payment is null)
                {
                    throw new PaymentConflictException("A payment attempt for this order is already in progress.");
                }
            }
        }

        if (!isNewPayment)
        {
            switch (payment.Status)
            {
                case PaymentStatus.Authorized:
                case PaymentStatus.Captured:
                    return MapPay(payment);

                case PaymentStatus.Authorizing:
                    if (Now - payment.UpdatedAt < ClaimTtl)
                    {
                        throw new PaymentConflictException("A payment operation for this order is already in progress.");
                    }

                    if (await TrySettleAuthorizationAsync(payment, ct))
                    {
                        return MapPay(payment);
                    }
                    break;

                case PaymentStatus.Capturing:
                case PaymentStatus.Voiding:
                    throw new PaymentConflictException("A payment operation for this order is already in progress.");

                case PaymentStatus.Voided:
                    throw new PaymentConflictException("This order's authorization was voided; place a new order to pay again.");

                case PaymentStatus.Unknown:
                    if (await TrySettleAuthorizationAsync(payment, ct))
                    {
                        return MapPay(payment);
                    }

                    throw new PaymentConflictException(
                        "The previous authorization outcome is still unknown; retry shortly to reconcile it.");

                case PaymentStatus.Failed:
                    break;
            }
        }

        var wasFailed = payment.Status == PaymentStatus.Failed;
        payment.Status = PaymentStatus.Authorizing;
        payment.AmountCents = amountCents;
        payment.Currency = _options.Currency;
        if (wasFailed)
        {
            payment.IdempotencyId = $"eshop-order-{orderId}-{Guid.NewGuid():N}";
        }
        payment.FailureCode = null;
        payment.FailureMessage = null;
        payment.UpdatedAt = Now;
        await _db.SaveChangesAsync(ct);

        var source = await ResolveSourceSafelyAsync(payment, shopperId, request, ct);

        try
        {
            var authorization = await _finix.AuthorizationsApi.CreateAuthorization(
                new CreateAuthorizationRequest
                {
                    Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                    {
                        Amount = amountCents,
                        Currency = FinixCurrency(),
                        Merchant = _options.MerchantId,
                        Source = source,
                        IdempotencyId = payment.IdempotencyId,
                        Tags = new Dictionary<string, string>
                        {
                            ["eshop_order_id"] = orderId.ToString(),
                            ["eshop_shopper_id"] = shopperId
                        }
                    })
                },
                cancellationToken: ct);

            if (authorization.Id is null)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.UpdatedAt = Now;
                await _db.SaveChangesAsync(ct);
                throw new PaymentProviderException(
                    "Finix did not return an authorization id; the authorization outcome is unknown.", outcomeUnknown: true);
            }

            ApplyAuthorization(payment, authorization);

            if (payment.Status == PaymentStatus.Failed)
            {
                await _db.SaveChangesAsync(ct);
                throw new PaymentProviderException(
                    DescribeAuthorizationFailure(authorization), HttpStatusCode.PaymentRequired);
            }

            if (payment.Status != PaymentStatus.Authorized)
            {
                await _db.SaveChangesAsync(ct);
                throw new PaymentProviderException(
                    $"Authorization {authorization.Id} is in state {authorization.State?.Value ?? "UNKNOWN"}; " +
                    "the payment is not complete. Retrying pay will reconcile it.", outcomeUnknown: true);
            }

            order.MarkAuthorized();
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Authorized order {OrderId} for {AmountCents} {Currency} (authorization {AuthorizationId}).",
                orderId, amountCents, _options.Currency, authorization.Id);

            return MapPay(payment);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic)
                ? Describe(generic)
                : Describe(ex.Error);
            payment.Status = PaymentStatus.Failed;
            payment.FailureCode = generic?.Embedded?.Errors?.FirstOrDefault()?.Code;
            payment.FailureMessage = detail;
            payment.UpdatedAt = Now;
            await TrySaveAsync(ct);
            _logger.LogWarning("Finix rejected authorization for order {OrderId}: {Detail}", orderId, detail);
            throw new PaymentProviderException($"Finix rejected the authorization: {detail}", ex.StatusCode, inner: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                payment.Status = PaymentStatus.Unknown;
                payment.UpdatedAt = Now;
                await TrySaveAsync(ct);
                throw new PaymentProviderException(
                    "Finix returned an authorization response that could not be read; the outcome is unknown.",
                    outcomeUnknown: true, inner: ex);
            }

            payment.Status = PaymentStatus.Failed;
            payment.FailureMessage = "Finix rejected the authorization and its error body could not be read.";
            payment.UpdatedAt = Now;
            await TrySaveAsync(ct);
            throw new PaymentProviderException(payment.FailureMessage, ex.StatusCode, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            if (await TrySettleAuthorizationAsync(payment, ct))
            {
                order = await LoadOrderForShopperAsync(shopperId, orderId, ct);
                if (payment.Status == PaymentStatus.Authorized && order.Status == OrderStatus.AwaitingPayment)
                {
                    order.MarkAuthorized();
                    await _db.SaveChangesAsync(ct);
                }
                return MapPay(payment);
            }

            payment.Status = PaymentStatus.Unknown;
            payment.UpdatedAt = Now;
            await TrySaveAsync(ct);
            throw new PaymentProviderException(
                "The authorization outcome is unknown because Finix did not answer; retrying pay will reconcile it.",
                outcomeUnknown: true, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected; the payment was not authorized.",
                HttpStatusCode.BadGateway, inner: ex);
        }
    }

    public Task<FulfilOrderResponse> FulfilOrderAsync(int orderId, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct, token => FulfilOrderCoreAsync(orderId, token));

    private async Task<FulfilOrderResponse> FulfilOrderCoreAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct)
            ?? throw new PaymentConflictException($"Order {orderId} has no payment to capture.");

        if (payment.Status == PaymentStatus.Captured && order.Status == OrderStatus.Fulfilled)
        {
            return MapFulfil(payment);
        }

        if (payment.Status == PaymentStatus.Voided)
        {
            throw new PaymentConflictException($"Order {orderId} was cancelled; there is no hold to capture.");
        }

        if (payment.AuthorizationId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has not been authorized.");
        }

        var claim = await TryClaimOrderOperationAsync(orderId, "CAPTURE", ct);
        if (claim is null)
        {
            var current = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct);
            if (current?.Status == PaymentStatus.Captured)
            {
                return MapFulfil(current);
            }
            throw new PaymentConflictException($"A capture for order {orderId} is already in progress.");
        }

        try
        {
            if (payment.Status == PaymentStatus.Unknown &&
                await TrySettleCaptureAsync(payment, order, ct))
            {
                return MapFulfil(payment);
            }

            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            var blockReason = CaptureBlockReason(authorization, Now);
            if (blockReason is not null)
            {
                await ReleaseClaimAsync(claim, ct);
                throw new PaymentConflictException(blockReason);
            }

            var captured = await _finix.AuthorizationsApi.CaptureAuthorization(
                new CaptureAuthorizationRequest
                {
                    AuthorizationId = payment.AuthorizationId,
                    Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                    {
                        CaptureAmount = payment.AmountCents
                    })
                },
                cancellationToken: ct);

            if (captured.Transfer is null)
            {
                captured = await GetAuthorizationAsync(payment.AuthorizationId, ct);
                if (captured.Transfer is null)
                {
                    payment.Status = PaymentStatus.Unknown;
                    payment.UpdatedAt = Now;
                    await ReleaseClaimAsync(claim, ct);
                    throw new PaymentProviderException(
                        $"Finix accepted the capture for order {orderId} but returned no transfer; " +
                        "the capture outcome is unknown.", outcomeUnknown: true);
                }
            }

            payment.Status = PaymentStatus.Captured;
            payment.CaptureTransferId = captured.Transfer;
            payment.AuthorizationState = captured.State?.Value;
            payment.FailureCode = null;
            payment.FailureMessage = null;
            payment.UpdatedAt = Now;
            await FillCaptureDetailsAsync(payment, ct);

            order.MarkFulfilled();
            await ReleaseClaimAsync(claim, ct);

            _logger.LogInformation(
                "Captured order {OrderId} amount {AmountCents} {Currency} (transfer {TransferId}, fee {FeeCents}).",
                orderId, payment.CapturedAmountCents, payment.Currency, payment.CaptureTransferId, payment.FeeCents);

            return MapFulfil(payment);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic) ? Describe(generic) : Describe(ex.Error);
            var reread = await TryGetAuthorizationAsync(payment.AuthorizationId, ct);
            payment.AuthorizationState = reread?.State?.Value ?? payment.AuthorizationState;
            payment.FailureCode = reread?.FailureCode;
            payment.FailureMessage = reread?.FailureMessage;
            payment.UpdatedAt = Now;
            await ReleaseClaimAsync(claim, ct);
            _logger.LogWarning("Finix rejected capture for order {OrderId}: {Detail}", orderId, detail);
            throw new PaymentConflictException(
                $"Finix could not capture authorization {payment.AuthorizationId} for order {orderId}: {detail}. " +
                DescribeHoldState(reread));
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                if (await TrySettleCaptureAsync(payment, order, ct))
                {
                    return MapFulfil(payment);
                }
                payment.Status = PaymentStatus.Unknown;
                payment.UpdatedAt = Now;
                await ReleaseClaimAsync(claim, ct);
                throw new PaymentProviderException(
                    $"The capture outcome for order {orderId} is unknown because Finix's response could not be read.",
                    outcomeUnknown: true, inner: ex);
            }

            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                $"Finix rejected the capture for order {orderId} and its error body could not be read.",
                ex.StatusCode, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            if (await TrySettleCaptureAsync(payment, order, ct))
            {
                return MapFulfil(payment);
            }

            payment.Status = PaymentStatus.Unknown;
            payment.UpdatedAt = Now;
            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                $"The capture outcome for order {orderId} is unknown because Finix did not answer; " +
                "retrying fulfil will reconcile it.", outcomeUnknown: true, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected; nothing was captured.",
                HttpStatusCode.BadGateway, inner: ex);
        }
        catch
        {
            await ReleaseClaimAsync(claim, ct);
            throw;
        }
    }

    public Task<CancelOrderResponse> CancelOrderAsync(int orderId, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct, token => CancelOrderCoreAsync(orderId, token));

    private async Task<CancelOrderResponse> CancelOrderCoreAsync(int orderId, CancellationToken ct)
    {
        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == orderId, ct)
            ?? throw new PaymentNotFoundException($"Order {orderId} was not found.");

        var payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct);
        if (payment is null)
        {
            if (order.Status != OrderStatus.Cancelled)
            {
                order.MarkCancelled();
                await _db.SaveChangesAsync(ct);
            }

            return new CancelOrderResponse
            {
                OrderId = orderId,
                PaymentStatus = "None",
                VoidedAt = Now
            };
        }

        if (payment.Status == PaymentStatus.Voided || order.Status == OrderStatus.Cancelled)
        {
            return MapCancel(payment);
        }

        if (payment.Status == PaymentStatus.Captured || order.Status == OrderStatus.Fulfilled)
        {
            throw new PaymentConflictException(
                $"Order {orderId} was already fulfilled; use the refunds endpoint instead of cancelling.");
        }

        if (payment.AuthorizationId is null)
        {
            order.MarkCancelled();
            payment.Status = PaymentStatus.Voided;
            payment.VoidedAt = Now;
            payment.UpdatedAt = Now;
            await _db.SaveChangesAsync(ct);
            return MapCancel(payment);
        }

        var claim = await TryClaimOrderOperationAsync(orderId, "VOID", ct);
        if (claim is null)
        {
            var current = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct);
            if (current?.Status == PaymentStatus.Voided)
            {
                return MapCancel(current);
            }
            throw new PaymentConflictException($"A cancellation for order {orderId} is already in progress.");
        }

        try
        {
            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
            {
                payment.Status = PaymentStatus.Voided;
                payment.VoidedAt ??= Now;
                payment.UpdatedAt = Now;
                if (order.Status is OrderStatus.Authorized or OrderStatus.AwaitingPayment)
                {
                    order.MarkCancelled();
                }
                await ReleaseClaimAsync(claim, ct);
                return MapCancel(payment);
            }

            if (authorization.Transfer is not null)
            {
                await ReleaseClaimAsync(claim, ct);
                throw new PaymentConflictException(
                    $"Authorization {payment.AuthorizationId} was already captured (transfer {authorization.Transfer}); " +
                    "use the refunds endpoint instead of cancelling.");
            }

            var voided = await _finix.AuthorizationsApi.VoidAuthorization(
                new VoidAuthorizationRequest
                {
                    AuthorizationIdVoidTo = payment.AuthorizationId,
                    Body = new VoidAnAuthorization { VoidMe = true }
                },
                cancellationToken: ct);

            payment.Status = PaymentStatus.Voided;
            payment.AuthorizationState = voided.State?.Value;
            payment.VoidedAt = Now;
            payment.UpdatedAt = Now;
            if (order.Status is OrderStatus.Authorized or OrderStatus.AwaitingPayment)
            {
                order.MarkCancelled();
            }

            await ReleaseClaimAsync(claim, ct);
            _logger.LogInformation("Voided authorization {AuthorizationId} for order {OrderId}.",
                payment.AuthorizationId, orderId);

            return MapCancel(payment);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic) ? Describe(generic) : Describe(ex.Error);
            await ReleaseClaimAsync(claim, ct);
            throw new PaymentConflictException(
                $"Finix could not void authorization {payment.AuthorizationId} for order {orderId}: {detail}.");
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300 && await TrySettleVoidAsync(payment, order, ct))
            {
                return MapCancel(payment);
            }

            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                $"The void outcome for order {orderId} is unknown because Finix's response could not be read.",
                outcomeUnknown: true, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            if (await TrySettleVoidAsync(payment, order, ct))
            {
                return MapCancel(payment);
            }

            payment.Status = PaymentStatus.Unknown;
            payment.UpdatedAt = Now;
            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                $"The void outcome for order {orderId} is unknown because Finix did not answer; " +
                "retrying cancel will reconcile it.", outcomeUnknown: true, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            await ReleaseClaimAsync(claim, ct);
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected; the authorization was not voided.",
                HttpStatusCode.BadGateway, inner: ex);
        }
        catch
        {
            await ReleaseClaimAsync(claim, ct);
            throw;
        }
    }

    public Task<RefundOrderResponse> RefundOrderAsync(
        string shopperId, int orderId, RefundOrderRequest request, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct,
            token => RefundOrderCoreAsync(shopperId, orderId, request, token));

    private async Task<RefundOrderResponse> RefundOrderCoreAsync(
        string shopperId, int orderId, RefundOrderRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentValidationException("IdempotencyKey is required.");
        }

        if (request.IdempotencyKey.Length > 255)
        {
            throw new PaymentValidationException("IdempotencyKey must be 255 characters or fewer.");
        }

        if (request.Amount <= 0)
        {
            throw new PaymentValidationException("Amount must be greater than zero.");
        }

        var amountCents = ToCents(request.Amount);
        if (amountCents > int.MaxValue)
        {
            throw new PaymentValidationException("Amount is too large.");
        }

        await LoadOrderForShopperAsync(shopperId, orderId, ct);

        var payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct)
            ?? throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");

        if (payment.Status != PaymentStatus.Captured || payment.CaptureTransferId is null)
        {
            throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");
        }

        var refundKey = $"{orderId}:{request.IdempotencyKey}";
        var existing = await _db.RefundRecords.FindAsync(new object[] { refundKey }, ct);
        if (existing is not null)
        {
            if (existing.Status == "Succeeded" && existing.RefundId is not null)
            {
                return MapRefund(existing, payment.RefundedCents);
            }

            if (existing.Status == "Unknown" && await TrySettleRefundAsync(existing, payment, ct))
            {
                return MapRefund(existing, payment.RefundedCents);
            }

            if (existing.Status == "Failed")
            {
                _db.RefundRecords.Remove(existing);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                throw new PaymentConflictException(
                    "A refund with this idempotency key is already in progress; retry later to reconcile it.");
            }
        }

        var lockRow = new RefundLock { OrderId = orderId, CreatedAt = Now };
        _db.RefundLocks.Add(lockRow);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(lockRow).State = EntityState.Detached;
            throw new PaymentConflictException($"Another refund for order {orderId} is in progress; retry shortly.");
        }

        try
        {
            payment = await _db.OrderPayments.FindAsync(new object[] { orderId }, ct)
                ?? throw new PaymentConflictException($"Order {orderId} has no captured payment to refund.");

            var captured = payment.CapturedAmountCents ?? payment.AmountCents;
            var remaining = captured - payment.RefundedCents;
            if (amountCents > remaining)
            {
                throw new PaymentValidationException(
                    $"Refund amount {amountCents} exceeds the {remaining} cents still refundable on order {orderId}.");
            }

            var existingIds = await ListReversalIdsAsync(payment.CaptureTransferId!, ct);
            var record = new RefundRecord
            {
                RefundKey = refundKey,
                OrderId = orderId,
                IdempotencyKey = request.IdempotencyKey,
                AmountCents = amountCents,
                Status = "Pending",
                ExistingReversalIds = string.Join(",", existingIds),
                CreatedAt = Now,
                UpdatedAt = Now
            };
            _db.RefundRecords.Add(record);

            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                _db.Entry(record).State = EntityState.Detached;
                throw new PaymentConflictException(
                    "A refund with this idempotency key is already in progress; retry later to reconcile it.");
            }

            try
            {
                var body = new RefundTransfer { RefundAmount = (int)amountCents };
                body.AdditionalProperties.Set("idempotency_id", request.IdempotencyKey);

                var reversal = await _finix.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = payment.CaptureTransferId!,
                        Body = TransfersReversalsRequest.RefundTransfer(body)
                    },
                    cancellationToken: ct);

                if (reversal.Id is null)
                {
                    record.Status = "Unknown";
                    record.UpdatedAt = Now;
                    throw new PaymentProviderException(
                        $"Finix accepted the refund for order {orderId} but returned no refund id; " +
                        "the refund outcome is unknown.", outcomeUnknown: true);
                }

                record.RefundId = reversal.Id!;
                record.Status = "Succeeded";
                record.FailureCode = null;
                record.FailureMessage = null;
                record.UpdatedAt = Now;
                payment.RefundedCents += amountCents;
                payment.UpdatedAt = Now;
                await _db.SaveChangesAsync(ct);

                _logger.LogInformation(
                    "Refunded {AmountCents} cents on order {OrderId} (refund {RefundId}, total refunded {RefundedCents}).",
                    amountCents, orderId, reversal.Id, payment.RefundedCents);

                return MapRefund(record, payment.RefundedCents);
            }
            catch (ApiException<CreateTransferReversalError> ex)
            {
                var generic = ex.Error.TryGetGenericError(out var typed) ? typed : null;
                var detail = generic is not null ? Describe(generic) : Describe(ex.Error);
                _db.RefundRecords.Remove(record);
                await TrySaveAsync(ct);

                if (generic is not null && IsPendingCaptureRejection(generic) ||
                    IsPendingCaptureRejection(ex.Error, ex.StatusCode))
                {
                    throw new PaymentConflictException(
                        $"The capture transfer {payment.CaptureTransferId} is still pending; " +
                        $"partial refunds can be retried after it succeeds. {detail}");
                }

                throw new PaymentProviderException($"Finix rejected the refund: {detail}", ex.StatusCode, inner: ex);
            }
            catch (ResponseDeserializationException ex)
            {
                if ((int)ex.StatusCode is >= 200 and < 300)
                {
                    record.Status = "Unknown";
                    record.UpdatedAt = Now;
                    await TrySaveAsync(ct);
                    throw new PaymentProviderException(
                        $"The refund outcome for order {orderId} is unknown because Finix's response could not be read.",
                        outcomeUnknown: true, inner: ex);
                }

                record.Status = "Failed";
                record.FailureMessage = "Finix rejected the refund and its error body could not be read.";
                record.UpdatedAt = Now;
                _db.RefundRecords.Remove(record);
                await TrySaveAsync(ct);
                throw new PaymentProviderException(record.FailureMessage, ex.StatusCode, inner: ex);
            }
            catch (SdkConnectionException ex)
            {
                if (await TrySettleRefundAsync(record, payment, ct))
                {
                    return MapRefund(record, payment.RefundedCents);
                }

                record.Status = "Unknown";
                record.UpdatedAt = Now;
                await TrySaveAsync(ct);
                throw new PaymentProviderException(
                    $"The refund outcome for order {orderId} is unknown because Finix did not answer; " +
                    "retry the same idempotency key to reconcile it.", outcomeUnknown: true, inner: ex);
            }
            catch (AuthSchemeException ex)
            {
                record.Status = "Failed";
                record.FailureMessage = "Finix credentials are missing or were rejected.";
                record.UpdatedAt = Now;
                _db.RefundRecords.Remove(record);
                await TrySaveAsync(ct);
                throw new PaymentProviderException(
                    "Finix credentials are missing or were rejected; nothing was refunded.",
                    HttpStatusCode.BadGateway, inner: ex);
            }
        }
        finally
        {
            await ReleaseRefundLockAsync(orderId, ct);
        }
    }

    public Task<MyOrdersResponse> GetMyOrdersAsync(string shopperId, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(30), ct, token => GetMyOrdersCoreAsync(shopperId, token));

    private async Task<MyOrdersResponse> GetMyOrdersCoreAsync(string shopperId, CancellationToken ct)
    {
        RequireShopper(shopperId);

        var orders = await _db.Orders
            .AsNoTracking()
            .Where(o => o.BuyerId == shopperId)
            .Include(o => o.OrderItems)
            .ThenInclude(i => i.ItemOrdered)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(ct);

        var orderIds = orders.Select(o => o.Id).ToList();
        var payments = await _db.OrderPayments
            .AsNoTracking()
            .Where(p => orderIds.Contains(p.OrderId))
            .ToDictionaryAsync(p => p.OrderId, ct);

        var response = new MyOrdersResponse();
        foreach (var order in orders)
        {
            payments.TryGetValue(order.Id, out var payment);
            response.Orders.Add(new OrderSummary
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                Status = order.Status.ToString(),
                Total = order.Total(),
                TotalCents = ToCents(order.Total()),
                Currency = payment?.Currency ?? _options.Currency,
                ShipToAddress = new ShippingAddressRequest
                {
                    Street = order.ShipToAddress.Street,
                    City = order.ShipToAddress.City,
                    State = order.ShipToAddress.State,
                    Country = order.ShipToAddress.Country,
                    ZipCode = order.ShipToAddress.ZipCode
                },
                Items = order.OrderItems.Select(item => new OrderItemSummary
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Units = item.Units
                }).ToList(),
                Payment = payment is null ? null : MapPaymentSummary(payment)
            });
        }

        return response;
    }

    public Task<SavePaymentMethodResponse> SaveCardAsync(
        string shopperId, SavePaymentMethodRequest request, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct,
            token => SaveCardCoreAsync(shopperId, request, token));

    private async Task<SavePaymentMethodResponse> SaveCardCoreAsync(
        string shopperId, SavePaymentMethodRequest request, CancellationToken ct)
    {
        RequireShopper(shopperId);
        var card = ValidateCard(request.Number, request.ExpirationMonth, request.ExpirationYear,
            request.SecurityCode, request.Name);

        var claimKey = $"{shopperId}:{card.LastFour}:{card.ExpirationMonth}:{card.ExpirationYear}";
        var claim = await _db.SavedCardClaims.FindAsync(new object[] { claimKey }, ct);

        if (claim?.PaymentInstrumentId is not null)
        {
            var existing = await _db.SavedCards
                .FirstOrDefaultAsync(c => c.ShopperId == shopperId && c.PaymentInstrumentId == claim.PaymentInstrumentId, ct);
            if (existing is not null)
            {
                return MapSavedCard(existing);
            }
        }

        if (claim is not null)
        {
            if (Now - claim.CreatedAt < ClaimTtl)
            {
                throw new PaymentConflictException("This card is already being saved; retry shortly.");
            }

            _db.SavedCardClaims.Remove(claim);
            await _db.SaveChangesAsync(ct);
        }

        claim = new SavedCardClaim { ClaimKey = claimKey, CreatedAt = Now };
        _db.SavedCardClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            throw new PaymentConflictException("This card is already being saved; retry shortly.");
        }

        try
        {
            var identityId = await GetOrCreateIdentityAsync(shopperId, ct);
            var created = await CreateInstrumentAsync(identityId, card, claimKey, ct);

            var saved = new SavedCard
            {
                ShopperId = shopperId,
                PaymentInstrumentId = created.Id!,
                Brand = created.Brand?.Value,
                LastFour = created.LastFour ?? card.LastFour,
                ExpirationMonth = created.ExpirationMonth ?? card.ExpirationMonth,
                ExpirationYear = created.ExpirationYear ?? card.ExpirationYear,
                Name = created.Name ?? card.Name,
                CreatedAt = Now
            };

            _db.SavedCards.Add(saved);
            claim.PaymentInstrumentId = saved.PaymentInstrumentId;
            await _db.SaveChangesAsync(ct);

            _logger.LogInformation("Saved card {PaymentInstrumentId} for shopper {ShopperId} (last four {LastFour}).",
                saved.PaymentInstrumentId, shopperId, saved.LastFour);

            return MapSavedCard(saved);
        }
        catch
        {
            _db.SavedCardClaims.Remove(claim);
            await TrySaveAsync(ct);
            throw;
        }
    }

    public Task<ListPaymentMethodsResponse> GetCardsAsync(string shopperId, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(30), ct, token => GetCardsCoreAsync(shopperId, token));

    private async Task<ListPaymentMethodsResponse> GetCardsCoreAsync(string shopperId, CancellationToken ct)
    {
        RequireShopper(shopperId);

        var cards = await _db.SavedCards
            .AsNoTracking()
            .Where(c => c.ShopperId == shopperId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        var response = new ListPaymentMethodsResponse();
        response.PaymentMethods.AddRange(cards.Select(MapPaymentMethodSummary));
        return response;
    }

    public Task<DeletePaymentMethodResponse> DeleteCardAsync(
        string shopperId, string paymentMethodId, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(60), ct,
            token => DeleteCardCoreAsync(shopperId, paymentMethodId, token));

    private async Task<DeletePaymentMethodResponse> DeleteCardCoreAsync(
        string shopperId, string paymentMethodId, CancellationToken ct)
    {
        RequireShopper(shopperId);
        if (string.IsNullOrWhiteSpace(paymentMethodId))
        {
            throw new PaymentValidationException("paymentMethodId is required.");
        }

        var card = await _db.SavedCards
            .FirstOrDefaultAsync(c => c.ShopperId == shopperId && c.PaymentInstrumentId == paymentMethodId, ct)
            ?? throw new PaymentNotFoundException("Saved card was not found.");

        try
        {
            await _finix.PaymentInstrumentsApi.UpdatePaymentInstrument(
                new UpdatePaymentInstrumentRequest
                {
                    PaymentInstrumentId = paymentMethodId,
                    Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                        new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                },
                cancellationToken: ct);
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic) ? Describe(generic) : Describe(ex.Error);
            throw new PaymentProviderException($"Finix could not disable the saved card: {detail}", ex.StatusCode, inner: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            if ((int)ex.StatusCode is >= 200 and < 300)
            {
                // The disable may have landed; fall through to the local removal and let a
                // later pay attempt be refused by the provider if it did not.
                _logger.LogWarning(ex, "Finix returned an unreadable response while disabling card {PaymentInstrumentId}.",
                    paymentMethodId);
            }
            else
            {
                throw new PaymentProviderException(
                    "Finix rejected the request to disable the saved card.", ex.StatusCode, inner: ex);
            }
        }
        catch (SdkConnectionException ex)
        {
            if (!await CardIsGoneAsync(shopperId, paymentMethodId, ct))
            {
                throw new PaymentProviderException(
                    "The saved card could not be disabled because Finix did not answer; retry the delete.",
                    outcomeUnknown: true, inner: ex);
            }
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected; the saved card was not disabled.",
                HttpStatusCode.BadGateway, inner: ex);
        }

        _db.SavedCards.Remove(card);
        await _db.SaveChangesAsync(ct);

        return new DeletePaymentMethodResponse
        {
            PaymentMethodId = paymentMethodId,
            Deleted = true
        };
    }

    public Task<ReconciliationResponse> ReconcileAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        RunBoundedAsync(TimeSpan.FromSeconds(120), ct, token => ReconcileCoreAsync(from, to, token));

    private async Task<ReconciliationResponse> ReconcileCoreAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (from > to)
        {
            throw new PaymentValidationException("from must be before or equal to to.");
        }

        var response = new ReconciliationResponse { From = from, To = to };
        var transfers = await ListTransfersAsync(from, to, ct);
        var authorizations = await ListAuthorizationsAsync(from, to, ct);

        response.Truncated = transfers.Truncated || authorizations.Truncated;
        response.NextCursor = transfers.NextCursor ?? authorizations.NextCursor;

        var payments = await _db.OrderPayments.AsNoTracking().ToListAsync(ct);
        var refunds = await _db.RefundRecords.AsNoTracking().ToListAsync(ct);
        var matchedEshop = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers.Items)
        {
            var tx = MapTransfer(transfer);
            response.FinixTransactions.Add(tx);

            var payment = payments.FirstOrDefault(p => p.CaptureTransferId == transfer.Id);
            var refund = refunds.FirstOrDefault(r => r.RefundId == transfer.Id);

            if (payment is not null)
            {
                response.Matched.Add(new ReconciliationMatch
                {
                    FinixId = transfer.Id ?? string.Empty,
                    Kind = "TRANSFER",
                    OrderId = payment.OrderId,
                    MatchType = "CAPTURE"
                });
                matchedEshop.Add($"capture:{payment.OrderId}");
            }
            else if (refund is not null)
            {
                response.Matched.Add(new ReconciliationMatch
                {
                    FinixId = transfer.Id ?? string.Empty,
                    Kind = "TRANSFER",
                    OrderId = refund.OrderId,
                    MatchType = "REFUND"
                });
                matchedEshop.Add($"refund:{refund.RefundKey}");
            }
            else
            {
                response.UnmatchedFinix.Add(tx);
            }
        }

        foreach (var authorization in authorizations.Items)
        {
            var tx = MapAuthorization(authorization);
            response.FinixTransactions.Add(tx);

            var payment = payments.FirstOrDefault(p => p.AuthorizationId == authorization.Id);
            if (payment is not null)
            {
                response.Matched.Add(new ReconciliationMatch
                {
                    FinixId = authorization.Id ?? string.Empty,
                    Kind = "AUTHORIZATION",
                    OrderId = payment.OrderId,
                    MatchType = "AUTHORIZATION"
                });
                matchedEshop.Add($"auth:{payment.OrderId}");
            }
            else
            {
                response.UnmatchedFinix.Add(tx);
            }
        }

        foreach (var payment in payments)
        {
            var inRange = InRange(payment.AuthorizedAt, from, to) ||
                          InRange(payment.CapturedAt, from, to) ||
                          InRange(payment.CreatedAt, from, to);
            if (!inRange)
            {
                continue;
            }

            if (payment.AuthorizationId is not null && !matchedEshop.Contains($"auth:{payment.OrderId}"))
            {
                response.UnmatchedEshop.Add(new ReconciliationEshopRecord
                {
                    OrderId = payment.OrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureTransferId = payment.CaptureTransferId,
                    Status = payment.Status.ToString(),
                    AmountCents = payment.AmountCents
                });
            }

            if (payment.CaptureTransferId is not null && !matchedEshop.Contains($"capture:{payment.OrderId}"))
            {
                response.UnmatchedEshop.Add(new ReconciliationEshopRecord
                {
                    OrderId = payment.OrderId,
                    AuthorizationId = payment.AuthorizationId,
                    CaptureTransferId = payment.CaptureTransferId,
                    Status = payment.Status.ToString(),
                    AmountCents = payment.CapturedAmountCents ?? payment.AmountCents
                });
            }
        }

        foreach (var refund in refunds)
        {
            if (InRange(refund.CreatedAt, from, to) &&
                refund.RefundId is not null &&
                !matchedEshop.Contains($"refund:{refund.RefundKey}"))
            {
                response.UnmatchedEshop.Add(new ReconciliationEshopRecord
                {
                    OrderId = refund.OrderId,
                    RefundId = refund.RefundId,
                    Status = refund.Status,
                    AmountCents = refund.AmountCents
                });
            }
        }

        return response;
    }

    private async Task<string> ResolveSourceSafelyAsync(
        OrderPayment payment, string shopperId, PayOrderRequest request, CancellationToken ct)
    {
        try
        {
            return await ResolveSourceAsync(shopperId, request, ct);
        }
        catch (PaymentException ex)
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailureMessage = ex.Message;
            payment.UpdatedAt = Now;
            await TrySaveAsync(ct);
            throw;
        }
    }

    private async Task<string> ResolveSourceAsync(string shopperId, PayOrderRequest request, CancellationToken ct)
    {
        var hasSavedCard = !string.IsNullOrWhiteSpace(request.PaymentMethodId);
        var hasCard = request.Card is not null;
        if (hasSavedCard == hasCard)
        {
            throw new PaymentValidationException(
                "Provide either PaymentMethodId or Card (exactly one) to pay for the order.");
        }

        if (hasSavedCard)
        {
            var saved = await _db.SavedCards.FirstOrDefaultAsync(
                c => c.ShopperId == shopperId && c.PaymentInstrumentId == request.PaymentMethodId, ct)
                ?? throw new PaymentNotFoundException("Saved card was not found.");

            var identityId = await GetOrCreateIdentityAsync(shopperId, ct);
            var instrument = await GetInstrumentAsync(identityId, saved.PaymentInstrumentId, ct);
            if (instrument is null || instrument.Enabled == false)
            {
                throw new PaymentValidationException(
                    "The saved card is no longer usable; delete it and save the card again.");
            }

            return saved.PaymentInstrumentId;
        }

        var card = ValidateCard(request.Card!.Number, request.Card.ExpirationMonth,
            request.Card.ExpirationYear, request.Card.SecurityCode, request.Card.Name);
        var buyerIdentityId = await GetOrCreateIdentityAsync(shopperId, ct);
        var created = await CreateInstrumentAsync(buyerIdentityId, card,
            $"eshop-pay-{Guid.NewGuid():N}", ct);
        return created.Id!;
    }

    private async Task<string> GetOrCreateIdentityAsync(string shopperId, CancellationToken ct)
    {
        var row = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, ct);
        var created = false;

        if (row is null)
        {
            row = new ShopperFinixIdentity { ShopperId = shopperId, CreatedAt = Now };
            _db.ShopperFinixIdentities.Add(row);
            try
            {
                await _db.SaveChangesAsync(ct);
                created = true;
            }
            catch (DbUpdateException)
            {
                _db.Entry(row).State = EntityState.Detached;
                row = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, ct);
            }
        }

        if (row?.IdentityId is not null)
        {
            return row.IdentityId;
        }

        if (row is null)
        {
            throw new PaymentConflictException("The shopper's Finix identity is being provisioned; retry shortly.");
        }

        if (!created && Now - row.CreatedAt < ClaimTtl)
        {
            throw new PaymentConflictException("The shopper's Finix identity is being provisioned; retry shortly.");
        }

        row.CreatedAt = Now;
        await _db.SaveChangesAsync(ct);

        try
        {
            var identity = await _finix.Identities.CreateIdentity(
                new CreateIdentityRequest
                {
                    Body = IdentitiesRequest.Buyer(new Buyer
                    {
                        Entity = new Entity5
                        {
                            Email = shopperId,
                            FirstName = "eShop",
                            LastName = "Shopper"
                        },
                        Type = Type10.Personal,
                        Tags = new Dictionary<string, string> { ["eshop_shopper_id"] = shopperId }
                    })
                },
                cancellationToken: ct);

            if (identity.Id is null)
            {
                throw new PaymentProviderException(
                    "Finix did not return an identity id for the shopper; retry the request.", outcomeUnknown: true);
            }

            row.IdentityId = identity.Id;
            await _db.SaveChangesAsync(ct);
            return identity.Id;
        }
        catch (ApiException<CreateIdentityError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic) ? Describe(generic) : Describe(ex.Error);
            row.CreatedAt = Now - ClaimTtl;
            await TrySaveAsync(ct);
            throw new PaymentProviderException($"Finix could not create the shopper identity: {detail}", ex.StatusCode, inner: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            row.CreatedAt = Now - ClaimTtl;
            await TrySaveAsync(ct);
            throw new PaymentProviderException(
                "Finix returned an identity response that could not be read.", ex.StatusCode, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            var found = await FindIdentityByTagAsync(shopperId, ct);
            if (found is not null)
            {
                row.IdentityId = found;
                row.CreatedAt = Now;
                await TrySaveAsync(ct);
                return found;
            }

            row.CreatedAt = Now - ClaimTtl;
            await TrySaveAsync(ct);
            throw new PaymentProviderException(
                "The shopper identity outcome is unknown because Finix did not answer; retry the request.",
                outcomeUnknown: true, inner: ex);
        }
        catch
        {
            row.CreatedAt = Now - ClaimTtl;
            await TrySaveAsync(ct);
            throw;
        }
    }

    private async Task<PaymentInstrumentCard> CreateInstrumentAsync(
        string identityId, CardData card, string claimKey, CancellationToken ct)
    {
        try
        {
            var response = await _finix.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest
                {
                    Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                    {
                        Number = card.Number,
                        ExpirationMonth = card.ExpirationMonth,
                        ExpirationYear = card.ExpirationYear,
                        SecurityCode = card.SecurityCode,
                        Name = card.Name,
                        Identity = identityId,
                        Tags = new Dictionary<string, string> { ["eshop_claim_key"] = claimKey }
                    })
                },
                cancellationToken: ct);

            if (!response.TryGetPaymentInstrumentCard(out var instrument) || instrument.Id is null)
            {
                throw new PaymentProviderException(
                    "Finix did not return a payment instrument id; the card could not be used.",
                    outcomeUnknown: true);
            }

            return instrument;
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            var detail = ex.Error.TryGetGenericError(out var generic) ? Describe(generic) : Describe(ex.Error);
            throw new PaymentProviderException($"Finix rejected the card: {detail}", ex.StatusCode, inner: ex);
        }
        catch (ResponseDeserializationException ex)
        {
            throw new PaymentProviderException(
                "Finix returned a card response that could not be read; the card may not have been saved.",
                ex.StatusCode, inner: ex);
        }
        catch (SdkConnectionException ex)
        {
            var found = await FindInstrumentByTagAsync(identityId, claimKey, ct);
            if (found is not null)
            {
                return found;
            }

            throw new PaymentProviderException(
                "The card could not be saved because Finix did not answer; retry the request.",
                outcomeUnknown: true, inner: ex);
        }
        catch (AuthSchemeException ex)
        {
            throw new PaymentProviderException(
                "Finix credentials are missing or were rejected; the card was not saved.",
                HttpStatusCode.BadGateway, inner: ex);
        }
    }

    private async Task<string?> FindIdentityByTagAsync(string shopperId, CancellationToken ct)
    {
        try
        {
            var response = await _finix.Identities.ListIdentities(
                new ListIdentitiesRequest
                {
                    TagsKey = "eshop_shopper_id",
                    TagsValue = shopperId,
                    Limit = 10
                },
                cancellationToken: ct);

            return response.Embedded?.Identities?
                .FirstOrDefault(identity => identity.Id is not null)?.Id;
        }
        catch
        {
            return null;
        }
    }

    private async Task<PaymentInstrumentCard?> FindInstrumentByTagAsync(
        string identityId, string claimKey, CancellationToken ct)
    {
        try
        {
            var response = await _finix.PaymentInstrumentsApi.ListPaymentInstruments(
                new ListPaymentInstrumentsRequest
                {
                    OwnerIdentityId = identityId,
                    TagsKey = "eshop_claim_key",
                    TagsValue = claimKey,
                    Limit = 10
                },
                cancellationToken: ct);

            var item = response.Embedded?.PaymentInstruments?.FirstOrDefault();
            return item is not null && item.TryGetPaymentInstrumentCard(out var card) ? card : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<PaymentInstrumentCard?> GetInstrumentAsync(
        string identityId, string paymentInstrumentId, CancellationToken ct)
    {
        try
        {
            var response = await _finix.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = paymentInstrumentId },
                cancellationToken: ct);

            if (!response.TryGetPaymentInstrumentCard(out var card))
            {
                return null;
            }

            return string.Equals(card.Identity, identityId, StringComparison.Ordinal) ? card : null;
        }
        catch (ApiException<GetPaymentInstrumentError> ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (SdkException ex)
        {
            throw new PaymentProviderException(
                "The saved card could not be verified with Finix; retry the request.",
                outcomeUnknown: true, inner: ex);
        }
    }

    private async Task<bool> CardIsGoneAsync(string shopperId, string paymentInstrumentId, CancellationToken ct)
    {
        var identity = await _db.ShopperFinixIdentities.FindAsync(new object[] { shopperId }, ct);
        if (identity?.IdentityId is null)
        {
            return false;
        }

        try
        {
            var instrument = await GetInstrumentAsync(identity.IdentityId, paymentInstrumentId, ct);
            return instrument is null || instrument.Enabled == false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<Order> LoadOrderForShopperAsync(string shopperId, int orderId, CancellationToken ct)
    {
        var order = await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null || !string.Equals(order.BuyerId, shopperId, StringComparison.Ordinal))
        {
            throw new PaymentNotFoundException($"Order {orderId} was not found.");
        }

        return order;
    }

    private async Task<Authorization> GetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _finix.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
        }
        catch (SdkException ex)
        {
            throw new PaymentProviderException(
                $"Authorization {authorizationId} could not be read from Finix.", outcomeUnknown: true, inner: ex);
        }
    }

    private async Task<Authorization?> TryGetAuthorizationAsync(string authorizationId, CancellationToken ct)
    {
        try
        {
            return await _finix.AuthorizationsApi.GetAuthorization(
                new GetAuthorizationRequest { AuthorizationId = authorizationId }, cancellationToken: ct);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TrySettleAuthorizationAsync(OrderPayment payment, CancellationToken ct)
    {
        try
        {
            var response = await _finix.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    IdempotencyId = payment.IdempotencyId,
                    Merchant = _options.MerchantId,
                    Limit = 10
                },
                cancellationToken: ct);

            var authorization = response.Embedded?.Authorizations?.FirstOrDefault();
            if (authorization?.Id is null)
            {
                return false;
            }

            ApplyAuthorization(payment, authorization);
            await _db.SaveChangesAsync(ct);
            return payment.Status is PaymentStatus.Authorized or PaymentStatus.Captured;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TrySettleCaptureAsync(OrderPayment payment, Order order, CancellationToken ct)
    {
        if (payment.AuthorizationId is null)
        {
            return false;
        }

        try
        {
            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            ApplyAuthorization(payment, authorization);
            if (payment.Status == PaymentStatus.Captured && payment.CaptureTransferId is not null)
            {
                await FillCaptureDetailsAsync(payment, ct);
                if (order.Status == OrderStatus.Authorized)
                {
                    order.MarkFulfilled();
                }
                await _db.SaveChangesAsync(ct);
                return true;
            }

            await _db.SaveChangesAsync(ct);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TrySettleVoidAsync(OrderPayment payment, Order order, CancellationToken ct)
    {
        if (payment.AuthorizationId is null)
        {
            return false;
        }

        try
        {
            var authorization = await GetAuthorizationAsync(payment.AuthorizationId, ct);
            if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
            {
                payment.Status = PaymentStatus.Voided;
                payment.VoidedAt ??= Now;
                payment.AuthorizationState = authorization.State?.Value;
                payment.UpdatedAt = Now;
                if (order.Status is OrderStatus.Authorized or OrderStatus.AwaitingPayment)
                {
                    order.MarkCancelled();
                }
                await _db.SaveChangesAsync(ct);
                return true;
            }

            if (authorization.Transfer is not null)
            {
                ApplyAuthorization(payment, authorization);
                await _db.SaveChangesAsync(ct);
                return false;
            }

            await _db.SaveChangesAsync(ct);
            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TrySettleRefundAsync(RefundRecord record, OrderPayment payment, CancellationToken ct)
    {
        if (payment.CaptureTransferId is null)
        {
            return false;
        }

        try
        {
            var ids = await ListReversalIdsAsync(payment.CaptureTransferId!, ct);
            var previous = string.IsNullOrWhiteSpace(record.ExistingReversalIds)
                ? new HashSet<string>(StringComparer.Ordinal)
                : record.ExistingReversalIds.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

            var added = ids.Where(id => !previous.Contains(id)).ToList();
            if (added.Count == 1)
            {
                record.RefundId = added[0];
                record.Status = "Succeeded";
                record.UpdatedAt = Now;
                payment.RefundedCents += record.AmountCents;
                payment.UpdatedAt = Now;
                await _db.SaveChangesAsync(ct);
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task FillCaptureDetailsAsync(OrderPayment payment, CancellationToken ct)
    {
        if (payment.CaptureTransferId is null)
        {
            return;
        }

        try
        {
            var transfer = await _finix.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = payment.CaptureTransferId }, cancellationToken: ct);

            payment.CapturedAmountCents = transfer.Amount ?? payment.AmountCents;
            payment.FeeCents = transfer.Fee;
            payment.NetCents = transfer.Fee.HasValue
                ? payment.CapturedAmountCents - transfer.Fee.Value
                : null;
            payment.CapturedAt ??= Now;
            payment.UpdatedAt = Now;
        }
        catch (SdkException ex)
        {
            _logger.LogWarning(ex,
                "Captured order {OrderId} but could not read transfer {TransferId} for fee details.",
                payment.OrderId, payment.CaptureTransferId);
            payment.CapturedAmountCents ??= payment.AmountCents;
            payment.CapturedAt ??= Now;
            payment.UpdatedAt = Now;
        }
    }

    private async Task<OrderOperationClaim?> TryClaimOrderOperationAsync(
        int orderId, string operation, CancellationToken ct)
    {
        var claimKey = $"{orderId}:{operation}";
        var existing = await _db.OrderOperationClaims.FindAsync(new object[] { claimKey }, ct);
        if (existing is not null)
        {
            if (Now - existing.CreatedAt < ClaimTtl)
            {
                return null;
            }

            _db.OrderOperationClaims.Remove(existing);
            await _db.SaveChangesAsync(ct);
        }

        var claim = new OrderOperationClaim
        {
            ClaimKey = claimKey,
            OrderId = orderId,
            Operation = operation,
            CreatedAt = Now
        };

        _db.OrderOperationClaims.Add(claim);
        try
        {
            await _db.SaveChangesAsync(ct);
            return claim;
        }
        catch (DbUpdateException)
        {
            _db.Entry(claim).State = EntityState.Detached;
            return null;
        }
    }

    private async Task ReleaseClaimAsync(OrderOperationClaim claim, CancellationToken ct)
    {
        try
        {
            _db.OrderOperationClaims.Remove(claim);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // The claim expires on its own after ClaimTtl.
        }
    }

    private async Task ReleaseRefundLockAsync(int orderId, CancellationToken ct)
    {
        try
        {
            var row = await _db.RefundLocks.FindAsync(new object[] { orderId }, ct);
            if (row is not null)
            {
                _db.RefundLocks.Remove(row);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // The lock is short-lived; a retry can reclaim it once it is gone.
        }
    }

    private async Task<PagedResult<Transfer>> ListTransfersAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var items = new List<Transfer>();
        string? cursor = null;
        string? nextCursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _finix.TransfersApi.ListTransfers(
                new ListTransfersRequest
                {
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    Merchant = _options.MerchantId,
                    Currency = _options.Currency,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            if (response.Embedded?.Transfers is not null)
            {
                items.AddRange(response.Embedded.Transfers);
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                return new PagedResult<Transfer>(items, false, null);
            }

            nextCursor = cursor;
        }

        return new PagedResult<Transfer>(items, true, nextCursor);
    }

    private async Task<PagedResult<Authorization>> ListAuthorizationsAsync(
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var items = new List<Authorization>();
        string? cursor = null;
        string? nextCursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _finix.AuthorizationsApi.ListAuthorizations(
                new ListAuthorizationsRequest
                {
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    Merchant = _options.MerchantId,
                    Currency = _options.Currency,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            if (response.Embedded?.Authorizations is not null)
            {
                items.AddRange(response.Embedded.Authorizations);
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                return new PagedResult<Authorization>(items, false, null);
            }

            nextCursor = cursor;
        }

        return new PagedResult<Authorization>(items, true, nextCursor);
    }

    private async Task<List<string>> ListReversalIdsAsync(string transferId, CancellationToken ct)
    {
        var ids = new List<string>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            var response = await _finix.TransfersApi.ListTransferReversals(
                new ListTransferReversalsRequest
                {
                    TransferId = transferId,
                    Limit = PageSize,
                    AfterCursor = cursor
                },
                cancellationToken: ct);

            if (response.Embedded?.Transfers is not null)
            {
                ids.AddRange(response.Embedded.Transfers
                    .Where(t => t.Id is not null)
                    .Select(t => t.Id!));
            }

            cursor = response.Page?.NextCursor;
            if (string.IsNullOrEmpty(cursor))
            {
                break;
            }
        }

        return ids;
    }

    private static void ApplyAuthorization(OrderPayment payment, Authorization authorization)
    {
        payment.AuthorizationId = authorization.Id ?? payment.AuthorizationId;
        payment.AuthorizationState = authorization.State?.Value;
        payment.FailureCode = authorization.FailureCode;
        payment.FailureMessage = authorization.FailureMessage;

        if (authorization.Transfer is not null)
        {
            payment.Status = PaymentStatus.Captured;
            payment.CaptureTransferId = authorization.Transfer;
            payment.CapturedAt ??= payment.UpdatedAt;
        }
        else if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
        {
            payment.Status = PaymentStatus.Voided;
            payment.VoidedAt ??= payment.UpdatedAt;
        }
        else if (authorization.State == State1.Succeeded)
        {
            payment.Status = PaymentStatus.Authorized;
            payment.AuthorizedAt ??= payment.UpdatedAt;
        }
        else if (authorization.State == State1.Failed || authorization.State == State1.Canceled)
        {
            payment.Status = PaymentStatus.Failed;
        }
        else
        {
            payment.Status = PaymentStatus.Unknown;
        }
    }

    private static string? CaptureBlockReason(Authorization authorization, DateTimeOffset now)
    {
        if (authorization.Transfer is not null)
        {
            return $"Authorization {authorization.Id} was already captured (transfer {authorization.Transfer}).";
        }

        if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
        {
            return $"Authorization {authorization.Id} was voided and can no longer be captured.";
        }

        if (authorization.State == State1.Canceled)
        {
            return $"Authorization {authorization.Id} was cancelled and can no longer be captured.";
        }

        if (authorization.State == State1.Failed)
        {
            return $"Authorization {authorization.Id} failed ({authorization.FailureCode}: {authorization.FailureMessage}) " +
                   "and can no longer be captured.";
        }

        if (authorization.State == State1.Unknown)
        {
            return $"Authorization {authorization.Id} is in an unknown state and cannot be captured safely.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value <= now)
        {
            return $"Authorization {authorization.Id} expired at {authorization.ExpiresAt.Value:O} and can no longer be captured.";
        }

        return null;
    }

    private static string DescribeHoldState(Authorization? authorization)
    {
        if (authorization is null)
        {
            return "The authorization could not be re-read from Finix.";
        }

        return $"Finix now reports authorization {authorization.Id} as state={authorization.State?.Value ?? "UNKNOWN"}, " +
               $"is_void={authorization.IsVoid}, void_state={authorization.VoidState?.Value ?? "UNKNOWN"}, " +
               $"expires_at={authorization.ExpiresAt?.ToString("O") ?? "UNKNOWN"}, " +
               $"failure_code={authorization.FailureCode ?? "none"}, failure_message={authorization.FailureMessage ?? "none"}.";
    }

    private static bool IsPendingCaptureRejection(GenericError error) =>
        error.Embedded?.Errors?.Any(e =>
            e.Code == "CONFLICT" &&
            e.Message?.Contains("PENDING", StringComparison.OrdinalIgnoreCase) == true) == true;

    private static bool IsPendingCaptureRejection(ApiError error, HttpStatusCode status) =>
        status == HttpStatusCode.Conflict &&
        error.TryGetRawError(out var raw) &&
        raw.ReadAsString().Contains("PENDING", StringComparison.OrdinalIgnoreCase);

    private static string Describe(GenericError error)
    {        var first = error.Embedded?.Errors?.FirstOrDefault();
        if (first is null)
        {
            return "unknown provider error";
        }

        var message = $"{first.Code}: {first.Message}";
        if (!string.IsNullOrWhiteSpace(first.Logref))
        {
            message += $" (logref {first.Logref})";
        }

        return Truncate(message);
    }

    private static string Describe(ApiError error) =>
        error.TryGetRawError(out var raw) ? Truncate(raw.ReadAsString()) : "no provider error body";

    private static string Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "no provider error body";
        }

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray());
        return cleaned.Length <= 800 ? cleaned : cleaned[..800];
    }

    private static string DescribeAuthorizationFailure(Authorization authorization) =>
        $"Finix declined the authorization ({authorization.FailureCode}: {authorization.FailureMessage}).";

    private static PayOrderResponse MapPay(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AmountCents = payment.AmountCents,
        Currency = payment.Currency
    };

    private static FulfilOrderResponse MapFulfil(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        CaptureTransferId = payment.CaptureTransferId,
        CapturedAmountCents = payment.CapturedAmountCents,
        FeeCents = payment.FeeCents,
        NetCents = payment.NetCents,
        Currency = payment.Currency
    };

    private static CancelOrderResponse MapCancel(OrderPayment payment) => new()
    {
        OrderId = payment.OrderId,
        PaymentStatus = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        VoidedAt = payment.VoidedAt
    };

    private static RefundOrderResponse MapRefund(RefundRecord record, long refundedCents) => new()
    {
        RefundId = record.RefundId ?? string.Empty,
        OrderId = record.OrderId,
        AmountCents = record.AmountCents,
        RefundedCents = refundedCents,
        Status = record.Status
    };

    private static SavePaymentMethodResponse MapSavedCard(SavedCard card) => new()
    {
        PaymentMethodId = card.PaymentInstrumentId,
        Brand = card.Brand,
        Last4 = card.LastFour,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear,
        Name = card.Name
    };

    private static PaymentMethodSummary MapPaymentMethodSummary(SavedCard card) => new()
    {
        PaymentMethodId = card.PaymentInstrumentId,
        Brand = card.Brand,
        Last4 = card.LastFour,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear,
        Name = card.Name,
        CreatedAt = card.CreatedAt
    };

    private static PaymentSummary MapPaymentSummary(OrderPayment payment) => new()
    {
        Status = payment.Status.ToString(),
        AuthorizationId = payment.AuthorizationId,
        AuthorizationState = payment.AuthorizationState,
        AmountCents = payment.AmountCents,
        Currency = payment.Currency,
        CaptureTransferId = payment.CaptureTransferId,
        CapturedAmountCents = payment.CapturedAmountCents,
        FeeCents = payment.FeeCents,
        NetCents = payment.NetCents,
        RefundedCents = payment.RefundedCents,
        FailureCode = payment.FailureCode,
        FailureMessage = payment.FailureMessage
    };

    private static ReconciliationFinixTransaction MapTransfer(Transfer transfer) => new()
    {
        Kind = "TRANSFER",
        Id = transfer.Id ?? string.Empty,
        State = transfer.State?.Value,
        Type = transfer.Type?.Value,
        AmountCents = transfer.Amount,
        Currency = transfer.Currency?.Value,
        Merchant = transfer.Merchant,
        ParentTransfer = transfer.ParentTransfer,
        CreatedAt = transfer.CreatedAt
    };

    private static ReconciliationFinixTransaction MapAuthorization(Authorization authorization) => new()
    {
        Kind = "AUTHORIZATION",
        Id = authorization.Id ?? string.Empty,
        State = authorization.State?.Value,
        Type = "AUTHORIZATION",
        AmountCents = authorization.Amount,
        Currency = authorization.Currency?.Value,
        Merchant = authorization.Merchant,
        CreatedAt = authorization.CreatedAt
    };

    private static bool InRange(DateTimeOffset? value, DateTimeOffset from, DateTimeOffset to) =>
        value.HasValue && value.Value >= from && value.Value <= to;

    private static void RequireShopper(string shopperId)
    {
        if (string.IsNullOrWhiteSpace(shopperId))
        {
            throw new PaymentValidationException("The caller could not be identified from the token.");
        }
    }

    private static void RequireAddress(ShippingAddressRequest address)
    {
        if (string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            throw new PaymentValidationException(
                "ShipToAddress requires Street, City, Country and ZipCode.");
        }
    }

    private static CardData ValidateCard(
        string number, int expirationMonth, int expirationYear, string securityCode, string name)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length is < 12 or > 19)
        {
            throw new PaymentValidationException("Card number must contain between 12 and 19 digits.");
        }

        if (expirationMonth is < 1 or > 12)
        {
            throw new PaymentValidationException("ExpirationMonth must be between 1 and 12.");
        }

        var now = DateTimeOffset.UtcNow;
        if (expirationYear < now.Year || expirationYear > now.Year + 30 ||
            (expirationYear == now.Year && expirationMonth < now.Month))
        {
            throw new PaymentValidationException("The card expiration date is in the past or invalid.");
        }

        var cvv = new string((securityCode ?? string.Empty).Where(char.IsDigit).ToArray());
        if (cvv.Length is < 3 or > 4)
        {
            throw new PaymentValidationException("SecurityCode must contain 3 or 4 digits.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new PaymentValidationException("The cardholder name is required.");
        }

        return new CardData(
            digits,
            digits[^4..],
            expirationMonth,
            expirationYear,
            cvv,
            name.Trim());
    }

    private Currency FinixCurrency() => _options.Currency == "CAD" ? Currency.Cad : Currency.Usd;

    private static long ToCents(decimal amount) =>
        checked((long)Math.Round(amount * 100m, 0, MidpointRounding.AwayFromZero));

    private async Task TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // A failed bookkeeping write must not replace the provider failure the caller needs.
        }
    }

    private async Task<T> RunBoundedAsync<T>(
        TimeSpan budget, CancellationToken ct, Func<CancellationToken, Task<T>> action)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(budget);

        try
        {
            return await action(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && cts.IsCancellationRequested)
        {
            throw new PaymentProviderException(
                $"The payment operation exceeded its {budget.TotalSeconds:0}-second budget; " +
                "the outcome may be unknown.", outcomeUnknown: true);
        }
    }

    private sealed record CardData(
        string Number,
        string LastFour,
        int ExpirationMonth,
        int ExpirationYear,
        string SecurityCode,
        string Name);

    private sealed record PagedResult<T>(List<T> Items, bool Truncated, string? NextCursor);
}
