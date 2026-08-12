using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a delivery
/// follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, int, IOrderProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderProcessingService orderProcessingService) =>
            {
                return await HandleAsync(orderId, orderProcessingService);
            })
            .Produces<OrderStatusResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderProcessingService orderProcessingService)
    {
        var result = await orderProcessingService.DispatchOrderAsync(orderId);

        if (!result.Found)
        {
            return Results.NotFound();
        }
        if (result.Error is not null)
        {
            return Results.Conflict(new { error = result.Error });
        }

        return Results.Ok(new OrderStatusResponse
        {
            OrderId = result.Order!.Id,
            Status = result.Order.Status.ToString()
        });
    }
}

public class OrderStatusResponse : BaseResponse
{
    public OrderStatusResponse(Guid correlationId) : base(correlationId) { }
    public OrderStatusResponse() { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
