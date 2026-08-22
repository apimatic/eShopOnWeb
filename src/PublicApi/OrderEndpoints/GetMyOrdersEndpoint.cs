using System.Collections.Generic;
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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext httpContext, IOrderPaymentService orderPaymentService) =>
            {
                var buyerId = httpContext.User.Identity?.Name
                    ?? httpContext.User.FindFirstValue(ClaimTypes.Name)
                    ?? string.Empty;
                return await HandleAsync(new GetMyOrdersRequest { BuyerId = buyerId }, orderPaymentService);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IOrderPaymentService orderPaymentService)
    {
        var orders = await orderPaymentService.ListMyOrdersAsync(request.BuyerId);
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(OrderDto.From).ToList()
        });
    }
}

public class GetMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class GetMyOrdersResponse : BaseResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
