using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FinixApiReference;
using FinixApiReference.Core.Exceptions;
using FinixApiReference.Errors;
using FinixApiReference.Models;
using FinixApiReference.Models.AnyOf;
using FinixApiReference.Models.Enums;
using FinixAuthorization = FinixApiReference.Models.Authorization;
using FinixApiReference.Requests.AuthorizationsApi;
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.Identities;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.Infrastructure.Data;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.PublicApi.Services;

public sealed class FinixPaymentService : IFinixPaymentService
{
    private const int PageSize = 100;
    private const int MaxPages = 1000;

    private readonly CatalogContext _db;
    private readonly FinixApiReferenceClient _finix;
    private readonly FinixOptions _options;
    private readonly KeyedLock _locks;

    public FinixPaymentService(CatalogContext db, FinixApiReferenceClient finix, IOptions<FinixOptions> options, KeyedLock locks)
    {
        _db = db;
        _finix = finix;
        _options = options.Value;
        _locks = locks;
    }

    public async Task<OrderDto> CreateOrderAsync(string buyerId, CreateOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "At least one catalog item is required.");
        }

        var quantities = request.Items
            .GroupBy(i => i.CatalogItemId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        if (quantities.Any(kvp => kvp.Key <= 0 || kvp.Value <= 0))
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Catalog item ids and quantities must be positive.");
        }

        var catalogItemIds = quantities.Keys.ToArray();
        var catalogItems = await _db.CatalogItems
            .Where(i => catalogItemIds.Contains(i.Id))
            .ToListAsync(cancellationToken);

        if (catalogItems.Count != catalogItemIds.Length)
        {
            var missing = catalogItemIds.Except(catalogItems.Select(i => i.Id));
            throw new PaymentOperationException(HttpStatusCode.BadRequest,
                $"Unknown catalog item id(s): {string.Join(", ", missing)}.");
        }

        var orderItems = catalogItems.Select(catalogItem =>
        {
            var pictureUri = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                ? "images/products/placeholder.png"
                : catalogItem.PictureUri;
            var itemOrdered = new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, pictureUri);
            return new OrderItem(itemOrdered, catalogItem.Price, quantities[catalogItem.Id]);
        }).ToList();

        var shipTo = MapShipToAddress(request.ShippingAddress);
        var order = new Order(buyerId, shipTo, orderItems);

        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(order, Array.Empty<OrderRefund>());
    }

    public async Task<OrderDto> PayOrderAsync(string buyerId, PayOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        if ((request.Card is null) == (request.PaymentMethodId is null))
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest,
                "Provide exactly one of card details or paymentMethodId.");
        }

        using var orderLock = await _locks.AcquireAsync($"order:{request.OrderId}", cancellationToken);

        var order = await LoadOrderAsync(request.OrderId, cancellationToken);
        if (order is null || !string.Equals(order.BuyerId, buyerId, StringComparison.Ordinal))
        {
            throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
        }

        var refunds = await LoadRefundsAsync(order.Id, cancellationToken);

        switch (order.PaymentStatus)
        {
            case OrderPaymentStatus.Authorized:
            case OrderPaymentStatus.CapturePending:
            case OrderPaymentStatus.Captured:
            case OrderPaymentStatus.PartiallyRefunded:
            case OrderPaymentStatus.Refunded:
                return ToDto(order, refunds);
            case OrderPaymentStatus.Cancelled:
                throw new PaymentOperationException(HttpStatusCode.Conflict, "The order has been cancelled and cannot be paid.");
            case OrderPaymentStatus.CaptureFailed:
                throw new PaymentOperationException(HttpStatusCode.Conflict,
                    $"The payment was authorized but capture failed: {order.PaymentFailureMessage}. Cancel the order and ask the shopper to pay again.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.Failed)
        {
            order.RotatePaymentAttempt();
        }

        var amountCents = order.TotalInCents();
        if (amountCents <= 0)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "The order total must be greater than zero.");
        }

        var currencyCode = _options.Currency;
        var currency = ToFinixCurrency(currencyCode);
        var sourceId = string.Empty;
        int? savedPaymentMethodId = null;
        string? securityCode = null;

        if (request.PaymentMethodId.HasValue)
        {
            var saved = await _db.SavedPaymentMethods
                .FirstOrDefaultAsync(p => p.Id == request.PaymentMethodId.Value && p.BuyerId == buyerId, cancellationToken);
            if (saved is null)
            {
                throw new PaymentOperationException(HttpStatusCode.NotFound, "Saved payment method not found.");
            }

            sourceId = saved.FinixPaymentInstrumentId;
            savedPaymentMethodId = saved.Id;
        }
        else
        {
            var card = request.Card!;
            var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
            var paymentInstrument = await CreatePaymentInstrumentAsync(identityId, card, cancellationToken);
            sourceId = paymentInstrument.Id
                ?? throw new PaymentOperationException(HttpStatusCode.BadGateway, "Finix did not return a payment instrument id.");
            securityCode = card.SecurityCode;
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey)
            ? $"order-{order.Id}-{order.PaymentAttemptId}-authorize"
            : request.IdempotencyKey.Trim();

        try
        {
            var authorization = await _finix.AuthorizationsApi.CreateAuthorization(new CreateAuthorizationRequest
            {
                Body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
                {
                    Amount = amountCents,
                    Currency = currency,
                    Merchant = _options.MerchantId,
                    Source = sourceId,
                    SecurityCode = securityCode,
                    IdempotencyId = idempotencyKey,
                    Tags = new Dictionary<string, string>
                    {
                        ["orderId"] = order.Id.ToString(),
                        ["orderTotalCents"] = amountCents.ToString()
                    }
                })
            }, cancellationToken: cancellationToken);

            var state = authorization.State?.Value ?? "UNKNOWN";
            if (state == "SUCCEEDED" || state == "PENDING")
            {
                order.MarkAuthorized(authorization.Id ?? string.Empty, state, amountCents, currencyCode, savedPaymentMethodId);
                await _db.SaveChangesAsync(cancellationToken);
                return ToDto(order, refunds);
            }

            var failure = authorization.FailureMessage
                ?? $"Finix reported authorization state {state}.";
            order.MarkPaymentFailed(failure);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.UnprocessableEntity, failure);
        }
        catch (ApiException ex)
        {
            var message = $"Finix could not authorize the order: {DescribeFinixError(ex)}";
            order.MarkPaymentFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.UnprocessableEntity, message);
        }
    }

    public async Task<OrderDto> FulfilOrderAsync(FulfilOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        using var orderLock = await _locks.AcquireAsync($"order:{request.OrderId}", cancellationToken);

        var order = await LoadOrderAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
        }

        var refunds = await LoadRefundsAsync(order.Id, cancellationToken);

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled)
        {
            return ToDto(order, refunds);
        }

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Cancelled || order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict, "The order has been cancelled and cannot be fulfilled.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.CapturePending && !string.IsNullOrWhiteSpace(order.FinixTransferId))
        {
            var pendingAmount = order.AuthorizedAmountCents > 0 ? order.AuthorizedAmountCents : order.TotalInCents();
            return await CompleteCaptureAsync(order, order.FinixTransferId!, pendingAmount, refunds, cancellationToken);
        }

        if (order.PaymentStatus != OrderPaymentStatus.Authorized &&
            order.PaymentStatus != OrderPaymentStatus.CaptureFailed)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"The order is not authorized and cannot be fulfilled (payment status: {order.PaymentStatus}). Pay for the order first.");
        }

        if (string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "The order has no Finix authorization id. Cancel it and ask the shopper to pay again.");
        }

        var authorizationId = order.FinixAuthorizationId!;
        var authorization = await GetAuthorizationAsync(authorizationId, cancellationToken);
        var authorizationState = authorization.State?.Value ?? "UNKNOWN";

        if (authorization.IsVoid == true || authorizationState == "CANCELED")
        {
            var message = $"Finix authorization {authorizationId} is voided or cancelled and can no longer be captured. " +
                          "Cancel the order and ask the shopper to pay again.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            var message = $"Finix authorization {authorizationId} expired at {authorization.ExpiresAt.Value:O} and can no longer be captured. " +
                          "Cancel the order and ask the shopper to pay again.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        if (authorizationState != "SUCCEEDED")
        {
            var message = $"Finix authorization {authorizationId} is in state {authorizationState} and cannot be captured yet. " +
                          "Retry fulfilment after the authorization succeeds, or cancel the order and ask the shopper to pay again.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        var captureAmount = order.AuthorizedAmountCents > 0 ? order.AuthorizedAmountCents : order.TotalInCents();
        FinixAuthorization captured;
        try
        {
            captured = await _finix.AuthorizationsApi.CaptureAuthorization(new CaptureAuthorizationRequest
            {
                AuthorizationId = authorizationId,
                Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                {
                    CaptureAmount = captureAmount
                })
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException ex)
        {
            var message = $"Finix could not capture authorization {authorizationId}: {DescribeFinixError(ex)}. " +
                          "The hold may have expired or been voided; cancel the order and ask the shopper to pay again.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        var transferId = captured.Transfer;
        if (string.IsNullOrWhiteSpace(transferId))
        {
            var message = $"Finix captured authorization {authorizationId} but returned no transfer id. " +
                          "Check the Finix dashboard and reconcile before retrying.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        return await CompleteCaptureAsync(order, transferId!, captureAmount, refunds, cancellationToken);
    }

    private async Task<OrderDto> CompleteCaptureAsync(Order order, string transferId, long captureAmount,
        List<OrderRefund> refunds, CancellationToken cancellationToken)
    {
        Transfer? transfer = null;
        var transferState = "UNKNOWN";

        for (var attempt = 0; attempt < 15; attempt++)
        {
            transfer = await _finix.TransfersApi.GetTransfer(new GetTransferRequest
            {
                TransferId = transferId
            }, cancellationToken: cancellationToken);

            transferState = transfer.State?.Value ?? "UNKNOWN";
            if (transferState != "PENDING")
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        if (transferState == "PENDING")
        {
            order.MarkCapturePending(transferId, transferState);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"Finix capture transfer {transferId} is still PENDING. The capture has been initiated; retry fulfil in a few seconds to record the final state. Do not authorize the order again.");
        }

        if (transferState != "SUCCEEDED")
        {
            var message = $"Finix capture transfer {transferId} is in state {transferState}; the money was not taken. " +
                          "Check the payment in Finix, then cancel the order and ask the shopper to pay again.";
            order.MarkCaptureFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.Conflict, message);
        }

        var capturedAmount = transfer?.Amount ?? captureAmount;
        var feeCents = await SumFeesAsync(transferId, cancellationToken);
        var netCents = capturedAmount - feeCents;

        order.MarkCaptured(transferId, transferState, capturedAmount, feeCents, netCents);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(order, refunds);
    }

    public async Task<OrderDto> CancelOrderAsync(CancelOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        using var orderLock = await _locks.AcquireAsync($"order:{request.OrderId}", cancellationToken);

        var order = await LoadOrderAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
        }

        var refunds = await LoadRefundsAsync(order.Id, cancellationToken);

        if (order.FulfilmentStatus == OrderFulfilmentStatus.Fulfilled ||
            order.PaymentStatus == OrderPaymentStatus.CapturePending ||
            order.PaymentStatus == OrderPaymentStatus.Captured ||
            order.PaymentStatus == OrderPaymentStatus.PartiallyRefunded ||
            order.PaymentStatus == OrderPaymentStatus.Refunded)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "The payment has already been captured or capture is in progress. Use the refunds endpoint instead of cancelling.");
        }

        if (order.PaymentStatus == OrderPaymentStatus.Cancelled)
        {
            return ToDto(order, refunds);
        }

        string? authorizationState = order.FinixAuthorizationState;
        string? voidState = order.FinixVoidState;

        if (order.PaymentStatus == OrderPaymentStatus.Authorized && !string.IsNullOrWhiteSpace(order.FinixAuthorizationId))
        {
            try
            {
                var authorization = await GetAuthorizationAsync(order.FinixAuthorizationId!, cancellationToken);
                authorizationState = authorization.State?.Value ?? authorizationState;
                voidState = authorization.VoidState?.Value ?? (authorization.IsVoid == true ? "VOIDED" : voidState);
                if (authorization.IsVoid != true && authorizationState != "CANCELED")
                {
                    var voided = await _finix.AuthorizationsApi.VoidAuthorization(new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = order.FinixAuthorizationId!,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    }, cancellationToken: cancellationToken);
                    authorizationState = voided.State?.Value ?? authorizationState;
                    voidState = voided.VoidState?.Value ?? "VOIDED";
                }
            }
            catch (ApiException ex)
            {
                throw new PaymentOperationException(HttpStatusCode.BadGateway,
                    $"Finix could not release the authorization {order.FinixAuthorizationId}: {DescribeFinixError(ex)}. No money was captured; retry cancel.");
            }
        }

        order.MarkCancelled(authorizationState, voidState);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(order, refunds);
    }

    public async Task<OrderDto> RefundOrderAsync(RefundOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "idempotencyKey is required for refunds.");
        }

        var idempotencyKey = request.IdempotencyKey.Trim();

        using var orderLock = await _locks.AcquireAsync($"order:{request.OrderId}", cancellationToken);

        var order = await LoadOrderAsync(request.OrderId, cancellationToken);
        if (order is null)
        {
            throw new PaymentOperationException(HttpStatusCode.NotFound, "Order not found.");
        }

        var existing = await _db.OrderRefunds
            .FirstOrDefaultAsync(r => r.OrderId == order.Id && r.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null)
        {
            return ToDto(order, await LoadRefundsAsync(order.Id, cancellationToken));
        }

        if (order.FulfilmentStatus != OrderFulfilmentStatus.Fulfilled ||
            order.PaymentStatus != OrderPaymentStatus.Captured &&
            order.PaymentStatus != OrderPaymentStatus.PartiallyRefunded &&
            order.PaymentStatus != OrderPaymentStatus.Refunded)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "Only a captured payment can be refunded. Fulfil the order first.");
        }

        if (string.IsNullOrWhiteSpace(order.FinixTransferId))
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "The order has no Finix capture transfer id, so it cannot be refunded.");
        }

        var remaining = order.CapturedAmountCents - order.RefundedAmountCents;
        if (remaining <= 0)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                "The captured payment has already been fully refunded.");
        }

        var amountCents = request.AmountCents ?? remaining;
        if (amountCents <= 0)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "The refund amount must be greater than zero.");
        }

        if (amountCents > remaining)
        {
            throw new PaymentOperationException(HttpStatusCode.Conflict,
                $"The refund amount {amountCents} exceeds the remaining refundable amount {remaining}.");
        }

        if (amountCents > int.MaxValue)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "The refund amount is too large for a single Finix refund.");
        }

        var refund = new OrderRefund(order.Id, amountCents, idempotencyKey);
        _db.OrderRefunds.Add(refund);
        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            var body = new RefundTransfer { RefundAmount = (int)amountCents };
            body.AdditionalProperties.Set("idempotency_id", idempotencyKey);

            var reversal = await _finix.TransfersApi.CreateTransferReversal(new CreateTransferReversalRequest
            {
                TransferId = order.FinixTransferId!,
                Body = TransfersReversalsRequest.RefundTransfer(body)
            }, cancellationToken: cancellationToken);

            var state = reversal.State?.Value ?? "UNKNOWN";
            if (state == "FAILED" || state == "CANCELED" || state == "RETURNED")
            {
                var failure = reversal.FailureMessage ?? $"Finix reported refund state {state}.";
                refund.MarkFailed(failure);
                await _db.SaveChangesAsync(cancellationToken);
                throw new PaymentOperationException(HttpStatusCode.UnprocessableEntity, failure);
            }

            refund.MarkSucceeded(reversal.Id ?? string.Empty, state);
            order.ApplyRefund(amountCents);
            await _db.SaveChangesAsync(cancellationToken);

            return ToDto(order, await LoadRefundsAsync(order.Id, cancellationToken));
        }
        catch (ApiException ex)
        {
            var message = $"Finix could not refund the payment: {DescribeFinixError(ex)}";
            refund.MarkFailed(message);
            await _db.SaveChangesAsync(cancellationToken);
            throw new PaymentOperationException(HttpStatusCode.UnprocessableEntity, message);
        }
    }

    public async Task<IReadOnlyList<OrderDto>> GetMyOrdersAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        var orders = await _db.Orders
            .Where(o => o.BuyerId == buyerId)
            .Include(o => o.OrderItems)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var refunds = await _db.OrderRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        return orders
            .Select(o => ToDto(o, refunds.Where(r => r.OrderId == o.Id)))
            .ToList();
    }

    public async Task<ReconciliationResponse> ReconcileAsync(ReconciliationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.From == default || request.To == default)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Both from and to ISO-8601 date-times are required.");
        }

        if (request.From > request.To)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "from must be before or equal to to.");
        }

        var response = new ReconciliationResponse
        {
            From = request.From,
            To = request.To
        };

        var orders = await _db.Orders
            .Where(o => o.OrderDate >= request.From && o.OrderDate <= request.To &&
                        (o.PaymentStatus != OrderPaymentStatus.AwaitingPayment ||
                         o.FinixAuthorizationId != null ||
                         o.FinixTransferId != null))
            .ToListAsync(cancellationToken);

        var orderIds = orders.Select(o => o.Id).ToArray();
        var refunds = await _db.OrderRefunds
            .Where(r => orderIds.Contains(r.OrderId))
            .ToListAsync(cancellationToken);

        var ordersByAuthorization = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.FinixAuthorizationId))
            .ToDictionary(o => o.FinixAuthorizationId!, o => o);
        var ordersByTransfer = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.FinixTransferId))
            .ToDictionary(o => o.FinixTransferId!, o => o);
        var ordersByRefundTransfer = refunds
            .Where(r => !string.IsNullOrWhiteSpace(r.FinixTransferId))
            .GroupBy(r => r.FinixTransferId!)
            .ToDictionary(g => g.Key, g => g.First().OrderId);

        var matchedOrderIds = new HashSet<int>();

        var authorizations = await ListAllAuthorizationsAsync(request.From, request.To, cancellationToken);
        foreach (var authorization in authorizations)
        {
            var id = authorization.Id ?? string.Empty;
            Order? matchedOrder = null;
            var matched = !string.IsNullOrEmpty(id) && ordersByAuthorization.TryGetValue(id, out matchedOrder);
            if (matched)
            {
                matchedOrderIds.Add(matchedOrder!.Id);
            }

            response.FinixTransactions.Add(new FinixTransactionDto
            {
                Source = "authorization",
                Id = id,
                Type = "AUTHORIZATION",
                State = authorization.State?.Value,
                AmountCents = authorization.Amount,
                Currency = authorization.Currency?.Value,
                CreatedAt = authorization.CreatedAt,
                OrderId = matched ? matchedOrder!.Id : null,
                Matched = matched
            });
        }

        var transfers = await ListAllTransfersAsync(request.From, request.To, cancellationToken);
        foreach (var transfer in transfers)
        {
            var id = transfer.Id ?? string.Empty;
            int? orderId = null;

            if (!string.IsNullOrEmpty(id) && ordersByTransfer.TryGetValue(id, out var matchedByTransfer))
            {
                orderId = matchedByTransfer.Id;
            }
            else if (!string.IsNullOrEmpty(id) && ordersByRefundTransfer.TryGetValue(id, out var refundOrderId))
            {
                orderId = refundOrderId;
            }
            else if (!string.IsNullOrWhiteSpace(transfer.ParentTransfer) &&
                     ordersByTransfer.TryGetValue(transfer.ParentTransfer!, out var matchedByParent))
            {
                orderId = matchedByParent.Id;
            }
            else if (TryGetOrderIdTag(transfer.Tags, out var taggedOrderId) &&
                     orders.Any(o => o.Id == taggedOrderId))
            {
                orderId = taggedOrderId;
            }

            if (orderId.HasValue)
            {
                matchedOrderIds.Add(orderId.Value);
            }

            response.FinixTransactions.Add(new FinixTransactionDto
            {
                Source = "transfer",
                Id = id,
                Type = transfer.Type?.Value,
                State = transfer.State?.Value,
                AmountCents = transfer.Amount,
                Currency = transfer.Currency?.Value,
                CreatedAt = transfer.CreatedAt,
                OrderId = orderId,
                Matched = orderId.HasValue
            });
        }

        foreach (var order in orders)
        {
            var matched = matchedOrderIds.Contains(order.Id);

            if (matched)
            {
                matchedOrderIds.Add(order.Id);
            }

            response.EshopOrders.Add(new ReconciliationOrderDto
            {
                OrderId = order.Id,
                OrderDate = order.OrderDate,
                TotalCents = order.TotalInCents(),
                PaymentStatus = order.PaymentStatus.ToString(),
                FulfilmentStatus = order.FulfilmentStatus.ToString(),
                FinixAuthorizationId = order.FinixAuthorizationId,
                FinixTransferId = order.FinixTransferId,
                Matched = matched
            });
        }

        foreach (var transaction in response.FinixTransactions.Where(t => !t.Matched))
        {
            response.Discrepancies.Add(new ReconciliationDiscrepancyDto
            {
                Kind = "FinixOnly",
                Message = $"Finix {transaction.Source} {transaction.Id} ({transaction.Type}, {transaction.State}) has no matching eShop order.",
                FinixTransactionId = transaction.Id
            });
        }

        foreach (var order in response.EshopOrders.Where(o => !o.Matched))
        {
            response.Discrepancies.Add(new ReconciliationDiscrepancyDto
            {
                Kind = "EshopOnly",
                Message = $"eShop order {order.OrderId} has no matching Finix transaction in the requested range.",
                OrderId = order.OrderId
            });
        }

        return response;
    }

    public async Task<PaymentMethodDto> SavePaymentMethodAsync(string buyerId, SavePaymentMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        if (request.Card is null)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "Card details are required.");
        }

        var identityId = await GetOrCreateIdentityAsync(buyerId, cancellationToken);
        var paymentInstrument = await CreatePaymentInstrumentAsync(identityId, request.Card, cancellationToken);

        var saved = new SavedPaymentMethod(
            buyerId,
            identityId,
            paymentInstrument.Id ?? throw new PaymentOperationException(HttpStatusCode.BadGateway, "Finix did not return a payment instrument id."),
            paymentInstrument.Brand?.Value,
            paymentInstrument.LastFour,
            paymentInstrument.ExpirationMonth,
            paymentInstrument.ExpirationYear,
            paymentInstrument.Name);

        _db.SavedPaymentMethods.Add(saved);
        await _db.SaveChangesAsync(cancellationToken);

        return ToDto(saved);
    }

    public async Task<IReadOnlyList<PaymentMethodDto>> GetPaymentMethodsAsync(string buyerId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        var methods = await _db.SavedPaymentMethods
            .Where(p => p.BuyerId == buyerId)
            .OrderByDescending(p => p.CreatedAt)
            .ToListAsync(cancellationToken);

        return methods.Select(ToDto).ToList();
    }

    public async Task DeletePaymentMethodAsync(string buyerId, int paymentMethodId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            throw new PaymentOperationException(HttpStatusCode.Unauthorized, "The access token does not identify a shopper.");
        }

        var saved = await _db.SavedPaymentMethods
            .FirstOrDefaultAsync(p => p.Id == paymentMethodId && p.BuyerId == buyerId, cancellationToken);
        if (saved is null)
        {
            throw new PaymentOperationException(HttpStatusCode.NotFound, "Saved payment method not found.");
        }

        try
        {
            await _finix.PaymentInstrumentsApi.UpdatePaymentInstrument(new UpdatePaymentInstrumentRequest
            {
                PaymentInstrumentId = saved.FinixPaymentInstrumentId,
                Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                    new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
            }, cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // The token is already gone at Finix; removing our reference is still correct.
        }
        catch (ApiException ex)
        {
            throw new PaymentOperationException(HttpStatusCode.BadGateway,
                $"Finix could not disable the saved card: {DescribeFinixError(ex)}");
        }

        _db.SavedPaymentMethods.Remove(saved);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Order?> LoadOrderAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.Orders
            .Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken);

    private async Task<List<OrderRefund>> LoadRefundsAsync(int orderId, CancellationToken cancellationToken) =>
        await _db.OrderRefunds
            .Where(r => r.OrderId == orderId)
            .OrderBy(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    private async Task<string> GetOrCreateIdentityAsync(string buyerId, CancellationToken cancellationToken)
    {
        using var buyerLock = await _locks.AcquireAsync($"buyer:{buyerId}", cancellationToken);

        var profile = await _db.FinixCustomerProfiles
            .FirstOrDefaultAsync(p => p.BuyerId == buyerId, cancellationToken);
        if (profile is not null)
        {
            return profile.FinixIdentityId;
        }

        var identity = await _finix.Identities.CreateIdentity(new CreateIdentityRequest
        {
            Body = IdentitiesRequest.Buyer(new Buyer
            {
                Entity = new Entity5 { Email = buyerId },
                IdentityRoles = new[] { IdentityRole5.Buyer },
                Type = Type10.Personal
            })
        }, cancellationToken: cancellationToken);

        var identityId = identity.Id
            ?? throw new PaymentOperationException(HttpStatusCode.BadGateway, "Finix did not return an identity id.");

        profile = new FinixCustomerProfile(buyerId, identityId);
        _db.FinixCustomerProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);

        return identityId;
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(string identityId, CardDetailsDto card,
        CancellationToken cancellationToken)
    {
        var number = NormalizeCardNumber(card.Number);
        if (number.Length is < 12 or > 19 || !number.All(char.IsDigit))
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12 || card.ExpirationYear < 2000)
        {
            throw new PaymentOperationException(HttpStatusCode.BadRequest, "A valid card expiration month and year are required.");
        }

        var name = string.IsNullOrWhiteSpace(card.Name) ? "eShop Shopper" : card.Name.Trim();
        var finixAddress = MapFinixAddress(card.Address);

        try
        {
            var created = await _finix.PaymentInstrumentsApi.CreatePaymentInstrument(new CreatePaymentInstrumentRequest
            {
                Body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
                {
                    Number = number,
                    ExpirationMonth = card.ExpirationMonth,
                    ExpirationYear = card.ExpirationYear,
                    SecurityCode = card.SecurityCode,
                    Identity = identityId,
                    Name = name,
                    Address = finixAddress
                })
            }, cancellationToken: cancellationToken);

            if (created.TryGetPaymentInstrumentCard(out var paymentInstrument) && paymentInstrument is not null)
            {
                return paymentInstrument;
            }

            throw new PaymentOperationException(HttpStatusCode.BadGateway,
                "Finix returned a payment instrument that was not a card.");
        }
        catch (ApiException ex)
        {
            throw new PaymentOperationException(HttpStatusCode.UnprocessableEntity,
                $"Finix could not tokenize the card: {DescribeFinixError(ex)}");
        }
    }

    private async Task<FinixAuthorization> GetAuthorizationAsync(string authorizationId, CancellationToken cancellationToken) =>
        await _finix.AuthorizationsApi.GetAuthorization(new GetAuthorizationRequest
        {
            AuthorizationId = authorizationId
        }, cancellationToken: cancellationToken);

    private async Task<long> SumFeesAsync(string transferId, CancellationToken cancellationToken)
    {
        double total = 0;
        string? cursor = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var result = await _finix.FeesApi.ListFees(new ListFeesRequest
            {
                LinkedTo = transferId,
                Limit = PageSize,
                AfterCursor = cursor
            }, cancellationToken: cancellationToken);

            if (result.Embedded?.Fees is not null)
            {
                total += result.Embedded.Fees.Sum(f => f.Amount ?? 0);
            }

            cursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return (long)Math.Round(total, MidpointRounding.AwayFromZero);
    }

    private async Task<List<FinixAuthorization>> ListAllAuthorizationsAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var all = new List<FinixAuthorization>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            ListAuthorizations result;
            try
            {
                result = await _finix.AuthorizationsApi.ListAuthorizations(new ListAuthorizationsRequest
                {
                    Merchant = _options.MerchantId,
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    Limit = PageSize,
                    AfterCursor = cursor
                }, cancellationToken: cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentOperationException(HttpStatusCode.BadGateway,
                    $"Finix could not list authorizations: {DescribeFinixError(ex)}");
            }

            if (result.Embedded?.Authorizations is not null)
            {
                all.AddRange(result.Embedded.Authorizations);
            }

            cursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return all.Where(x => !x.CreatedAt.HasValue || (x.CreatedAt.Value >= from && x.CreatedAt.Value <= to)).ToList();
    }

    private async Task<List<Transfer>> ListAllTransfersAsync(DateTimeOffset from, DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var all = new List<Transfer>();
        string? cursor = null;

        for (var page = 0; page < MaxPages; page++)
        {
            ListTransfers result;
            try
            {
                result = await _finix.TransfersApi.ListTransfers(new ListTransfersRequest
                {
                    Merchant = _options.MerchantId,
                    CreatedAtGte = from,
                    CreatedAtLte = to,
                    Limit = PageSize,
                    AfterCursor = cursor
                }, cancellationToken: cancellationToken);
            }
            catch (ApiException ex)
            {
                throw new PaymentOperationException(HttpStatusCode.BadGateway,
                    $"Finix could not list transfers: {DescribeFinixError(ex)}");
            }

            if (result.Embedded?.Transfers is not null)
            {
                all.AddRange(result.Embedded.Transfers);
            }

            cursor = result.Page?.NextCursor;
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return all.Where(x => !x.CreatedAt.HasValue || (x.CreatedAt.Value >= from && x.CreatedAt.Value <= to)).ToList();
    }

    private static string DescribeFinixError(ApiException exception)
    {
        var details = new List<string>();
        try
        {
            var error = exception.GetType().GetProperty("Error")?.GetValue(exception);
            var tryGet = error?.GetType().GetMethod("TryGetGenericError");
            if (tryGet is not null)
            {
                var arguments = new object?[] { null };
                if (tryGet.Invoke(error, arguments) is true)
                {
                    if (arguments[0] is GenericError genericError && genericError.Embedded?.Errors is not null)
                    {
                        details.AddRange(genericError.Embedded.Errors
                            .Select(e => e.Message)
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .Select(m => m!));
                    }
                }
            }
        }
        catch
        {
            // Fall back to the SDK exception message if the error body cannot be inspected.
        }

        return details.Count == 0
            ? exception.Message
            : $"{exception.Message} ({string.Join("; ", details)})";
    }
    private static OrderDto ToDto(Order order, IEnumerable<OrderRefund> refunds) => new()
    {
        OrderId = order.Id,
        OrderDate = order.OrderDate,
        TotalCents = order.TotalInCents(),
        Currency = order.PaymentCurrency ?? string.Empty,
        PaymentStatus = order.PaymentStatus.ToString(),
        FulfilmentStatus = order.FulfilmentStatus.ToString(),
        FinixAuthorizationId = order.FinixAuthorizationId,
        FinixAuthorizationState = order.FinixAuthorizationState,
        FinixVoidState = order.FinixVoidState,
        FinixTransferId = order.FinixTransferId,
        FinixTransferState = order.FinixTransferState,
        AuthorizedAmountCents = order.AuthorizedAmountCents,
        CapturedAmountCents = order.CapturedAmountCents,
        FinixFeeCents = order.FinixFeeCents,
        NetAmountCents = order.NetAmountCents,
        RefundedAmountCents = order.RefundedAmountCents,
        FailureMessage = order.PaymentFailureMessage,
        SavedPaymentMethodId = order.SavedPaymentMethodId,
        PaidAt = order.PaidAt,
        FulfilledAt = order.FulfilledAt,
        CancelledAt = order.CancelledAt,
        ShipToAddress = new ShippingAddressDto
        {
            Street = order.ShipToAddress.Street,
            City = order.ShipToAddress.City,
            State = order.ShipToAddress.State,
            Country = order.ShipToAddress.Country,
            ZipCode = order.ShipToAddress.ZipCode
        },
        Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList(),
        Refunds = refunds.Select(r => new RefundDto
        {
            RefundId = r.Id,
            AmountCents = r.AmountCents,
            Status = r.Status,
            IdempotencyKey = r.IdempotencyKey,
            FinixTransferId = r.FinixTransferId,
            FinixTransferState = r.FinixTransferState,
            FailureMessage = r.FailureMessage,
            CreatedAt = r.CreatedAt
        }).ToList()
    };

    private static PaymentMethodDto ToDto(SavedPaymentMethod saved) => new()
    {
        PaymentMethodId = saved.Id,
        Brand = saved.Brand,
        Last4 = saved.LastFour,
        ExpirationMonth = saved.ExpirationMonth,
        ExpirationYear = saved.ExpirationYear,
        Name = saved.Name,
        CreatedAt = saved.CreatedAt
    };

    private static Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address MapShipToAddress(ShippingAddressDto? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Street) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Country) ||
            string.IsNullOrWhiteSpace(address.ZipCode))
        {
            return new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address("1 Main Street", "Seattle", "WA", "USA", "98101");
        }

        return new Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address(address.Street, address.City, address.State, address.Country, address.ZipCode);
    }

    private static FinixApiReference.Models.Address? MapFinixAddress(CardAddressDto? address)
    {
        if (address is null ||
            string.IsNullOrWhiteSpace(address.Line1) ||
            string.IsNullOrWhiteSpace(address.City) ||
            string.IsNullOrWhiteSpace(address.Region) ||
            string.IsNullOrWhiteSpace(address.PostalCode))
        {
            return null;
        }

        return new FinixApiReference.Models.Address
        {
            Line1 = address.Line1,
            Line2 = address.Line2,
            City = address.City,
            Region = address.Region,
            PostalCode = address.PostalCode,
            Country = ToFinixCountry(address.Country)
        };
    }

    private static string NormalizeCardNumber(string number) =>
        new((number ?? string.Empty).Where(char.IsDigit).ToArray());

    private static Currency ToFinixCurrency(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new PaymentOperationException(HttpStatusCode.InternalServerError, "Finix:Currency is not configured.");
        }

        try
        {
            return JsonSerializer.Deserialize<Currency>(JsonSerializer.Serialize(value.Trim().ToUpperInvariant()))
                   ?? throw new JsonException("No currency value.");
        }
        catch (JsonException)
        {
            throw new PaymentOperationException(HttpStatusCode.InternalServerError,
                $"Finix:Currency '{value}' is not a valid ISO 4217 currency code.");
        }
    }

    private static Country ToFinixCountry(string? value)
    {
        var code = string.IsNullOrWhiteSpace(value) ? "USA" : value.Trim().ToUpperInvariant();
        return JsonSerializer.Deserialize<Country>(JsonSerializer.Serialize(code))
               ?? throw new PaymentOperationException(HttpStatusCode.BadRequest, $"'{value}' is not a valid country code.");
    }

    private static bool TryGetOrderIdTag(object? tags, out int orderId)
    {
        orderId = 0;
        if (tags is null)
        {
            return false;
        }

        JsonElement element;
        try
        {
            element = JsonSerializer.SerializeToElement(tags);
        }
        catch (Exception)
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("orderId", out var property))
        {
            return false;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetInt32(out orderId),
            JsonValueKind.String => int.TryParse(property.GetString(), out orderId),
            _ => false
        };
    }
}
