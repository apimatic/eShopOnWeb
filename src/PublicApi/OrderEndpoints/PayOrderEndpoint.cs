using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderRequest : BaseRequest
{
    /// <summary>Raw card details for a one-off payment.</summary>
    public PaymentEndpoints.PayPalCardRequest? Card { get; set; }

    /// <summary>Id of one of the shopper's saved cards to pay with instead.</summary>
    public int? PaymentMethodId { get; set; }
}

public class PayOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = "";
    public PaymentEndpoints.OrderPaymentDto? Payment { get; set; }
}

/// <summary>
/// Authorizes the order total with PayPal: places a hold on the money without taking it.
/// Idempotent: paying an already-authorized order returns its existing payment state.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize] async (int orderId, PayOrderRequest request, IHttpContextAccessor httpContextAccessor,
                IRepository<Order> orderRepository, IPaymentService paymentService) =>
                await HandleAsync(orderId, request, httpContextAccessor, orderRepository, paymentService))
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request, IHttpContextAccessor httpContextAccessor,
        IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        var buyerId = httpContextAccessor.HttpContext.User.RequireBuyerId();

        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null || order.BuyerId != buyerId)
        {
            return Results.NotFound(new { error = $"Order {orderId} was not found for this shopper." });
        }

        var payment = await paymentService.PayOrderAsync(order, request.Card.ToCardPayment(), request.PaymentMethodId);

        return Results.Ok(new PayOrderResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = payment.ToDto()
        });
    }
}
