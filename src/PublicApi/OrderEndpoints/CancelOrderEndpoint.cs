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

public class CancelOrderEndpoint : IEndpoint<IResult, CancelOrderRequest>
{
    private readonly IShopperOrderService _orders;

    public CancelOrderEndpoint(IShopperOrderService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                return await HandleAsync(new CancelOrderRequest
                {
                    OrderId = orderId,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CancelOrderRequest request)
    {
        try
        {
            var order = await _orders.CancelAsync(request.OrderId, request.CancellationToken);
            return Results.Ok(new CancelOrderResponse
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

public class CancelOrderRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal CancellationToken CancellationToken { get; set; }
}

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}
