using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService notifications) =>
            {
                return await HandleAsync(notifications);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService notifications)
    {
        var http = _httpContextAccessor.HttpContext;
        var buyerId = http?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await notifications.GetMyOrdersAsync(buyerId, http?.RequestAborted ?? default);
        var response = new ListMyOrdersResponse
        {
            Orders = orders.Select(o => new MyOrderDto
            {
                OrderId = o.OrderId,
                Status = o.Status.ToString(),
                OrderDate = o.OrderDate,
                Total = o.Total,
                Notifications = o.Notifications.Select(MapNotification).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }

    internal static NotificationStatusDto MapNotification(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        Kind = n.Kind.ToString(),
        ProviderSid = n.ProviderSid,
        Status = n.Status,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        Body = n.ContentRedacted ? null : n.Body,
        ScheduledFor = n.ScheduledFor,
        ContentRedacted = n.ContentRedacted,
        ResendOfNotificationId = n.ResendOfNotificationId
    };
}
