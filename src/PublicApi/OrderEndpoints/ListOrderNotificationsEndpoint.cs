using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.Extensions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class ListOrderNotificationsEndpoint : IEndpoint<IResult, ListOrderNotificationsRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/orders/{orderId}/notifications",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (int orderId, HttpContext http, IShopperOrderService service) =>
            {
                return await HandleAsync(new ListOrderNotificationsRequest
                {
                    OrderId = orderId,
                    BuyerId = http.User.GetBuyerId(),
                    IsAdministrator = http.User.IsAdministrator()
                }, service);
            })
            .Produces<ListOrderNotificationsResponse>()
            .Produces(StatusCodes.Status404NotFound)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListOrderNotificationsRequest request, IShopperOrderService service)
    {
        var notifications = await service.GetOrderNotificationsAsync(
            request.BuyerId,
            request.OrderId,
            request.IsAdministrator);

        var response = new ListOrderNotificationsResponse
        {
            OrderId = request.OrderId
        };
        response.Notifications.AddRange(notifications.Select(ListMyOrdersEndpoint.ToDto));
        return Results.Ok(response);
    }
}

public class ListOrderNotificationsRequest : BaseRequest
{
    public int OrderId { get; set; }
    public string BuyerId { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
}
