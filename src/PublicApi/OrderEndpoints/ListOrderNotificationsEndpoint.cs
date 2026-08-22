using System.Linq;
using System.Security.Claims;
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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IShopperOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, ClaimsPrincipal user, IShopperOrderNotificationService service, CancellationToken cancellationToken) =>
            {
                var notifications = await service.ListNotificationsForShopperOrderAsync(
                    user.GetBuyerId(),
                    orderId,
                    cancellationToken);
                var response = new ListOrderNotificationsResponse
                {
                    OrderId = orderId,
                    Notifications = notifications.Select(NotificationDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ListOrderNotificationsResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(int request, IShopperOrderNotificationService service)
        => Task.FromResult(Results.Ok());
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}
