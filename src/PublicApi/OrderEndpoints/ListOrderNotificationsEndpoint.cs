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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, IShopperOrderService shopperOrderService) =>
            {
                return await HandleAsync(
                    new ListOrderNotificationsRequest(orderId, httpContext.GetBuyerId()),
                    shopperOrderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService shopperOrderService)
    {
        var result = await shopperOrderService.GetOrderNotificationsAsync(request.BuyerId, request.OrderId);
        if (result is null)
        {
            return Results.NotFound();
        }

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = result.Order.Id,
            Status = result.Order.Status.ToString(),
            Notifications = result.Notifications.Select(OrderNotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public ListOrderNotificationsRequest(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public string BuyerId { get; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
