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

public class GetOrderNotificationsEndpoint : IEndpoint<IResult, GetOrderNotificationsRequest, IBuyerOrderService>
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
            (int orderId, IBuyerOrderService buyerOrderService) =>
            {
                return await HandleAsync(new GetOrderNotificationsRequest(orderId), buyerOrderService);
            })
            .Produces<GetOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetOrderNotificationsRequest request, IBuyerOrderService buyerOrderService)
    {
        var response = new GetOrderNotificationsResponse(request.CorrelationId());
        var buyerId = _httpContextAccessor.HttpContext!.GetBuyerId();
        var notifications = await buyerOrderService.ListNotificationsAsync(buyerId, request.OrderId);
        response.OrderId = request.OrderId;
        response.Notifications = NotificationMapping.ToDto(notifications).ToList();
        return Results.Ok(response);
    }
}
