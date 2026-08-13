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
using Microsoft.eShopWeb.PublicApi.NotificationEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>The signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IOrderNotificationService _service;

    public MyOrdersEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersRequest { CallerId = user.GetUserId() }, ct);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request) => HandleAsync(request, default);

    public async Task<IResult> HandleAsync(MyOrdersRequest request, CancellationToken ct)
    {
        var response = new MyOrdersResponse(request.CorrelationId());
        if (string.IsNullOrEmpty(request.CallerId)) return Results.Unauthorized();

        var views = await _service.GetMyOrdersAsync(request.CallerId, ct);
        response.Orders = views.Select(v => new MyOrderDto
        {
            OrderId = v.Order.Id,
            OrderDate = v.Order.OrderDate,
            Total = v.Order.Total(),
            Items = OrderSummaryDto.From(v.Order).Items,
            Notifications = v.Notifications.Select(NotificationDto.From).ToList(),
        }).ToList();

        return Results.Ok(response);
    }
}
