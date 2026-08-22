using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class DispatchOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/dispatch",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), operatorOrderService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOperatorOrderService operatorOrderService)
    {
        var result = await operatorOrderService.DispatchAsync(request.OrderId);
        return Results.Ok(OrderActionResponse.From(request.CorrelationId(), result));
    }
}

public class CancelOrderEndpoint : IEndpoint<IResult, OrderActionRequest, IOperatorOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderService operatorOrderService) =>
            {
                return await HandleAsync(new OrderActionRequest(orderId), operatorOrderService);
            })
            .Produces<OrderActionResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(OrderActionRequest request, IOperatorOrderService operatorOrderService)
    {
        var result = await operatorOrderService.CancelAsync(request.OrderId);
        return Results.Ok(OrderActionResponse.From(request.CorrelationId(), result));
    }
}

public class OrderActionRequest : BaseRequest
{
    public OrderActionRequest(int orderId)
    {
        OrderId = orderId;
    }

    public int OrderId { get; }
}

public class OrderActionResponse : BaseResponse
{
    public OrderActionResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();

    public static OrderActionResponse From(Guid correlationId, ShopperOrderDetails details)
    {
        return new OrderActionResponse(correlationId)
        {
            OrderId = details.Order.Id,
            Status = details.Order.Status.ToString(),
            Notifications = details.Notifications.Select(OrderNotificationDto.From).ToList()
        };
    }
}
