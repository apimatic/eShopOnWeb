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

/// <summary>
/// GET /api/my-orders — the caller's own orders, each showing where its notifications got to (statuses are
/// refreshed against the provider). One shopper never sees another's orders.
/// </summary>
public class GetMyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service) =>
            {
                return await HandleAsync(service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        var ownerId = EndpointCaller.UserName(_httpContextAccessor);
        if (string.IsNullOrEmpty(ownerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(ownerId, EndpointCaller.RequestAborted(_httpContextAccessor));
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(o => new OrderWithNotificationsDto
            {
                OrderId = o.Order.Id,
                OrderDate = o.Order.OrderDate,
                Total = o.Order.Total(),
                Notifications = o.Notifications.Select(NotificationMapper.ToDto).ToList()
            }).ToList()
        };
        return Results.Ok(response);
    }
}
