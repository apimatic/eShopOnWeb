using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, int, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListOrderNotificationsEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId:int}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, IOrderNotificationService orderNotificationService) =>
            {
                return await HandleAsync(orderId, orderNotificationService);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOrderNotificationService orderNotificationService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = EndpointHelpers.GetBuyerId(httpContext);
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await orderNotificationService.ListNotificationsAsync(
            orderId,
            buyerId,
            EndpointHelpers.IsAdministrator(httpContext));

        return result.ToHttpResult(notifications => Results.Ok(new ListOrderNotificationsResponse
        {
            OrderId = orderId,
            Notifications = notifications.Select(OrderNotificationDto.From).ToList()
        }));
    }
}

public class ListOrderNotificationsResponse : BaseResponse
{
    public int OrderId { get; set; }
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
