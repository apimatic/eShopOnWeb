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

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IShopperOrderService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest(orderId), service, user, http.RequestAborted);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, new ClaimsPrincipal(), default);

    private async Task<IResult> HandleAsync(
        ListOrderNotificationsRequest request,
        IShopperOrderService service,
        ClaimsPrincipal user,
        System.Threading.CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var notifications = await service.ListNotificationsAsync(request.OrderId, buyerId, user.IsAdministrator(), cancellationToken);
        var response = new ListOrderNotificationsResponse(request.CorrelationId())
        {
            OrderId = request.OrderId,
            Notifications = notifications.Select(NotificationDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; init; }

    public ListOrderNotificationsRequest(int orderId)
    {
        OrderId = orderId;
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public ListOrderNotificationsResponse(System.Guid correlationId) : base(correlationId) { }
    public ListOrderNotificationsResponse() { }

    public int OrderId { get; set; }
    public System.Collections.Generic.List<NotificationDto> Notifications { get; set; } = new();
}
