using System;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class FulfilOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public FulfilOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class FulfilOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = "";
    public PaymentEndpoints.OrderPaymentDto? Payment { get; set; }
}

/// <summary>
/// Marks the order fulfilled; the money is taken (captured) at this point.
/// A stale authorization is renewed before capturing where PayPal allows it.
/// </summary>
public class FulfilOrderEndpoint : IEndpoint<IResult, FulfilOrderRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/fulfil",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepository, IPaymentService paymentService) =>
                await HandleAsync(orderId, orderRepository, paymentService))
            .Produces<FulfilOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(FulfilOrderRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound(new { error = $"Order {orderId} was not found." });
        }

        var payment = await paymentService.FulfilOrderAsync(order);

        return Results.Ok(new FulfilOrderResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            Payment = payment.ToDto()
        });
    }
}
