using System.Collections.Generic;
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

public class MyOrdersResponse : BaseResponse
{
    public List<OrderSummaryDto> Orders { get; set; } = new();
}

/// <summary>Returns the signed-in shopper's own orders, each showing where its notifications got to.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IOrderNotificationService service) => await HandleAsync(service))
            .Produces<MyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderNotificationService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var orders = await service.GetMyOrdersAsync(buyerId);

        var response = new MyOrdersResponse
        {
            Orders = orders.Select(OrderSummaryDto.From).ToList()
        };
        return Results.Ok(response);
    }
}
