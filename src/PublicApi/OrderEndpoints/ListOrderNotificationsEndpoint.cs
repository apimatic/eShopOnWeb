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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, OrderActionRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                var unauthorized = HttpCaller.RequireBuyerId(user, out var buyerId);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new OrderActionRequest(orderId), service, buyerId, HttpCaller.IsAdministrator(user));
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(OrderActionRequest request, IOrderNotificationService service)
        => HandleAsync(request, service, string.Empty, false);

    private async Task<IResult> HandleAsync(
        OrderActionRequest request,
        IOrderNotificationService service,
        string buyerId,
        bool isAdministrator)
    {
        var notifications = await service.GetNotificationsForOrderAsync(request.OrderId, buyerId, isAdministrator, default);
        var response = new ListOrderNotificationsResponse(request.CorrelationId());
        response.Notifications.AddRange(notifications.Select(ListMyOrdersEndpoint.MapNotification));
        return Results.Ok(response);
    }
}
