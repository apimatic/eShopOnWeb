using System;
using System.Collections.Generic;
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
    public GetOrderNotificationsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public GetOrderNotificationsResponse()
    {
    }

    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public GetOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService orders) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), orders);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopperOrderService orders)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        await orders.GetOrderForShopperAsync(request.OrderId, buyerId);
        var notifications = await _notifications.GetForOrderAsync(request.OrderId, buyerId);

        var response = new GetOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId
        };
        foreach (var notification in notifications)
        {
            response.Notifications.Add(NotificationDto.From(notification));
        }

        return Results.Ok(response);
    }
}
