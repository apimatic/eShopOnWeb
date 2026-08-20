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

public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), service);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IShopperOrderService service)
    {
        try
        {
            var order = await service.CancelAsync(request.OrderId);
            return Results.Ok(new OrderActionResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }
}
