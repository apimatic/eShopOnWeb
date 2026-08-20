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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext httpContext, IOrderNotificationService service) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(httpContext.User.Identity?.Name), service);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, IOrderNotificationService service)
    {
        if (string.IsNullOrWhiteSpace(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.GetMyOrdersAsync(request.BuyerId);
        var response = new ListMyOrdersResponse(request.CorrelationId());
        response.Orders.AddRange(orders.Select(o => new OrderSummaryDto
        {
            OrderId = o.Order.Id,
            Status = o.Order.Status.ToString(),
            OrderDate = o.Order.OrderDate,
            Total = o.Order.Total(),
            Notifications = o.Notifications.Select(OrderNotificationDto.From).ToList()
        }));

        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
    public ListMyOrdersRequest(string? buyerId)
    {
        BuyerId = buyerId;
    }

    public string? BuyerId { get; }
}
