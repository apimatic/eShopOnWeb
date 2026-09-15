using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
/// Returns the caller's own orders, each showing where its notifications got to.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService service) =>
            {
                var request = new MyOrdersRequest();
                request.SetCaller(user);
                return await HandleAsync(request, service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderNotificationService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(request.CallerUserName);
        var notifications = await service.GetNotificationsForBuyerAsync(request.CallerUserName);
        var byOrder = notifications
            .GroupBy(n => n.OrderId)
            .ToDictionary(g => g.Key, g => g.Select(NotificationDto.FromEntity).ToList());

        var response = new MyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(o => new OrderSummaryDto
            {
                OrderId = o.Id,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total(),
                Notifications = byOrder.TryGetValue(o.Id, out var list) ? list : new List<NotificationDto>()
            }).ToList()
        };

        return Results.Ok(response);
    }
}
