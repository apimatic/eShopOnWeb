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
    public int OrderId { get; set; }
}

public class GetOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IShopOrderService>
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
            (int orderId, IShopOrderService orders) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest { OrderId = orderId }, orders);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopOrderService orders)
    {
        var buyerId = CallerIdentity.GetBuyerId(_httpContextAccessor.HttpContext!);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await orders.GetOrderNotificationsAsync(buyerId, request.OrderId);
        if (notifications == null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new GetOrderNotificationsResponse
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        });
    }
}
