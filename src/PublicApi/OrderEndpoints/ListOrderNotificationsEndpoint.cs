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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, ICatalogOrderService>
{
    private readonly IOrderNotificationService _notificationService;

    public ListOrderNotificationsEndpoint(IOrderNotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, HttpContext httpContext, ICatalogOrderService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), service, httpContext, ct);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, ICatalogOrderService service)
        => HandleAsync(request, service, null!, CancellationToken.None);

    private async Task<IResult> HandleAsync(
        ListOrderNotificationsRequest request,
        ICatalogOrderService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var buyerId = EndpointIdentity.GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        await service.GetForBuyerAsync(request.OrderId, buyerId, ct);
        var notifications = await _notificationService.ListForOrderAsync(request.OrderId, ct);
        await _notificationService.RefreshFromProviderAsync(notifications, ct);

        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
