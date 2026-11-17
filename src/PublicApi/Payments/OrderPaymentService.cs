using System;
using System.Collections.Generic;
using System.Linq;
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
using FinixApiReference.Requests.FeesApi;
using FinixApiReference.Requests.PaymentInstrumentsApi;
using FinixApiReference.Requests.TransfersApi;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderAddress = Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate.Address;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public interface IOrderPaymentService
{
    Task<ServiceResult> CreateOrderAsync(string? buyerId, CreateOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<ServiceResult> PayAsync(string? buyerId, int orderId, PayOrderRequest request, CancellationToken cancellationToken);
    Task<ServiceResult> FulfilAsync(int orderId, CancellationToken cancellationToken);
    Task<ServiceResult> CancelAsync(int orderId, CancellationToken cancellationToken);
    Task<ServiceResult> RefundAsync(string? buyerId, int orderId, RefundOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<ServiceResult> GetMyOrdersAsync(string? buyerId, CancellationToken cancellationToken);
    Task<ServiceResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
    Task<ServiceResult> SavePaymentMethodAsync(string? buyerId, SavePaymentMethodRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<ServiceResult> ListPaymentMethodsAsync(string? buyerId, CancellationToken cancellationToken);
    Task<ServiceResult> DeletePaymentMethodAsync(string? buyerId, string paymentMethodId, CancellationToken cancellationToken);
}

public sealed class OrderPaymentService : IOrderPaymentService
{
    private static readonly TimeSpan PayBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CaptureBudget = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CancelBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan RefundBudget = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReconciliationBudget = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CardBudget = TimeSpan.FromSeconds(20);

    private const int MaxPages = 1000;

    private readonly FinixApiReferenceClient _client;
    private readonly FinixOptions _options;
    private readonly FinixMerchantContext _merchantContext;
    private readonly PaymentStore _store;
    private readonly IReadRepository<CatalogItem> _catalog;
    private readonly ILogger<OrderPaymentService> _logger;

    public OrderPaymentService(
        FinixApiReferenceClient client,
        IOptions<FinixOptions> options,
        FinixMerchantContext merchantContext,
        PaymentStore store,
        IReadRepository<CatalogItem> catalog,
        ILogger<OrderPaymentService> logger)
    {
        _client = client;
        _options = options.Value;
        _merchantContext = merchantContext;
        _store = store;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ServiceResult> CreateOrderAsync(
        string? buyerId,
        CreateOrderRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller.");
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return ServiceResult.BadRequest("ORDER_ITEMS_REQUIRED", "At least one catalog item is required.");
        }

        var claimKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? null
            : $"order:{buyerId}:{idempotencyKey.Trim()}";

        if (claimKey is not null)
        {
            var existing = _store.BeginClaim(claimKey);
            if (existing is not null)
            {
                return existing.Completed
                    ? new ServiceResult(existing.StatusCode, existing.Body)
                    : ServiceResult.Conflict("ORDER_IN_PROGRESS", "An identical order request is already in progress.");
            }
        }

        try
        {
            var items = new List<OrderItem>();
            foreach (var line in request.Items)
            {
                if (line.Quantity <= 0)
                {
                    Release(claimKey);
                    return ServiceResult.BadRequest("INVALID_QUANTITY", $"Quantity for catalog item {line.CatalogItemId} must be greater than zero.");
                }

                var catalogItem = await _catalog.GetByIdAsync(line.CatalogItemId, cancellationToken);
                if (catalogItem is null)
                {
                    Release(claimKey);
                    return ServiceResult.BadRequest("CATALOG_ITEM_NOT_FOUND", $"Catalog item {line.CatalogItemId} does not exist.");
                }

                var picture = string.IsNullOrWhiteSpace(catalogItem.PictureUri)
                    ? "images/products/placeholder.png"
                    : catalogItem.PictureUri;

                items.Add(new OrderItem(
                    new CatalogItemOrdered(catalogItem.Id, catalogItem.Name, picture),
                    catalogItem.Price,
                    line.Quantity));
            }

            var address = BuildAddress(request.ShipToAddress);
            var order = new Order(buyerId, address, items);
            var totalAmount = order.Total();
            var totalCents = ToCents(totalAmount);
            if (totalCents <= 0)
            {
                Release(claimKey);
                return ServiceResult.BadRequest("INVALID_ORDER_TOTAL", "The order total must be greater than zero.");
            }

            var record = new OrderPaymentRecord
            {
                OrderId = _store.NextOrderId(),
                BuyerId = buyerId,
                Order = order,
                PaymentReference = Guid.NewGuid().ToString("N"),
                TotalAmount = totalAmount,
                TotalAmountCents = totalCents,
                Currency = _options.CurrencyCode,
                Status = OrderPaymentStatus.AwaitingPayment
            };

            _store.AddOrder(record);

            var response = new CreateOrderResponse
            {
                OrderId = record.OrderId,
                Status = StatusText(record.Status),
                TotalAmount = totalCents,
                Currency = record.Currency,
                Items = order.OrderItems.Select(item => new OrderItemResponse
                {
                    CatalogItemId = item.ItemOrdered.CatalogItemId,
                    ProductName = item.ItemOrdered.ProductName,
                    UnitPrice = item.UnitPrice,
                    Quantity = item.Units
                }).ToList()
            };

            _logger.LogInformation(
                "Order {OrderId} created for {BuyerId}: {TotalCents} {Currency} (minor units).",
                record.OrderId, buyerId, totalCents, record.Currency);

            Complete(claimKey, 201, response);
            return ServiceResult.Created(response);
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    public async Task<ServiceResult> PayAsync(
        string? buyerId,
        int orderId,
        PayOrderRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller.");
        }

        var order = _store.GetOrderForBuyer(orderId, buyerId);
        if (order is null)
        {
            return ServiceResult.NotFound("ORDER_NOT_FOUND", "No order with that id belongs to the caller.");
        }

        if (order.Status == OrderPaymentStatus.Authorized)
        {
            return ServiceResult.Ok(PayResponse(order));
        }

        if (order.Status == OrderPaymentStatus.Fulfilled)
        {
            return ServiceResult.Conflict("ORDER_ALREADY_FULFILLED", "This order has already been fulfilled and captured.");
        }

        if (order.Status == OrderPaymentStatus.Canceled)
        {
            return ServiceResult.Conflict("ORDER_CANCELED", "This order was canceled; place a new order to pay.");
        }

        if (order.Status == OrderPaymentStatus.PaymentUnknown)
        {
            var settled = await TryFindAuthorizationAsync(AuthorizationIdempotencyId(order), cancellationToken);
            if (settled is not null)
            {
                ApplyAuthorization(order, settled, order.PaymentMethodId);
                return ServiceResult.Ok(PayResponse(order));
            }
        }

        var hasCard = request.Card is not null;
        var hasSavedCard = !string.IsNullOrWhiteSpace(request.PaymentMethodId);
        if (hasCard == hasSavedCard)
        {
            return ServiceResult.BadRequest(
                "PAYMENT_SOURCE_REQUIRED",
                "Provide either card details for a one-off payment or paymentMethodId for a saved card.");
        }

        var claimKey = $"authorize:{orderId}";
        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("PAYMENT_IN_PROGRESS", "A payment for this order is already in progress.");
        }

        try
        {
            string source;
            string? paymentMethodId = null;

            if (hasSavedCard)
            {
                var card = _store.GetCardForBuyer(request.PaymentMethodId!, buyerId);
                if (card is null)
                {
                    Release(claimKey);
                    return ServiceResult.NotFound("PAYMENT_METHOD_NOT_FOUND", "No saved card with that id belongs to the caller.");
                }

                if (!card.Enabled)
                {
                    Release(claimKey);
                    return ServiceResult.Conflict("PAYMENT_METHOD_DISABLED", "That saved card has been removed and cannot be used.");
                }

                source = card.FinixPaymentInstrumentId;
                paymentMethodId = card.PaymentMethodId;
            }
            else
            {
                var validation = ValidateCard(request.Card!, out var normalizedNumber);
                if (validation is not null)
                {
                    Release(claimKey);
                    return validation;
                }

                var instrument = await CreatePaymentInstrumentAsync(request.Card!, normalizedNumber, cancellationToken);
                source = instrument.Id!;
            }

            await _merchantContext.EnsureCurrencySupportedAsync(cancellationToken);

            var idempotencyId = AuthorizationIdempotencyId(order);
            var body = AuthorizationsRequest.AuthorizationAllFields(new AuthorizationAllFields
            {
                Amount = order.TotalAmountCents,
                Currency = _options.ResolveCurrency(),
                Merchant = _options.MerchantId,
                Source = source,
                IdempotencyId = idempotencyId,
                SecurityCode = hasCard ? request.Card!.SecurityCode : null,
                Tags = new Dictionary<string, string>
                {
                    ["eshop_order_id"] = orderId.ToString()
                }
            });

            var authorization = await BoundedAsync(
                PayBudget,
                ct => _client.AuthorizationsApi.CreateAuthorization(
                    new CreateAuthorizationRequest { Body = body },
                    cancellationToken: ct),
                cancellationToken);

            if (string.IsNullOrWhiteSpace(authorization.Id))
            {
                Release(claimKey);
                return ServiceResult.Failure(502, "FINIX_EMPTY_AUTHORIZATION", "Finix did not return an authorization id.");
            }

            ApplyAuthorization(order, authorization, paymentMethodId);
            var response = PayResponse(order);

            if (order.Status == OrderPaymentStatus.PaymentFailed)
            {
                Release(claimKey);
                return ServiceResult.Failure(402, order.FailureCode ?? "AUTHORIZATION_FAILED", order.FailureMessage);
            }

            _logger.LogInformation(
                "Order {OrderId} authorized as {AuthorizationId} for {Amount} {Currency}.",
                orderId, order.AuthorizationId, order.HeldAmountCents, order.Currency);

            Complete(claimKey, 200, response);
            return ServiceResult.Ok(response);
        }
        catch (ApiException<CreateAuthorizationError> ex)
        {
            var failure = ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix rejected the authorization.")
                : FromApiException(ex, "Finix rejected the authorization.");

            MarkPaymentFailed(order, failure);
            Release(claimKey);
            return failure;
        }
        catch (ApiException ex)
        {
            var failure = FromApiException(ex, "Finix rejected the authorization.");
            MarkPaymentFailed(order, failure);
            Release(claimKey);
            return failure;
        }
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownAuthorizationAsync(order, claimKey, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownAuthorizationAsync(order, claimKey, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable for order {OrderId}.", orderId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the payment budget.");
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    public async Task<ServiceResult> FulfilAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = _store.GetOrder(orderId);
        if (order is null)
        {
            return ServiceResult.NotFound("ORDER_NOT_FOUND", "No order with that id exists.");
        }

        if (order.Status == OrderPaymentStatus.Fulfilled)
        {
            return ServiceResult.Ok(FulfilResponse(order));
        }

        if (order.Status == OrderPaymentStatus.Canceled)
        {
            return ServiceResult.Conflict("ORDER_CANCELED", "A canceled order has no held funds to capture.");
        }

        if (order.Status != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            return ServiceResult.Conflict(
                "ORDER_NOT_AUTHORIZED",
                "The order has no successful authorization to capture. Ask the shopper to pay first.");
        }

        var claimKey = $"capture:{orderId}";
        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("CAPTURE_IN_PROGRESS", "A capture for this order is already in progress.");
        }

        try
        {
            var authorization = await BoundedAsync(
                CaptureBudget,
                ct => _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = order.AuthorizationId! },
                    cancellationToken: ct),
                cancellationToken);

            var holdProblem = ExplainUncapturableHold(authorization, order);
            if (holdProblem is not null)
            {
                UpdateHold(order, authorization);
                Release(claimKey);
                return ServiceResult.Conflict("AUTHORIZATION_NOT_CAPTURABLE", holdProblem);
            }

            string transferId;
            string? captureState;
            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                transferId = authorization.Transfer!;
                captureState = authorization.State?.Value;
            }
            else
            {
                var captured = await BoundedAsync(
                    CaptureBudget,
                    ct => _client.AuthorizationsApi.CaptureAuthorization(
                        new CaptureAuthorizationRequest
                        {
                            AuthorizationId = order.AuthorizationId!,
                            Body = AuthorizationsRequest1.CaptureAuthorization(new CaptureAuthorization
                            {
                                CaptureAmount = order.TotalAmountCents
                            })
                        },
                        cancellationToken: ct),
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(captured.Transfer))
                {
                    UpdateHold(order, captured);
                    Release(claimKey);
                    return ServiceResult.Failure(
                        502,
                        "CAPTURE_NOT_SETTLED",
                        "Finix accepted the capture but did not return a capture transfer yet; retry fulfilment.");
                }

                transferId = captured.Transfer!;
                captureState = captured.State?.Value;
            }

            var response = await PersistCaptureAsync(order, transferId, captureState, cancellationToken);
            Complete(claimKey, 200, response);
            return ServiceResult.Ok(response);
        }
        catch (ApiException<CaptureAuthorizationError> ex)
        {
            var failure = ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix could not capture the authorization.")
                : FromApiException(ex, "Finix could not capture the authorization.");

            var recovered = await TryRecoverCaptureAsync(order, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 200, recovered);
                return ServiceResult.Ok(recovered);
            }

            var providerDetail = (failure.Body as ApiErrorBody)?.Explanation;
            Release(claimKey);
            return ServiceResult.Failure(
                failure.StatusCode,
                "CAPTURE_REJECTED",
                "The authorization can no longer be captured. The shopper must pay again; " +
                "cancel this order or ask them to place a new payment. Provider detail: " + providerDetail);
        }
        catch (ApiException ex)
        {
            var recovered = await TryRecoverCaptureAsync(order, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 200, recovered);
                return ServiceResult.Ok(recovered);
            }

            var failure = FromApiException(ex, "Finix could not capture the authorization.");
            Release(claimKey);
            return ServiceResult.Failure(failure.StatusCode, "CAPTURE_REJECTED", "Finix could not capture the authorization; the shopper may need to pay again.");
        }
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownCaptureAsync(order, claimKey, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownCaptureAsync(order, claimKey, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable while fulfilling order {OrderId}.", orderId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the capture budget.");
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    public async Task<ServiceResult> CancelAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = _store.GetOrder(orderId);
        if (order is null)
        {
            return ServiceResult.NotFound("ORDER_NOT_FOUND", "No order with that id exists.");
        }

        if (order.Status == OrderPaymentStatus.Canceled)
        {
            return ServiceResult.Ok(CancelResponse(order));
        }

        if (order.Status == OrderPaymentStatus.Fulfilled)
        {
            return ServiceResult.Conflict(
                "ORDER_ALREADY_FULFILLED",
                "The order has already been fulfilled and captured; use the refunds endpoint instead.");
        }

        if (order.Status != OrderPaymentStatus.Authorized || string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            return ServiceResult.Conflict("ORDER_NOT_AUTHORIZED", "There are no held funds to release.");
        }

        var claimKey = $"void:{orderId}";
        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("CANCEL_IN_PROGRESS", "A cancellation for this order is already in progress.");
        }

        try
        {
            var authorization = await BoundedAsync(
                CancelBudget,
                ct => _client.AuthorizationsApi.VoidAuthorization(
                    new VoidAuthorizationRequest
                    {
                        AuthorizationIdVoidTo = order.AuthorizationId!,
                        Body = new VoidAnAuthorization { VoidMe = true }
                    },
                    cancellationToken: ct),
                cancellationToken);

            ApplyVoid(order, authorization);
            var response = CancelResponse(order);
            _logger.LogInformation(
                "Order {OrderId} authorization {AuthorizationId} released.",
                orderId, order.AuthorizationId);
            Complete(claimKey, 200, response);
            return ServiceResult.Ok(response);
        }
        catch (ApiException<VoidAuthorizationError> ex)
        {
            var recovered = await TryRecoverVoidAsync(order, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 200, recovered);
                return ServiceResult.Ok(recovered);
            }

            var failure = ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix could not release the authorization.")
                : FromApiException(ex, "Finix could not release the authorization.");
            Release(claimKey);
            return failure;
        }
        catch (ApiException ex)
        {
            var recovered = await TryRecoverVoidAsync(order, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 200, recovered);
                return ServiceResult.Ok(recovered);
            }

            Release(claimKey);
            return FromApiException(ex, "Finix could not release the authorization.");
        }
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownVoidAsync(order, claimKey, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownVoidAsync(order, claimKey, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable while canceling order {OrderId}.", orderId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the cancellation budget.");
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    public async Task<ServiceResult> RefundAsync(
        string? buyerId,
        int orderId,
        RefundOrderRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller.");
        }

        var order = _store.GetOrderForBuyer(orderId, buyerId);
        if (order is null)
        {
            return ServiceResult.NotFound("ORDER_NOT_FOUND", "No order with that id belongs to the caller.");
        }

        if (order.Status != OrderPaymentStatus.Fulfilled
            || string.IsNullOrWhiteSpace(order.CaptureTransferId)
            || order.CapturedAmountCents is null)
        {
            return ServiceResult.Conflict("ORDER_NOT_CAPTURED", "Only a fulfilled, captured order can be refunded.");
        }

        var key = request.IdempotencyKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = idempotencyKey;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return ServiceResult.BadRequest("IDEMPOTENCY_KEY_REQUIRED", "A refund idempotency key is required.");
        }

        key = key.Trim();
        var claimKey = $"refund:{orderId}:{key}";
        var existingRefund = FindRefundByKey(order, key);
        if (existingRefund is not null)
        {
            return ServiceResult.Ok(RefundResponse(order, existingRefund));
        }

        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("REFUND_IN_PROGRESS", "A refund with this idempotency key is already in progress.");
        }

        long amount;
        RefundRecord refund;
        try
        {
            amount = request.Amount ?? (order.CapturedAmountCents.Value - order.RefundedAmountCents);
            if (amount <= 0)
            {
                Release(claimKey);
                return ServiceResult.BadRequest("INVALID_REFUND_AMOUNT", "The refund amount must be greater than zero.");
            }

            if (amount > int.MaxValue)
            {
                Release(claimKey);
                return ServiceResult.BadRequest("INVALID_REFUND_AMOUNT", "The refund amount is too large for Finix.");
            }

            var remaining = order.CapturedAmountCents.Value - order.RefundedAmountCents;
            if (amount > remaining)
            {
                Release(claimKey);
                return ServiceResult.Conflict(
                    "REFUND_EXCEEDS_CAPTURE",
                    $"The requested refund exceeds the unrefunded capture. Remaining refundable: {remaining} (minor units).");
            }

            refund = _store.WithOrder(orderId, o =>
            {
                var record = new RefundRecord
                {
                    RefundId = "rf_" + Guid.NewGuid().ToString("N"),
                    IdempotencyKey = key,
                    AmountCents = amount,
                    Status = "PENDING"
                };
                o.Refunds.Add(record);
                o.RefundedAmountCents += amount;
                o.UpdatedAt = DateTimeOffset.UtcNow;
                return record;
            });
        }
        catch
        {
            Release(claimKey);
            throw;
        }

        var beforeIds = await TryListReversalIdsAsync(order.CaptureTransferId!, cancellationToken);

        var captureState = await EnsureCaptureSettledAsync(order.CaptureTransferId!, cancellationToken);
        if (captureState != State11.Succeeded
            && amount < order.CapturedAmountCents.Value)
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            Release(claimKey);
            return ServiceResult.Conflict(
                "CAPTURE_NOT_SETTLED",
                $"The capture transfer is still {captureState?.Value ?? "PENDING"}. Finix allows a full refund now, " +
                "but partial refunds become available after the capture settles; retry this partial refund shortly.");
        }

        try
        {
            var reversal = await BoundedAsync(
                RefundBudget,
                ct => _client.TransfersApi.CreateTransferReversal(
                    new CreateTransferReversalRequest
                    {
                        TransferId = order.CaptureTransferId!,
                        Body = TransfersReversalsRequest.RefundTransfer(new RefundTransfer
                        {
                            RefundAmount = (int)amount
                        })
                    },
                    cancellationToken: ct),
                cancellationToken);

            var state = reversal.State?.Value ?? "PENDING";
            _store.WithOrder(orderId, o =>
            {
                var record = o.Refunds.First(r => r.RefundId == refund.RefundId);
                record.FinixRefundId = reversal.Id;
                record.Status = state;
                record.FailureCode = reversal.FailureCode;
                record.FailureMessage = reversal.FailureMessage;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                if (string.Equals(state, State11.Failed.Value, StringComparison.Ordinal))
                {
                    o.RefundedAmountCents -= record.AmountCents;
                }
                o.UpdatedAt = DateTimeOffset.UtcNow;
                return record;
            });

            if (string.Equals(state, State11.Failed.Value, StringComparison.Ordinal))
            {
                Release(claimKey);
                return ServiceResult.Failure(402, reversal.FailureCode ?? "REFUND_FAILED", reversal.FailureMessage);
            }

            var response = RefundResponse(order, refund);
            _logger.LogInformation(
                "Order {OrderId} refunded {Amount} {Currency} as {RefundId} ({State}).",
                orderId, amount, order.Currency, response.RefundId, state);
            Complete(claimKey, 200, response);
            return ServiceResult.Ok(response);
        }
        catch (ApiException<CreateTransferReversalError> ex)
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            Release(claimKey);
            if (ex.Error.TryGetGenericError(out var generic))
            {
                return FromGenericError((int)ex.StatusCode, generic, "Finix rejected the refund.");
            }

            if (ex.Error.TryGetRawError(out var raw))
            {
                _logger.LogWarning("Finix refund refusal for order {OrderId}: HTTP {Status} {Body}", orderId, (int)ex.StatusCode, raw.ReadAsString());
                return ServiceResult.Failure(MapStatus((int)ex.StatusCode), $"FINIX_HTTP_{(int)ex.StatusCode}", raw.ReadAsString());
            }

            return FromApiException(ex, "Finix rejected the refund.");
        }
        catch (ApiException ex)
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            Release(claimKey);
            return FromApiException(ex, "Finix rejected the refund.");
        }
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownRefundAsync(order, refund, claimKey, beforeIds, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownRefundAsync(order, refund, claimKey, beforeIds, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            _logger.LogError(ex, "Finix credentials are not usable while refunding order {OrderId}.", orderId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the refund budget.");
        }
        catch
        {
            RemoveFailedRefund(orderId, refund.RefundId);
            Release(claimKey);
            throw;
        }
    }

    public Task<ServiceResult> GetMyOrdersAsync(string? buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Task.FromResult(ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller."));
        }

        var response = new MyOrdersResponse
        {
            Orders = _store.GetOrdersForBuyer(buyerId)
                .Select(ToMyOrderResponse)
                .ToList()
        };

        return Task.FromResult(ServiceResult.Ok(response));
    }

    public async Task<ServiceResult> ReconcileAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return ServiceResult.BadRequest("INVALID_DATE_RANGE", "'from' must be earlier than or equal to 'to'.");
        }

        try
        {
            return await BoundedAsync(
                ReconciliationBudget,
                async ct =>
                {
                    var transfers = new List<Transfer>();
                    string? cursor = null;
                    var pages = 0;
                    var truncated = false;

                    do
                    {
                        var page = await _client.TransfersApi.ListTransfers(
                            new ListTransfersRequest
                            {
                                Merchant = _options.MerchantId,
                                Limit = 100,
                                AfterCursor = cursor
                            },
                            cancellationToken: ct);

                        var items = page.Embedded?.Transfers;
                        if (items is null || items.Count == 0)
                        {
                            break;
                        }

                        transfers.AddRange(items);
                        var nextCursor = page.Page?.NextCursor;

                        if (items.All(t => t.CreatedAt.HasValue && t.CreatedAt.Value < from))
                        {
                            break;
                        }

                        if (string.Equals(nextCursor, cursor, StringComparison.Ordinal))
                        {
                            truncated = !string.IsNullOrWhiteSpace(nextCursor);
                            cursor = nextCursor;
                            break;
                        }

                        cursor = nextCursor;
                        pages++;

                        if (pages >= MaxPages && !string.IsNullOrWhiteSpace(cursor))
                        {
                            truncated = true;
                            break;
                        }
                    }
                    while (!string.IsNullOrWhiteSpace(cursor));

                    return ServiceResult.Ok(BuildReconciliation(from, to, transfers, truncated, cursor));
                },
                cancellationToken);
        }
        catch (ApiException<ListTransfersError> ex)
        {
            return ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix could not list transfers for reconciliation.")
                : FromApiException(ex, "Finix could not list transfers for reconciliation.");
        }
        catch (ApiException ex)
        {
            return FromApiException(ex, "Finix could not list transfers for reconciliation.");
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable during reconciliation.");
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (SdkConnectionException ex)
        {
            _logger.LogError(ex, "Finix was unreachable during reconciliation.");
            return ServiceResult.Failure(502, "FINIX_UNAVAILABLE", "Finix could not be reached during reconciliation.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not finish the reconciliation walk within the budget.");
        }
    }

    public async Task<ServiceResult> SavePaymentMethodAsync(
        string? buyerId,
        SavePaymentMethodRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller.");
        }

        var validation = ValidateCard(request, out var normalizedNumber);
        if (validation is not null)
        {
            return validation;
        }

        var last4 = normalizedNumber[^4..];
        var name = request.Name!.Trim();

        var existingCard = _store.FindCardForBuyer(
            buyerId, last4, request.ExpirationMonth, request.ExpirationYear, name);
        if (existingCard is not null)
        {
            return ServiceResult.Ok(CardResponse(existingCard));
        }

        var claimKey = string.IsNullOrWhiteSpace(idempotencyKey)
            ? $"cardsave:{buyerId}:{last4}:{request.ExpirationMonth}:{request.ExpirationYear}:{name.ToLowerInvariant()}"
            : $"cardsave:{buyerId}:{idempotencyKey.Trim()}";

        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("CARD_SAVE_IN_PROGRESS", "An identical card save is already in progress.");
        }

        try
        {
            var instrument = await CreatePaymentInstrumentAsync(request, normalizedNumber, cancellationToken);
            var card = new SavedCardRecord
            {
                PaymentMethodId = "pm_" + Guid.NewGuid().ToString("N"),
                BuyerId = buyerId,
                FinixPaymentInstrumentId = instrument.Id!,
                Brand = instrument.Brand?.Value,
                Last4 = instrument.LastFour ?? last4,
                ExpirationMonth = instrument.ExpirationMonth ?? request.ExpirationMonth,
                ExpirationYear = instrument.ExpirationYear ?? request.ExpirationYear,
                Name = instrument.Name ?? name
            };

            _store.AddCard(card);
            var response = CardResponse(card);
            _logger.LogInformation(
                "Saved card {PaymentMethodId} ({Brand} {Last4}) for {BuyerId}.",
                card.PaymentMethodId, card.Brand, card.Last4, buyerId);
            Complete(claimKey, 201, response);
            return ServiceResult.Created(response);
        }
        catch (ApiException<CreatePaymentInstrumentError> ex)
        {
            var recovered = await TryRecoverSavedCardAsync(buyerId, request, last4, name, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 201, recovered);
                return ServiceResult.Created(recovered);
            }

            Release(claimKey);
            return ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix rejected the card.")
                : FromApiException(ex, "Finix rejected the card.");
        }
        catch (ApiException ex)
        {
            var recovered = await TryRecoverSavedCardAsync(buyerId, request, last4, name, cancellationToken);
            if (recovered is not null)
            {
                Complete(claimKey, 201, recovered);
                return ServiceResult.Created(recovered);
            }

            Release(claimKey);
            return FromApiException(ex, "Finix rejected the card.");
        }
        catch (SdkTimeoutException ex)
        {
            return await SettleUnknownCardAsync(buyerId, request, last4, name, claimKey, ex, cancellationToken);
        }
        catch (SdkConnectionException ex)
        {
            return await SettleUnknownCardAsync(buyerId, request, last4, name, claimKey, ex, cancellationToken);
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable while saving a card for {BuyerId}.", buyerId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the card-save budget.");
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    public Task<ServiceResult> ListPaymentMethodsAsync(string? buyerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Task.FromResult(ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller."));
        }

        var response = new ListPaymentMethodsResponse
        {
            PaymentMethods = _store.GetCardsForBuyer(buyerId)
                .Select(CardResponse)
                .ToList()
        };

        return Task.FromResult(ServiceResult.Ok(response));
    }

    public async Task<ServiceResult> DeletePaymentMethodAsync(
        string? buyerId,
        string paymentMethodId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return ServiceResult.Failure(401, "UNAUTHENTICATED", "The bearer token does not identify a caller.");
        }

        var card = _store.GetCardForBuyer(paymentMethodId, buyerId);
        if (card is null)
        {
            return ServiceResult.NotFound("PAYMENT_METHOD_NOT_FOUND", "No saved card with that id belongs to the caller.");
        }

        if (!card.Enabled)
        {
            return ServiceResult.NoContent();
        }

        var claimKey = $"disable:{paymentMethodId}";
        var existingClaim = _store.BeginClaim(claimKey);
        if (existingClaim is not null)
        {
            return existingClaim.Completed
                ? new ServiceResult(existingClaim.StatusCode, existingClaim.Body)
                : ServiceResult.Conflict("CARD_DELETE_IN_PROGRESS", "This card is already being removed.");
        }

        try
        {
            await BoundedAsync(
                CardBudget,
                ct => _client.PaymentInstrumentsApi.UpdatePaymentInstrument(
                    new UpdatePaymentInstrumentRequest
                    {
                        PaymentInstrumentId = card.FinixPaymentInstrumentId,
                        Body = PaymentInstrumentsRequest1.PaymentInstrumentDisableAPaymentInstrument(
                            new PaymentInstrumentDisableAPaymentInstrument { Enabled = false })
                    },
                    cancellationToken: ct),
                cancellationToken);

            MarkCardDisabled(paymentMethodId);
            Complete(claimKey, 204, null);
            return ServiceResult.NoContent();
        }
        catch (ApiException<UpdatePaymentInstrumentError> ex)
        {
            if (await IsCardDisabledAtFinixAsync(card.FinixPaymentInstrumentId, cancellationToken))
            {
                MarkCardDisabled(paymentMethodId);
                Complete(claimKey, 204, null);
                return ServiceResult.NoContent();
            }

            Release(claimKey);
            return ex.Error.TryGetGenericError(out var generic)
                ? FromGenericError((int)ex.StatusCode, generic, "Finix could not disable the card.")
                : FromApiException(ex, "Finix could not disable the card.");
        }
        catch (ApiException ex)
        {
            if (await IsCardDisabledAtFinixAsync(card.FinixPaymentInstrumentId, cancellationToken))
            {
                MarkCardDisabled(paymentMethodId);
                Complete(claimKey, 204, null);
                return ServiceResult.NoContent();
            }

            Release(claimKey);
            return FromApiException(ex, "Finix could not disable the card.");
        }
        catch (SdkConnectionException ex)
        {
            if (await IsCardDisabledAtFinixAsync(card.FinixPaymentInstrumentId, cancellationToken))
            {
                MarkCardDisabled(paymentMethodId);
                Complete(claimKey, 204, null);
                return ServiceResult.NoContent();
            }

            _logger.LogError(ex, "Unknown outcome while disabling card {PaymentMethodId}.", paymentMethodId);
            Release(claimKey);
            return ServiceResult.Failure(502, "CARD_DELETE_UNKNOWN", "Finix did not confirm the card removal; retry the delete.");
        }
        catch (AuthSchemeException ex)
        {
            _logger.LogError(ex, "Finix credentials are not usable while deleting card {PaymentMethodId}.", paymentMethodId);
            Release(claimKey);
            return ServiceResult.Failure(502, "FINIX_AUTHENTICATION_FAILED", "The Finix credentials are not usable.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Release(claimKey);
            return ServiceResult.Failure(504, "FINIX_TIMEOUT", "Finix did not answer within the card-delete budget.");
        }
        catch
        {
            Release(claimKey);
            throw;
        }
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(
        CardDetailsRequest card,
        string normalizedNumber,
        CancellationToken cancellationToken)
    {
        var identity = await _merchantContext.GetIdentityAsync(cancellationToken);
        var body = PaymentInstrumentsRequest.PaymentInstrumentCard1(new PaymentInstrumentCard1
        {
            ExpirationMonth = card.ExpirationMonth,
            ExpirationYear = card.ExpirationYear,
            Identity = identity,
            Name = card.Name!.Trim(),
            Number = normalizedNumber,
            SecurityCode = string.IsNullOrWhiteSpace(card.SecurityCode) ? null : card.SecurityCode
        });

        var response = await BoundedAsync(
            CardBudget,
            ct => _client.PaymentInstrumentsApi.CreatePaymentInstrument(
                new CreatePaymentInstrumentRequest { Body = body },
                cancellationToken: ct),
            cancellationToken);

        if (!response.TryGetPaymentInstrumentCard(out var instrument)
            || string.IsNullOrWhiteSpace(instrument.Id))
        {
            throw new InvalidOperationException("Finix did not return a card payment instrument.");
        }

        return instrument;
    }

    private async Task<PaymentInstrumentCard> CreatePaymentInstrumentAsync(
        SavePaymentMethodRequest card,
        string normalizedNumber,
        CancellationToken cancellationToken)
    {
        return await CreatePaymentInstrumentAsync(
            new CardDetailsRequest
            {
                Number = normalizedNumber,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Name = card.Name
            },
            normalizedNumber,
            cancellationToken);
    }

    private async Task<FulfilOrderResponse> PersistCaptureAsync(
        OrderPaymentRecord order,
        string transferId,
        string? captureState,
        CancellationToken cancellationToken)
    {
        var transfer = await BoundedAsync(
            CaptureBudget,
            ct => _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: ct),
            cancellationToken);

        var settleDeadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (transfer.State != State11.Succeeded
            && transfer.State != State11.Failed
            && transfer.State != State11.Canceled
            && DateTimeOffset.UtcNow < settleDeadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await BoundedAsync(
                CaptureBudget,
                ct => _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: ct),
                cancellationToken);
        }

        decimal? fee = await TryGetFeeAsync(transferId, cancellationToken);
        if (!fee.HasValue && transfer.Fee.HasValue)
        {
            fee = transfer.Fee.Value;
        }

        var captured = transfer.Amount ?? order.TotalAmountCents;
        decimal? net = fee.HasValue ? captured - fee.Value : null;

        _store.WithOrder(order.OrderId, o =>
        {
            o.Status = OrderPaymentStatus.Fulfilled;
            o.CaptureTransferId = transferId;
            o.CaptureStatus = transfer.State?.Value ?? captureState;
            o.CapturedAmountCents = captured;
            o.FinixFeeCents = fee;
            o.NetProceedsCents = net;
            o.FeesFinalized = fee.HasValue;
            o.FailureCode = transfer.FailureCode;
            o.FailureMessage = transfer.FailureMessage;
            o.UpdatedAt = DateTimeOffset.UtcNow;
            return o;
        });

        var response = FulfilResponse(order);
        if (!fee.HasValue)
        {
            response.Explanation = "Finix has not posted a fee for this capture yet; finixFee and netProceeds will appear on a later read.";
        }
        else if (transfer.State != State11.Succeeded)
        {
            response.Explanation =
                $"Finix accepted the capture but the capture transfer is still {transfer.State?.Value}; " +
                "partial refunds become available once Finix settles it. A full refund can be requested now.";
        }

        _logger.LogInformation(
            "Order {OrderId} captured as {TransferId}: captured {Captured}, fee {Fee}, net {Net} {Currency}.",
            order.OrderId, transferId, captured, fee, net, order.Currency);

        return response;
    }

    private async Task<decimal?> TryGetFeeAsync(string transferId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                var fees = new List<Fee>();
                string? cursor = null;
                var pages = 0;
                do
                {
                    var page = await _client.FeesApi.ListFees(
                        new ListFeesRequest
                        {
                            LinkedTo = transferId,
                            MerchantId = _options.MerchantId,
                            Limit = 100,
                            AfterCursor = cursor
                        },
                        cancellationToken: cancellationToken);

                    if (page.Embedded?.Fees is { Count: > 0 } items)
                    {
                        fees.AddRange(items);
                    }

                    cursor = page.Page?.NextCursor;
                    pages++;
                }
                while (!string.IsNullOrWhiteSpace(cursor) && pages < 10);

                if (fees.Count > 0)
                {
                    return fees.Sum(f => (decimal)(f.Amount ?? 0));
                }
            }
            catch (ApiException)
            {
                // A fee lookup is a reporting step; retry briefly, then fall back to the transfer's fee.
            }
            catch (SdkException)
            {
                // A fee lookup is a reporting step; retry briefly, then fall back to the transfer's fee.
            }

            if (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400), cancellationToken);
            }
        }

        return null;
    }

    private async Task<State11?> EnsureCaptureSettledAsync(string transferId, CancellationToken cancellationToken)
    {
        var transfer = await BoundedAsync(
            CaptureBudget,
            ct => _client.TransfersApi.GetTransfer(
                new GetTransferRequest { TransferId = transferId },
                cancellationToken: ct),
            cancellationToken);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (transfer.State != State11.Succeeded
            && transfer.State != State11.Failed
            && transfer.State != State11.Canceled
            && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            transfer = await BoundedAsync(
                CaptureBudget,
                ct => _client.TransfersApi.GetTransfer(
                    new GetTransferRequest { TransferId = transferId },
                    cancellationToken: ct),
                cancellationToken);
        }

        return transfer.State;
    }

    private async Task<Authorization?> TryFindAuthorizationAsync(string idempotencyId, CancellationToken cancellationToken)
    {
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await BoundedAsync(
                PayBudget,
                ct => _client.AuthorizationsApi.ListAuthorizations(
                    new ListAuthorizationsRequest
                    {
                        IdempotencyId = idempotencyId,
                        Merchant = _options.MerchantId,
                        Limit = 100,
                        AfterCursor = cursor
                    },
                    cancellationToken: ct),
                cancellationToken);

            if (page.Embedded?.Authorizations is { Count: > 0 } authorizations)
            {
                var match = authorizations.FirstOrDefault(a =>
                    string.Equals(a.IdempotencyId, idempotencyId, StringComparison.Ordinal));
                if (match is not null)
                {
                    return match;
                }
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && pages < 20);

        return null;
    }

    private async Task<FulfilOrderResponse?> TryRecoverCaptureAsync(OrderPaymentRecord order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            return null;
        }

        try
        {
            var authorization = await BoundedAsync(
                CaptureBudget,
                ct => _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = order.AuthorizationId! },
                    cancellationToken: ct),
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(authorization.Transfer))
            {
                return await PersistCaptureAsync(order, authorization.Transfer!, authorization.State?.Value, cancellationToken);
            }
        }
        catch (SdkException)
        {
        }

        return null;
    }

    private async Task<CancelOrderResponse?> TryRecoverVoidAsync(OrderPaymentRecord order, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(order.AuthorizationId))
        {
            return null;
        }

        try
        {
            var authorization = await BoundedAsync(
                CancelBudget,
                ct => _client.AuthorizationsApi.GetAuthorization(
                    new GetAuthorizationRequest { AuthorizationId = order.AuthorizationId! },
                    cancellationToken: ct),
                cancellationToken);

            if (authorization.IsVoid == true
                || authorization.VoidState == VoidState.Succeeded
                || authorization.State == State1.Canceled)
            {
                ApplyVoid(order, authorization);
                return CancelResponse(order);
            }
        }
        catch (SdkException)
        {
        }

        return null;
    }

    private async Task<ServiceResult> SettleUnknownAuthorizationAsync(
        OrderPaymentRecord order,
        string claimKey,
        SdkException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Unknown authorization outcome for order {OrderId}; re-reading Finix by idempotency id.",
            order.OrderId);

        try
        {
            var settled = await TryFindAuthorizationAsync(AuthorizationIdempotencyId(order), cancellationToken);
            if (settled is not null)
            {
                ApplyAuthorization(order, settled, order.PaymentMethodId);
                var response = PayResponse(order);
                Complete(claimKey, 200, response);
                return ServiceResult.Ok(response);
            }
        }
        catch (SdkException)
        {
        }

        _store.WithOrder(order.OrderId, o =>
        {
            o.Status = OrderPaymentStatus.PaymentUnknown;
            o.UpdatedAt = DateTimeOffset.UtcNow;
            return o;
        });
        Release(claimKey);
        return ServiceResult.Failure(
            502,
            "AUTHORIZATION_OUTCOME_UNKNOWN",
            "Finix did not confirm whether the hold was created. The same order can be retried safely; " +
            "the request reuses the same Finix idempotency id.");
    }

    private async Task<ServiceResult> SettleUnknownCaptureAsync(
        OrderPaymentRecord order,
        string claimKey,
        SdkException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Unknown capture outcome for order {OrderId}; re-reading the authorization.",
            order.OrderId);

        var recovered = await TryRecoverCaptureAsync(order, cancellationToken);
        if (recovered is not null)
        {
            Complete(claimKey, 200, recovered);
            return ServiceResult.Ok(recovered);
        }

        Release(claimKey);
        return ServiceResult.Failure(
            502,
            "CAPTURE_OUTCOME_UNKNOWN",
            "Finix did not confirm whether the capture completed. Retry fulfilment; the authorization is re-read before capture.");
    }

    private async Task<ServiceResult> SettleUnknownVoidAsync(
        OrderPaymentRecord order,
        string claimKey,
        SdkException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Unknown void outcome for order {OrderId}; re-reading the authorization.",
            order.OrderId);

        var recovered = await TryRecoverVoidAsync(order, cancellationToken);
        if (recovered is not null)
        {
            Complete(claimKey, 200, recovered);
            return ServiceResult.Ok(recovered);
        }

        Release(claimKey);
        return ServiceResult.Failure(
            502,
            "CANCEL_OUTCOME_UNKNOWN",
            "Finix did not confirm whether the hold was released. Retry cancellation; voiding an already voided hold is harmless.");
    }

    private async Task<ServiceResult> SettleUnknownRefundAsync(
        OrderPaymentRecord order,
        RefundRecord refund,
        string claimKey,
        IReadOnlyCollection<string> beforeIds,
        SdkException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Unknown refund outcome for order {OrderId} key {Key}; re-reading reversals.",
            order.OrderId, refund.IdempotencyKey);

        var reversal = await TryFindNewReversalAsync(order.CaptureTransferId!, beforeIds, refund.AmountCents, cancellationToken);
        if (reversal is not null)
        {
            _store.WithOrder(order.OrderId, o =>
            {
                var record = o.Refunds.First(r => r.RefundId == refund.RefundId);
                record.FinixRefundId = reversal.Id;
                record.Status = reversal.State?.Value ?? "PENDING";
                record.FailureCode = reversal.FailureCode;
                record.FailureMessage = reversal.FailureMessage;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                o.UpdatedAt = DateTimeOffset.UtcNow;
                return record;
            });

            var response = RefundResponse(order, refund);
            Complete(claimKey, 200, response);
            return ServiceResult.Ok(response);
        }

        _store.WithOrder(order.OrderId, o =>
        {
            var record = o.Refunds.First(r => r.RefundId == refund.RefundId);
            record.Status = "UNKNOWN";
            record.UpdatedAt = DateTimeOffset.UtcNow;
            o.UpdatedAt = DateTimeOffset.UtcNow;
            return record;
        });

        var unknownResponse = RefundResponse(order, refund);
        unknownResponse.Explanation =
            "Finix did not confirm whether the refund was created. The amount remains reserved against the capture; " +
            "retrying with the same idempotency key settles it.";
        Complete(claimKey, 202, unknownResponse);
        return new ServiceResult(202, unknownResponse);
    }

    private async Task<ServiceResult> SettleUnknownCardAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        string last4,
        string name,
        string claimKey,
        SdkException exception,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            exception,
            "Unknown card-save outcome for {BuyerId} ending {Last4}; re-reading payment instruments.",
            buyerId, last4);

        var recovered = await TryRecoverSavedCardAsync(buyerId, request, last4, name, cancellationToken);
        if (recovered is not null)
        {
            Complete(claimKey, 201, recovered);
            return ServiceResult.Created(recovered);
        }

        Release(claimKey);
        return ServiceResult.Failure(
            502,
            "CARD_SAVE_OUTCOME_UNKNOWN",
            "Finix did not confirm whether the card was saved. Retry the request; an identical card is matched before creating another.");
    }

    private async Task<SavePaymentMethodResponse?> TryRecoverSavedCardAsync(
        string buyerId,
        SavePaymentMethodRequest request,
        string last4,
        string name,
        CancellationToken cancellationToken)
    {
        try
        {
            var identity = await _merchantContext.GetIdentityAsync(cancellationToken);
            string? cursor = null;
            var pages = 0;
            do
            {
                var page = await _client.PaymentInstrumentsApi.ListPaymentInstruments(
                    new ListPaymentInstrumentsRequest
                    {
                        OwnerIdentityId = identity,
                        Last4 = last4,
                        ExpirationMonth = request.ExpirationMonth.ToString(),
                        ExpirationYear = request.ExpirationYear.ToString(),
                        CreatedAtGte = DateTimeOffset.UtcNow.AddMinutes(-10),
                        Limit = 100,
                        AfterCursor = cursor
                    },
                    cancellationToken: cancellationToken);

                if (page.Embedded?.PaymentInstruments is { Count: > 0 } instruments)
                {
                    foreach (var item in instruments)
                    {
                        if (!item.TryGetPaymentInstrumentCard(out var card)
                            || string.IsNullOrWhiteSpace(card.Id))
                        {
                            continue;
                        }

                        var existing = _store.FindCardByFinixId(card.Id);
                        if (existing is not null)
                        {
                            return CardResponse(existing);
                        }

                        var saved = new SavedCardRecord
                        {
                            PaymentMethodId = "pm_" + Guid.NewGuid().ToString("N"),
                            BuyerId = buyerId,
                            FinixPaymentInstrumentId = card.Id,
                            Brand = card.Brand?.Value,
                            Last4 = card.LastFour ?? last4,
                            ExpirationMonth = card.ExpirationMonth ?? request.ExpirationMonth,
                            ExpirationYear = card.ExpirationYear ?? request.ExpirationYear,
                            Name = card.Name ?? name
                        };
                        _store.AddCard(saved);
                        return CardResponse(saved);
                    }
                }

                cursor = page.Page?.NextCursor;
                pages++;
            }
            while (!string.IsNullOrWhiteSpace(cursor) && pages < 20);
        }
        catch (SdkException)
        {
        }

        return null;
    }

    private async Task<bool> IsCardDisabledAtFinixAsync(string finixPaymentInstrumentId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.PaymentInstrumentsApi.GetPaymentInstrument(
                new GetPaymentInstrumentRequest { PaymentInstrumentId = finixPaymentInstrumentId },
                cancellationToken: cancellationToken);

            return response.TryGetPaymentInstrumentCard(out var card) && card.Enabled == false;
        }
        catch (SdkException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyCollection<string>> TryListReversalIdsAsync(
        string transferId,
        CancellationToken cancellationToken)
    {
        try
        {
            return (await ListReversalsAsync(transferId, cancellationToken))
                .Where(t => !string.IsNullOrWhiteSpace(t.Id))
                .Select(t => t.Id!)
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (SdkException)
        {
            return Array.Empty<string>();
        }
    }

    private async Task<List<Transfer>> ListReversalsAsync(string transferId, CancellationToken cancellationToken)
    {
        var reversals = new List<Transfer>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await _client.TransfersApi.ListTransferReversals(
                new ListTransferReversalsRequest
                {
                    TransferId = transferId,
                    Limit = 100,
                    AfterCursor = cursor
                },
                cancellationToken: cancellationToken);

            if (page.Embedded?.Transfers is { Count: > 0 } items)
            {
                reversals.AddRange(items);
            }

            cursor = page.Page?.NextCursor;
            pages++;
        }
        while (!string.IsNullOrWhiteSpace(cursor) && pages < 20);

        return reversals;
    }

    private async Task<Transfer?> TryFindNewReversalAsync(
        string transferId,
        IReadOnlyCollection<string> beforeIds,
        long amount,
        CancellationToken cancellationToken)
    {
        try
        {
            var reversals = await ListReversalsAsync(transferId, cancellationToken);
            return reversals.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.Id)
                && !beforeIds.Contains(t.Id!)
                && t.Amount == amount);
        }
        catch (SdkException)
        {
            return null;
        }
    }

    private ReconciliationResponse BuildReconciliation(
        DateTimeOffset from,
        DateTimeOffset to,
        IReadOnlyList<Transfer> transfers,
        bool truncated,
        string? nextCursor)
    {
        var orders = _store.GetAllOrders();
        var byCapture = orders
            .Where(o => !string.IsNullOrWhiteSpace(o.CaptureTransferId))
            .GroupBy(o => o.CaptureTransferId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var refunds = orders
            .SelectMany(o => o.Refunds.Select(r => new { Order = o, Refund = r }))
            .Where(x => !string.IsNullOrWhiteSpace(x.Refund.FinixRefundId))
            .GroupBy(x => x.Refund.FinixRefundId!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var response = new ReconciliationResponse { From = from, To = to, Truncated = truncated, NextCursor = truncated ? nextCursor : null };
        var providerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transfer in transfers)
        {
            if (string.IsNullOrWhiteSpace(transfer.Id))
            {
                continue;
            }

            if (transfer.CreatedAt.HasValue
                && (transfer.CreatedAt.Value < from || transfer.CreatedAt.Value > to))
            {
                continue;
            }

            providerIds.Add(transfer.Id);

            if (byCapture.TryGetValue(transfer.Id, out var captureOrder))
            {
                response.Matched.Add(new ReconciliationMatchResponse
                {
                    FinixTransferId = transfer.Id,
                    Kind = "capture",
                    OrderId = captureOrder.OrderId,
                    Amount = transfer.Amount ?? captureOrder.CapturedAmountCents ?? 0,
                    State = transfer.State?.Value,
                    Type = transfer.Type?.Value,
                    CreatedAt = transfer.CreatedAt
                });
            }
            else if (refunds.TryGetValue(transfer.Id, out var refundMatch))
            {
                response.Matched.Add(new ReconciliationMatchResponse
                {
                    FinixTransferId = transfer.Id,
                    Kind = "refund",
                    OrderId = refundMatch.Order.OrderId,
                    RefundId = refundMatch.Refund.RefundId,
                    Amount = transfer.Amount ?? refundMatch.Refund.AmountCents,
                    State = transfer.State?.Value,
                    Type = transfer.Type?.Value,
                    CreatedAt = transfer.CreatedAt
                });
            }
            else
            {
                response.ProviderOnly.Add(new ProviderTransferResponse
                {
                    FinixTransferId = transfer.Id,
                    Amount = transfer.Amount ?? 0,
                    State = transfer.State?.Value,
                    Type = transfer.Type?.Value,
                    ParentTransferId = transfer.ParentTransfer,
                    CreatedAt = transfer.CreatedAt
                });
            }
        }

        foreach (var order in orders.Where(o => o.Order.OrderDate >= from && o.Order.OrderDate <= to))
        {
            if (!string.IsNullOrWhiteSpace(order.CaptureTransferId)
                && !providerIds.Contains(order.CaptureTransferId))
            {
                response.EshopOnly.Add(new EshopOnlyResponse
                {
                    OrderId = order.OrderId,
                    Kind = "capture",
                    FinixTransferId = order.CaptureTransferId,
                    Amount = order.CapturedAmountCents ?? order.TotalAmountCents,
                    Reason = "eShop has a captured order whose Finix transfer is missing from the range."
                });
            }

            foreach (var refund in order.Refunds.Where(r => !string.IsNullOrWhiteSpace(r.FinixRefundId)))
            {
                if (!providerIds.Contains(refund.FinixRefundId!))
                {
                    response.EshopOnly.Add(new EshopOnlyResponse
                    {
                        OrderId = order.OrderId,
                        Kind = "refund",
                        RefundId = refund.RefundId,
                        FinixTransferId = refund.FinixRefundId,
                        Amount = refund.AmountCents,
                        Reason = "eShop has a refund whose Finix reversal is missing from the range."
                    });
                }
            }
        }

        return response;
    }

    private static string? ExplainUncapturableHold(Authorization authorization, OrderPaymentRecord order)
    {
        var state = authorization.State?.Value ?? "UNKNOWN";
        if (authorization.IsVoid == true || authorization.VoidState == VoidState.Succeeded)
        {
            return $"Authorization {authorization.Id} has already been released (void state {authorization.VoidState?.Value}). " +
                   "It can no longer be captured; the shopper must pay again.";
        }

        if (authorization.State == State1.Canceled || authorization.State == State1.Failed)
        {
            return $"Authorization {authorization.Id} is {state} and can no longer be captured. " +
                   "The shopper must pay again; cancel this order or ask them to place a new payment.";
        }

        if (authorization.ExpiresAt.HasValue && authorization.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            return $"Authorization {authorization.Id} expired at {authorization.ExpiresAt:O} and can no longer be captured. " +
                   "The shopper must pay again.";
        }

        if (string.Equals(state, State1.Succeeded.Value, StringComparison.Ordinal)
            && authorization.Transfer is not null)
        {
            return null;
        }

        return null;
    }

    private static void ApplyAuthorization(OrderPaymentRecord order, Authorization authorization, string? paymentMethodId)
    {
        var state = authorization.State?.Value ?? "UNKNOWN";
        order.AuthorizationId = authorization.Id;
        order.HoldStatus = state;
        order.HeldAmountCents = authorization.Amount;
        order.HoldExpiresAt = authorization.ExpiresAt;
        order.PaymentMethodId = paymentMethodId;
        order.FailureCode = authorization.FailureCode;
        order.FailureMessage = authorization.FailureMessage;
        order.Status = string.Equals(state, State1.Failed.Value, StringComparison.Ordinal)
            || string.Equals(state, State1.Canceled.Value, StringComparison.Ordinal)
            ? OrderPaymentStatus.PaymentFailed
            : OrderPaymentStatus.Authorized;
        order.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void UpdateHold(OrderPaymentRecord order, Authorization authorization)
    {
        order.HoldStatus = authorization.State?.Value ?? order.HoldStatus;
        order.HoldExpiresAt = authorization.ExpiresAt ?? order.HoldExpiresAt;
        order.FailureCode = authorization.FailureCode ?? order.FailureCode;
        order.FailureMessage = authorization.FailureMessage ?? order.FailureMessage;
        order.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void ApplyVoid(OrderPaymentRecord order, Authorization authorization)
    {
        order.Status = OrderPaymentStatus.Canceled;
        order.HoldStatus = authorization.State?.Value ?? "CANCELED";
        order.CaptureStatus = null;
        order.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private static void MarkPaymentFailed(OrderPaymentRecord order, ServiceResult failure)
    {
        var body = failure.Body as ApiErrorBody;
        order.Status = OrderPaymentStatus.PaymentFailed;
        order.FailureCode = body?.Error;
        order.FailureMessage = body?.Explanation;
        order.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private void MarkCardDisabled(string paymentMethodId)
    {
        _store.WithCard(paymentMethodId, card =>
        {
            card.Enabled = false;
            card.DisabledAt = DateTimeOffset.UtcNow;
            return card;
        });
    }

    private void RemoveFailedRefund(int orderId, string refundId)
    {
        _store.WithOrder(orderId, order =>
        {
            var record = order.Refunds.FirstOrDefault(r => r.RefundId == refundId);
            if (record is not null)
            {
                order.Refunds.Remove(record);
                order.RefundedAmountCents -= record.AmountCents;
                order.UpdatedAt = DateTimeOffset.UtcNow;
            }

            return order;
        });
    }

    private static RefundRecord? FindRefundByKey(OrderPaymentRecord order, string key)
    {
        return order.Refunds.FirstOrDefault(r =>
            string.Equals(r.IdempotencyKey, key, StringComparison.Ordinal));
    }

    private static PayOrderResponse PayResponse(OrderPaymentRecord order) => new()
    {
        OrderId = order.OrderId,
        Status = StatusText(order.Status),
        AuthorizationId = order.AuthorizationId,
        HeldAmount = order.HeldAmountCents,
        Currency = order.Currency,
        ExpiresAt = order.HoldExpiresAt,
        FailureCode = order.FailureCode,
        FailureMessage = order.FailureMessage
    };

    private static FulfilOrderResponse FulfilResponse(OrderPaymentRecord order) => new()
    {
        OrderId = order.OrderId,
        Status = StatusText(order.Status),
        CapturedAmount = order.CapturedAmountCents,
        FinixFee = order.FinixFeeCents,
        NetProceeds = order.NetProceedsCents,
        Currency = order.Currency,
        CaptureTransferId = order.CaptureTransferId,
        FeesFinalized = order.FeesFinalized,
        Explanation = order.OperatorExplanation
    };

    private static CancelOrderResponse CancelResponse(OrderPaymentRecord order) => new()
    {
        OrderId = order.OrderId,
        Status = StatusText(order.Status),
        AuthorizationId = order.AuthorizationId,
        VoidState = order.HoldStatus,
        Released = order.Status == OrderPaymentStatus.Canceled
    };

    private static RefundOrderResponse RefundResponse(OrderPaymentRecord order, RefundRecord refund) => new()
    {
        RefundId = refund.RefundId,
        OrderId = order.OrderId,
        Amount = refund.AmountCents,
        Currency = order.Currency,
        Status = refund.Status,
        IdempotencyKey = refund.IdempotencyKey,
        FinixRefundId = refund.FinixRefundId,
        Explanation = refund.FailureMessage
    };

    private static MyOrderResponse ToMyOrderResponse(OrderPaymentRecord order) => new()
    {
        OrderId = order.OrderId,
        Status = StatusText(order.Status),
        TotalAmount = order.TotalAmountCents,
        Currency = order.Currency,
        OrderDate = order.Order.OrderDate,
        Payment = new PaymentStateResponse
        {
            PaymentMethodId = order.PaymentMethodId,
            AuthorizationId = order.AuthorizationId,
            HoldStatus = order.HoldStatus,
            HeldAmount = order.HeldAmountCents,
            ExpiresAt = order.HoldExpiresAt,
            CaptureTransferId = order.CaptureTransferId,
            CaptureStatus = order.CaptureStatus,
            CapturedAmount = order.CapturedAmountCents,
            FinixFee = order.FinixFeeCents,
            NetProceeds = order.NetProceedsCents,
            FeesFinalized = order.FeesFinalized,
            RefundedAmount = order.RefundedAmountCents,
            Refunds = order.Refunds.Select(r => new RefundStateResponse
            {
                RefundId = r.RefundId,
                FinixRefundId = r.FinixRefundId,
                Amount = r.AmountCents,
                Status = r.Status,
                IdempotencyKey = r.IdempotencyKey,
                CreatedAt = r.CreatedAt
            }).ToList()
        }
    };

    private static SavePaymentMethodResponse CardResponse(SavedCardRecord card) => new()
    {
        PaymentMethodId = card.PaymentMethodId,
        Brand = card.Brand,
        Last4 = card.Last4,
        ExpirationMonth = card.ExpirationMonth,
        ExpirationYear = card.ExpirationYear,
        Name = card.Name,
        Description = DescribeCard(card)
    };

    private static string DescribeCard(SavedCardRecord card)
    {
        var brand = string.IsNullOrWhiteSpace(card.Brand) ? "Card" : card.Brand;
        var last4 = string.IsNullOrWhiteSpace(card.Last4) ? "****" : card.Last4;
        return $"{brand} ending in {last4}";
    }

    private static ServiceResult? ValidateCard(CardDetailsRequest card, out string normalizedNumber)
    {
        normalizedNumber = new string((card.Number ?? string.Empty).Where(char.IsDigit).ToArray());

        if (normalizedNumber.Length < 12 || normalizedNumber.Length > 19)
        {
            return ServiceResult.BadRequest("INVALID_CARD_NUMBER", "A valid card number is required.");
        }

        if (card.ExpirationMonth is < 1 or > 12)
        {
            return ServiceResult.BadRequest("INVALID_EXPIRATION_MONTH", "Expiration month must be between 1 and 12.");
        }

        if (card.ExpirationYear < DateTimeOffset.UtcNow.Year)
        {
            return ServiceResult.BadRequest("INVALID_EXPIRATION_YEAR", "The card has expired.");
        }

        if (string.IsNullOrWhiteSpace(card.Name))
        {
            return ServiceResult.BadRequest("CARDHOLDER_NAME_REQUIRED", "The cardholder name is required.");
        }

        if (!string.IsNullOrWhiteSpace(card.SecurityCode)
            && (card.SecurityCode.Length is < 3 or > 4 || !card.SecurityCode.All(char.IsDigit)))
        {
            return ServiceResult.BadRequest("INVALID_SECURITY_CODE", "The security code must be 3 or 4 digits.");
        }

        return null;
    }

    private static ServiceResult? ValidateCard(SavePaymentMethodRequest card, out string normalizedNumber)
    {
        return ValidateCard(
            new CardDetailsRequest
            {
                Number = card.Number,
                ExpirationMonth = card.ExpirationMonth,
                ExpirationYear = card.ExpirationYear,
                SecurityCode = card.SecurityCode,
                Name = card.Name
            },
            out normalizedNumber);
    }

    private static OrderAddress BuildAddress(ShipToAddressRequest? address)
    {
        if (address is null)
        {
            return new OrderAddress("1 Main Street", "Seattle", "WA", "USA", "98101");
        }

        return new OrderAddress(
            string.IsNullOrWhiteSpace(address.Street) ? "1 Main Street" : address.Street,
            string.IsNullOrWhiteSpace(address.City) ? "Seattle" : address.City,
            string.IsNullOrWhiteSpace(address.State) ? "WA" : address.State,
            string.IsNullOrWhiteSpace(address.Country) ? "USA" : address.Country,
            string.IsNullOrWhiteSpace(address.ZipCode) ? "98101" : address.ZipCode);
    }

    private static long ToCents(decimal amount) =>
        checked((long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero));

    private static string AuthorizationIdempotencyId(OrderPaymentRecord order) =>
        $"eshop-{order.PaymentReference}-auth";

    private static string StatusText(OrderPaymentStatus status) => status switch
    {
        OrderPaymentStatus.AwaitingPayment => "AwaitingPayment",
        OrderPaymentStatus.Authorized => "Authorized",
        OrderPaymentStatus.Fulfilled => "Fulfilled",
        OrderPaymentStatus.Canceled => "Canceled",
        OrderPaymentStatus.PaymentFailed => "PaymentFailed",
        OrderPaymentStatus.PaymentUnknown => "PaymentUnknown",
        _ => status.ToString()
    };

    private static ServiceResult FromGenericError(int status, GenericError generic, string fallback)
    {
        var first = generic.Embedded?.Errors?.FirstOrDefault();
        var code = first?.Code ?? "FINIX_ERROR";
        var message = first?.Message ?? fallback;
        return ServiceResult.Failure(MapStatus(status), code, message);
    }

    private static ServiceResult FromApiException(ApiException ex, string fallback) =>
        ServiceResult.Failure(MapStatus((int)ex.StatusCode), $"FINIX_HTTP_{(int)ex.StatusCode}", fallback);

    private static int MapStatus(int status)
    {
        if (status is 401 or 403)
        {
            return 502;
        }

        if (status == 429)
        {
            return 503;
        }

        return status is >= 400 and < 500 ? status : 502;
    }

    private void Release(string? claimKey)
    {
        if (claimKey is not null)
        {
            _store.ReleaseClaim(claimKey);
        }
    }

    private void Complete(string? claimKey, int statusCode, object? body)
    {
        if (claimKey is not null)
        {
            _store.CompleteClaim(claimKey, statusCode, body);
        }
    }

    private static async Task<T> BoundedAsync<T>(
        TimeSpan budget,
        Func<CancellationToken, Task<T>> call,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(budget);
        return await call(cts.Token);
    }
}
