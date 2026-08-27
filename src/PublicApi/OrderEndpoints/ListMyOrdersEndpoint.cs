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
/// Lists the signed-in shopper's orders, each showing where its
/// notifications got to.
/// </summary>
public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderNotificationService notificationService) =>
            {
                return await HandleAsync(new ListMyOrdersRequest { BuyerId = user.Identity!.Name! }, notificationService);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderNotificationService notificationService)
    {
        var response = new ListMyOrdersResponse(request.CorrelationId());

        var summaries = await notificationService.GetMyOrdersAsync(request.BuyerId);
        response.Orders.AddRange(summaries.Select(s => new MyOrderDto
        {
            OrderId = s.Order.Id,
            OrderDate = s.Order.OrderDate,
            Status = s.Order.Status.ToString(),
            Total = s.Order.Total(),
            Notifications = s.Notifications.Select(NotificationDto.FromEntity).ToList()
        }));

        return Results.Ok(response);
    }
}
