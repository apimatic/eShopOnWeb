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

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
}

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notifications;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor, IOrderNotificationService notifications)
    {
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService orderService) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest { OrderId = orderId }, orderService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService orderService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.GetBuyerId();
        if (string.IsNullOrWhiteSpace(buyerId))
            return Results.Unauthorized();

        var ct = _httpContextAccessor.HttpContext!.RequestAborted;
        var order = await orderService.GetByIdForBuyerAsync(request.OrderId, buyerId, ct);
        if (order is null)
            return Results.NotFound();

        var notifications = await _notifications.ListForOrderAsync(request.OrderId, buyerId, ct);
        var response = new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId
        };
        response.Notifications.AddRange(notifications.Select(ListMyOrdersEndpoint.ToDto));
        return Results.Ok(response);
    }
}
