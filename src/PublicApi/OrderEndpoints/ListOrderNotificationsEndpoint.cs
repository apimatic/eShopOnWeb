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
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext httpContext, IShopperOrderService service) =>
            {
                var unauthorized = BuyerIdentity.RequireBuyer(httpContext.User, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new ListOrderNotificationsRequest(orderId, buyerId), service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService service)
    {
        var order = await service.GetBuyerOrderAsync(request.BuyerId, request.OrderId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(request.OrderId, refreshFromProvider: true);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
