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

public class ListMyOrdersEndpoint : IEndpoint<IResult, ListMyOrdersRequest, IShopperOrderService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopperOrderService service, ClaimsPrincipal user, HttpContext http) =>
            {
                return await HandleAsync(new ListMyOrdersRequest(), service, user, http.RequestAborted);
            })
            .Produces<ListMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(ListMyOrdersRequest request, IShopperOrderService service) =>
        HandleAsync(request, service, new ClaimsPrincipal(), default);

    private async Task<IResult> HandleAsync(
        ListMyOrdersRequest request,
        IShopperOrderService service,
        ClaimsPrincipal user,
        System.Threading.CancellationToken cancellationToken)
    {
        var buyerId = user.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var orders = await service.ListMineAsync(buyerId, cancellationToken);
        var response = new ListMyOrdersResponse(request.CorrelationId())
        {
            Orders = orders.Select(OrderDto.From).ToList()
        };
        return Results.Ok(response);
    }
}

public class ListMyOrdersRequest : BaseRequest
{
}

public class ListMyOrdersResponse : BaseResponse
{
    public ListMyOrdersResponse(System.Guid correlationId) : base(correlationId) { }
    public ListMyOrdersResponse() { }

    public System.Collections.Generic.List<OrderDto> Orders { get; set; } = new();
}
