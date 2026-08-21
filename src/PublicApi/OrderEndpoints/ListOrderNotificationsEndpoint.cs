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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderWorkflowService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderWorkflowService orders) =>
            {
                return await HandleAsync(orderId, orders);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderWorkflowService orders)
    {
        var buyerId = HttpContextBuyer.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await orders.ListOrderNotificationsAsync(orderId, buyerId);
        var response = new ListOrderNotificationsResponse
        {
            OrderId = orderId
        };
        response.Notifications.AddRange(notifications.Select(NotificationDto.From));
        return Results.Ok(response);
    }
}
