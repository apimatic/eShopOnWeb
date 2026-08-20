using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest>
{
    private readonly IShopperOrderService _orders;
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IShopperOrderService orders, IOrderNotificationService notifications)
    {
        _orders = orders;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext) =>
            {
                var unauthorized = HttpCaller.UnauthorizedIfAnonymous(httpContext);
                if (unauthorized is not null)
                {
                    return unauthorized;
                }

                return await HandleAsync(new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = HttpCaller.BuyerId(httpContext)!,
                    CancellationToken = httpContext.RequestAborted
                });
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request)
    {
        var order = await _orders.GetForBuyerAsync(request.BuyerId, request.OrderId, request.CancellationToken);
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await _notifications.ListForOrderAsync(request.BuyerId, request.OrderId, request.CancellationToken);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = order.Id,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    internal string BuyerId { get; set; } = string.Empty;
    internal CancellationToken CancellationToken { get; set; }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
