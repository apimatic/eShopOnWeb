using System;
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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IShopperOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOrderNotificationService _notificationService;

    public GetOrderNotificationsEndpoint(
        IHttpContextAccessor httpContextAccessor,
        IOrderNotificationService notificationService)
    {
        _httpContextAccessor = httpContextAccessor;
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), service);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IShopperOrderService service)
    {
        try
        {
            var httpContext = _httpContextAccessor.HttpContext!;
            var buyerId = httpContext.RequireUserName();
            var order = await service.GetByIdForCallerAsync(request.OrderId, buyerId, httpContext.IsAdministrator());
            if (order is null)
            {
                return Results.NotFound(new { message = "Order was not found." });
            }

            var notifications = await _notificationService.ListForOrderAsync(order.Id);
            var response = new GetOrderNotificationsResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Notifications = notifications.Select(NotificationDto.From).ToList()
            };
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            return ex.ToResult();
        }
    }
}
