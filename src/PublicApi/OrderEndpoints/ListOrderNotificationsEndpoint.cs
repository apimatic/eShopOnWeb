using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Shows what was sent for an order and what became of each message. Shoppers see
/// only their own orders; administrators see any order.
/// </summary>
public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int>
{
    private readonly IRepository<Order> _orderRepository;
    private readonly IOrderNotificationService _notificationService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IRepository<Order> orderRepository,
        IOrderNotificationService notificationService,
        IHttpContextAccessor httpContextAccessor)
    {
        _orderRepository = orderRepository;
        _notificationService = notificationService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId) =>
            {
                return await HandleAsync(orderId);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId)
    {
        var user = _httpContextAccessor.HttpContext?.User;
        var buyerId = user?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            return Results.NotFound();
        }

        var isAdmin = user!.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
        if (!isAdmin && order.BuyerId != buyerId)
        {
            return Results.NotFound();
        }

        var notifications = await _notificationService.GetOrderNotificationsAsync(orderId);
        return Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(ListMyOrdersEndpoint.ToDto).ToList()
        });
    }
}
