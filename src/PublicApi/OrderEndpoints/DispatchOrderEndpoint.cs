using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, DispatchOrderRequest>
{
    private readonly IShopperOrderService _orders;

    public DispatchOrderEndpoint(IShopperOrderService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(new DispatchOrderRequest
                {
                    OrderId = orderId,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<DispatchOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(DispatchOrderRequest request)
    {
        try
        {
            var order = await _orders.DispatchAsync(request.OrderId, request.CancellationToken);
            return Results.Ok(new DispatchOrderResponse
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (OrderStateException ex)
        {
            return Results.Conflict(new { message = ex.Message });
        }
    }
}

public class DispatchOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal CancellationToken CancellationToken { get; set; }
}

public class DispatchOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
