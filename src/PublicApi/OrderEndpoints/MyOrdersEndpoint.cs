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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Returns the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderNotificationService service) =>
            {
                var request = new MyOrdersRequest { CallerId = CallerIdentity.Get(httpContext) ?? string.Empty };
                return await HandleAsync(request, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrEmpty(request.CallerId))
            return Results.Unauthorized();

        var orders = await service.GetOrdersForBuyerAsync(request.CallerId);
        var response = new MyOrdersResponse(request.CorrelationId());
        response.Orders.AddRange(orders.Select(o => new MyOrderDto
        {
            OrderId = o.Order.Id,
            OrderDate = o.Order.OrderDate,
            Total = o.Order.Total(),
            Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
        }));

        return Results.Ok(response);
    }
}

public class MyOrdersRequest : AuthenticatedRequest
{
}

public class MyOrdersResponse : BaseResponse
{
    public MyOrdersResponse(Guid correlationId) : base(correlationId)
    {
    }

    public MyOrdersResponse()
    {
    }

    public List<MyOrderDto> Orders { get; set; } = new();
}
