using System.Collections.Generic;
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

public class GetOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public GetOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationStatusDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOrderNotificationService notifications) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), notifications);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IOrderNotificationService notifications)
    {
        var http = _httpContextAccessor.HttpContext;
        var buyerId = http?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        try
        {
            var items = await notifications.GetOrderNotificationsAsync(
                buyerId,
                request.OrderId,
                http?.RequestAborted ?? default);
            return Results.Ok(new GetOrderNotificationsResponse
            {
                OrderId = request.OrderId,
                Notifications = items.Select(ListMyOrdersEndpoint.MapNotification).ToList()
            });
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
    }
}
