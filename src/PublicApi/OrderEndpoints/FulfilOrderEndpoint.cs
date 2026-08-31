using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: fulfils a paid order, which captures the held funds at PayPal.
/// A stale authorization is renewed first; one that can no longer be renewed produces
/// an actionable error. Repeating the call for a fulfilled order returns its state.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IRepository<OrderPayment> _paymentRepository;
    private readonly IPaymentGateway _paymentGateway;

    public FulfilOrderEndpoint(IRepository<Order> orderRepository,
        IRepository<OrderPayment> paymentRepository,
        IPaymentGateway paymentGateway)
    {
        _orderRepository = orderRepository;
        _paymentRepository = paymentRepository;
        _paymentGateway = paymentGateway;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(new FulfilOrderRequest(), orderId);
            })
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(FulfilOrderRequest request, int orderId)
    {
        var order = await _orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound();
        }

        var payment = await _paymentRepository.FirstOrDefaultAsync(new OrderPaymentByOrderIdSpec(orderId));

        if (order.Status == OrderStatus.Fulfilled && payment?.CaptureId != null)
        {
            // Idempotent replay: the capture already happened.
            return Results.Ok(BuildResponse(request, order, payment, false));
        }
        if (order.Status != OrderStatus.PaymentAuthorized)
        {
            return Results.Conflict($"Order {orderId} is {order.Status}; only a paid order can be fulfilled.");
        }
        if (payment?.AuthorizationId == null)
        {
            return Results.Conflict($"Order {orderId} has no PayPal authorization to capture.");
        }

        var renewed = false;
        try
        {
            var authorization = await _paymentGateway.GetAuthorizationAsync(payment.AuthorizationId);
            var stale = (authorization.Status != "CREATED" && authorization.Status != "PENDING")
                || authorization.ExpirationTime <= DateTimeOffset.UtcNow;

            if (stale)
            {
                var renewedAuthorization = await RenewAsync(payment, orderId);
                payment.MarkAuthorizationRenewed(renewedAuthorization.Id, renewedAuthorization.Status, renewedAuthorization.ExpirationTime);
                renewed = true;
            }

            var capture = await CaptureAsync(payment, orderId);
            payment.MarkCaptured(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
        }
        catch (PayPalApiException) when (!renewed)
        {
            // The hold may have expired between our check and the capture: renew once, then retry.
            try
            {
                var renewedAuthorization = await RenewAsync(payment, orderId);
                payment.MarkAuthorizationRenewed(renewedAuthorization.Id, renewedAuthorization.Status, renewedAuthorization.ExpirationTime);
                renewed = true;

                var capture = await CaptureAsync(payment, orderId);
                payment.MarkCaptured(capture.Id, capture.Status, capture.GrossAmount, capture.PayPalFee, capture.NetAmount);
            }
            catch (PayPalApiException retryEx)
            {
                await _paymentRepository.UpdateAsync(payment);
                return UnrenewableError(orderId, payment.AuthorizationId, retryEx);
            }
        }
        catch (PayPalApiException ex)
        {
            await _paymentRepository.UpdateAsync(payment);
            return UnrenewableError(orderId, payment.AuthorizationId, ex);
        }

        order.MarkFulfilled();
        await _orderRepository.UpdateAsync(order);
        await _paymentRepository.UpdateAsync(payment);

        return Results.Ok(BuildResponse(request, order, payment, renewed));
    }

    private Task<PayPalAuthorizationInfo> RenewAsync(OrderPayment payment, int orderId) =>
        _paymentGateway.ReauthorizeAsync(payment.AuthorizationId!, payment.Amount, payment.Currency,
            $"eshop-order-{orderId}-reauthorize-{payment.AuthorizationAttempts}-{PaymentRunContext.RunId}");

    private Task<PayPalCaptureInfo> CaptureAsync(OrderPayment payment, int orderId) =>
        _paymentGateway.CaptureAuthorizationAsync(payment.AuthorizationId!,
            $"eshop-order-{orderId}-capture-{PaymentRunContext.RunId}", $"eshop-order-{orderId}-capture-{PaymentRunContext.RunId}");

    private static IResult UnrenewableError(int orderId, string? authorizationId, PayPalApiException ex) =>
        Results.UnprocessableEntity(
            $"The PayPal authorization {authorizationId} for order {orderId} can no longer be renewed or captured " +
            $"({ex.Message}; debug id: {ex.DebugId}). PayPal only allows reauthorization within 29 days of the original hold. " +
            "Do not fulfil this order against the old hold: cancel it and ask the shopper to place and pay for a new order.");

    private static FulfilOrderResponse BuildResponse(FulfilOrderRequest request, Order order, OrderPayment payment, bool renewed) =>
        new(request.CorrelationId())
        {
            OrderId = order.Id,
            Status = order.Status.ToString(),
            AuthorizationRenewed = renewed,
            Payment = OrderDtoMapper.Map(payment)
        };
}
