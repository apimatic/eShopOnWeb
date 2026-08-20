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
/// Lists notifications for one of the signed-in shopper's orders, refreshed from Twilio.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IOrderNotificationService service)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.ListOrderNotificationsAsync(buyerId, request.OrderId);
        var response = new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId
        };
        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.From));
        return Results.Ok(response);
    }
}
