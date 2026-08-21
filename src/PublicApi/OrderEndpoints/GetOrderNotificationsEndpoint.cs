using System;
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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IShopperOrderService _orders;

    public GetOrderNotificationsEndpoint(IShopperOrderService orders)
    {
        _orders = orders;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service, HttpContext http) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), service, http);
            })
            .Produces<GetOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService service)
        => throw new NotSupportedException();

    private async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService service, HttpContext http)
    {
        var buyerId = CallerIdentity.RequireBuyerId(http);
        var isAdmin = CallerIdentity.IsAdministrator(http);
        if (isAdmin)
        {
            await _orders.GetAsync(request.OrderId, http.RequestAborted);
        }
        else
        {
            await _orders.GetForBuyerAsync(buyerId, request.OrderId, http.RequestAborted);
        }

        var notifications = await service.ListForOrderAsync(request.OrderId, buyerId, isAdmin, http.RequestAborted);
        var response = new GetOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId
        };
        response.Notifications.AddRange(notifications.Select(NotificationMapper.ToDto));
        return Results.Ok(response);
    }
}
