using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CancelOrderResponse : BaseResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class CancelOrderEndpoint : IEndpoint<IResult, int, IOperatorOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CancelOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/cancel",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, IOperatorOrderNotificationService service) =>
            {
                return await HandleAsync(orderId, service);
            })
            .Produces<CancelOrderResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(int orderId, IOperatorOrderNotificationService service)
    {
        var ct = _httpContextAccessor.HttpContext?.RequestAborted ?? default;
        var order = await service.CancelAsync(orderId, ct);
        return Results.Ok(new CancelOrderResponse
        {
            OrderId = order.Id,
            Status = NotificationDtoMapper.OrderStatusName(order.Status)
        });
    }
}
