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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IShopperOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public GetOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IShopperOrderService service, ClaimsPrincipal user) =>
            {
                var buyerId = BuyerIdentity.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(new GetOrderNotificationsRequest(orderId, buyerId), service);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopperOrderService service)
    {
        var order = await service.GetForBuyerAsync(request.OrderId, request.BuyerId);
        if (order is null)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.ListForOrderAsync(request.OrderId);
        var response = new GetOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };

        return Results.Ok(response);
    }
}

public class GetOrderNotificationsRequest : BaseRequest
{
    public GetOrderNotificationsRequest(int orderId, string buyerId)
    {
        OrderId = orderId;
        BuyerId = buyerId;
    }

    public int OrderId { get; }
    public string BuyerId { get; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}
