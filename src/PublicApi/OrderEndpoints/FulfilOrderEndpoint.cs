using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities;
using Microsoft.eShopWeb.ApplicationCore.Entities.BuyerAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.eShopWeb.Infrastructure.PayPal;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class FulfilOrderResponse : BaseResponse
{
    public FulfilOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = string.Empty;
    public string? CaptureId { get; set; }
    public string? CaptureStatus { get; set; }
    public decimal? CapturedAmount { get; set; }
    public decimal? FeeAmount { get; set; }
    public decimal? NetAmount { get; set; }
}

/// <summary>
/// Operator action: marks an order fulfilled and, at that moment, actually captures the
/// held funds. Renews a stale authorization automatically before capturing; if PayPal can no
/// longer renew it, fails with a message an operator can act on (collect a fresh payment).
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, PaymentDependencies>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId,
             IRepository<Order> orderRepository, IRepository<Payment> paymentRepository, IRepository<Buyer> buyerRepository,
             IRepository<CatalogItem> catalogItemRepository, IPayPalClient payPalClient, IOptions<PayPalOptions> payPalOptions) =>
            {
                var request = new FulfilOrderRequest(orderId);
                var deps = new PaymentDependencies(orderRepository, paymentRepository, buyerRepository, catalogItemRepository, payPalClient, payPalOptions.Value);
                return await HandleAsync(request, deps);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, PaymentDependencies deps)
    {
        var response = new FulfilOrderResponse(request.CorrelationId()) { OrderId = request.OrderId };

        var order = await deps.OrderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(request.OrderId));
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await deps.PaymentRepository.FirstOrDefaultAsync(new PaymentByOrderIdSpec(request.OrderId));
        if (payment == null)
        {
            return Results.Problem("This order has no associated payment record.", statusCode: 500);
        }

        // Idempotent in effect: fulfilling an already-fulfilled order just reports the
        // existing capture instead of capturing the shopper again.
        if (order.Status == OrderStatus.Fulfilled)
        {
            return Results.Ok(ToResponse(response, order, payment));
        }

        if (order.Status != OrderStatus.PaymentAuthorized || payment.PayPalAuthorizationId == null)
        {
            return Results.Conflict($"Order {order.Id} is not awaiting fulfilment (status: {order.Status}).");
        }

        var authorizationId = payment.PayPalAuthorizationId;

        PayPalAuthorizationResult live;
        try
        {
            live = await deps.PayPalClient.GetAuthorizationAsync(authorizationId);
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 502, title: "Could not read the current authorization state from PayPal");
        }

        if (string.Equals(live.Status, "VOIDED", StringComparison.OrdinalIgnoreCase) || string.Equals(live.Status, "DENIED", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Conflict($"The payment authorization for order {order.Id} was {live.Status} by PayPal and cannot be captured. " +
                "A new payment must be collected from the shopper (call pay again) before this order can be fulfilled.");
        }

        var isStale = live.ExpiresAt.HasValue && live.ExpiresAt.Value <= DateTimeOffset.UtcNow;
        if (isStale)
        {
            try
            {
                var renewIdempotencyKey = $"order-{order.Id}-reauth-{DateTime.UtcNow:yyyyMMdd}";
                var renewed = await deps.PayPalClient.ReauthorizeAsync(authorizationId, payment.Amount, payment.Currency, renewIdempotencyKey);
                payment.RecordReauthorization(renewed.AuthorizationId, renewed.Status, renewed.ExpiresAt);
                authorizationId = renewed.AuthorizationId;
                await deps.PaymentRepository.UpdateAsync(payment);
            }
            catch (PayPalApiException ex)
            {
                return Results.Conflict(
                    $"The payment authorization for order {order.Id} expired and PayPal could not renew it ({ex.Message}). " +
                    "A new payment must be collected from the shopper (call pay again) before this order can be fulfilled.");
            }
        }

        PayPalCaptureResult capture;
        try
        {
            capture = await deps.PayPalClient.CaptureAuthorizationAsync(authorizationId, $"order-{order.Id}-fulfil");
        }
        catch (PayPalApiException ex)
        {
            return Results.Problem(ex.Message, statusCode: 402, title: ex.ErrorName ?? "Capture failed");
        }

        payment.RecordCapture(capture.CaptureId, capture.Status, capture.Amount, capture.FeeAmount, capture.NetAmount);
        order.MarkFulfilled();

        await deps.PaymentRepository.UpdateAsync(payment);
        await deps.OrderRepository.UpdateAsync(order);

        return Results.Ok(ToResponse(response, order, payment));
    }

    private static FulfilOrderResponse ToResponse(FulfilOrderResponse response, Order order, Payment payment)
    {
        response.OrderStatus = order.Status.ToString();
        response.CaptureId = payment.PayPalCaptureId;
        response.CaptureStatus = payment.CaptureStatus;
        response.CapturedAmount = payment.CapturedAmount;
        response.FeeAmount = payment.PayPalFeeAmount;
        response.NetAmount = payment.NetAmount;
        return response;
    }
}
