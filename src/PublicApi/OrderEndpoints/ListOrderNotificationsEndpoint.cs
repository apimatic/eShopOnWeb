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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService orderService, HttpContext httpContext) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), httpContext, orderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService orderService)
        => HandleAsync(request, null!, orderService);

    private async Task<IResult> HandleAsync(
        ListOrderNotificationsRequest request,
        HttpContext httpContext,
        IShopperOrderService orderService)
    {
        var response = new ListOrderNotificationsResponse(request.CorrelationId()) { OrderId = request.OrderId };
        var notifications = await orderService.ListNotificationsAsync(
            request.OrderId,
            httpContext.GetBuyerId(),
            httpContext.IsAdministrator(),
            httpContext.RequestAborted);

        response.Notifications.AddRange(notifications.Select(OrderNotificationDto.From));
        return Results.Ok(response);
    }
}
