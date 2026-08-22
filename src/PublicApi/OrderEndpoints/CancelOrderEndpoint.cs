using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOrderWorkflowService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderWorkflowService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<CreateOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderWorkflowService service)
    {
        try
        {
            var order = await service.CancelAsync(orderId);
            return Results.Ok(new CreateOrderResponse(Guid.NewGuid())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
