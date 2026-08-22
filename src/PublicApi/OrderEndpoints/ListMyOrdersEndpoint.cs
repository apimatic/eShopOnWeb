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

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse()
    {
        Orders = new();
    }

    public System.Collections.Generic.List<OrderResponse> Orders { get; set; }
}

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, ICheckoutPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ListMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ICheckoutPaymentService checkout) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), checkout);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(ListMyOrdersRequest request, ICheckoutPaymentService checkout)
    {
        var buyerId = _httpContextAccessor.HttpContext?.User.Identity?.Name ?? string.Empty;
        var orders = await checkout.ListBuyerOrdersAsync(buyerId, default);
        return Results.Ok(new ListMyOrdersResponse
        {
            Orders = orders.Select(OrderResponseMapper.Map).ToList()
        });
    }
}
