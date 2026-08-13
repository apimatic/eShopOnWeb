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
/// Operator action: marks an order dispatched. The shopper is told it is on its way and a "how did
/// delivery go?" follow-up is queued with the provider for a few days later.
/// </summary>
public class DispatchOrderEndpoint : IEndpoint<IResult, IShopOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopOrderService orderService) =>
            {
                var result = await orderService.DispatchOrderAsync(orderId);
                return OrderTransitionResults.From(result);
            })
            .Produces<OrderTransitionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(IShopOrderService orderService) =>
        Task.FromResult<IResult>(Results.Empty);
}

public class OrderTransitionResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

/// <summary>Maps an <see cref="OrderOperationResult"/> to an HTTP result for the transition endpoints.</summary>
public static class OrderTransitionResults
{
    public static IResult From(OrderOperationResult result) => result.Status switch
    {
        OrderOperationStatus.NotFound => Results.NotFound(),
        OrderOperationStatus.Invalid => Results.Conflict(result.Error),
        _ => Results.Ok(new OrderTransitionResponse
        {
            OrderId = result.Order!.Id,
            Status = result.Order!.Status.ToString()
        })
    };
}
