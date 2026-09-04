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

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; init; }

    public CancelOrderRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string OrderStatus { get; set; } = "";
    public string? AuthorizationStatus { get; set; }
}

/// <summary>
/// Cancels the order before fulfilment and releases the shopper's held funds (voids the authorization).
/// </summary>
public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IRepository<Order> orderRepository, IPaymentService paymentService) =>
                await HandleAsync(orderId, orderRepository, paymentService))
            .Produces<CancelOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CancelOrderRequest request) => throw new NotSupportedException();

    public async Task<IResult> HandleAsync(int orderId, IRepository<Order> orderRepository, IPaymentService paymentService)
    {
        var order = await orderRepository.FirstOrDefaultAsync(new OrderWithItemsByIdSpec(orderId));
        if (order == null)
        {
            return Results.NotFound(new { error = $"Order {orderId} was not found." });
        }

        var payment = await paymentService.CancelOrderAsync(order);

        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            OrderStatus = order.Status.ToString(),
            AuthorizationStatus = payment?.AuthorizationStatus
        });
    }
}
