using System;
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

public class OrderIdRequest : BaseRequest
{
    public int OrderId { get; init; }

    public OrderIdRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class DispatchOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DispatchOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopOrderService service) =>
            {
                return await HandleAsync(new OrderIdRequest(orderId), service);
            })
            .Produces<PlaceOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IShopOrderService service)
    {
        try
        {
            var order = await service.DispatchAsync(request.OrderId, _httpContextAccessor.HttpContext!.RequestAborted);
            return Results.Ok(new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
        catch (System.InvalidOperationException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, OrderIdRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopOrderService service) =>
            {
                return await HandleAsync(new OrderIdRequest(orderId), service);
            })
            .Produces<PlaceOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderIdRequest request, IShopOrderService service)
    {
        try
        {
            var order = await service.CancelAsync(request.OrderId, _httpContextAccessor.HttpContext!.RequestAborted);
            return Results.Ok(new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString()
            });
        }
        catch (OrderNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
