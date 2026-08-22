using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class GetMyOrdersRequest : BaseRequest
{
}

public class GetMyOrdersResponse : BaseResponse
{
    public GetMyOrdersResponse(Guid correlationId) : base(correlationId) { }
    public GetMyOrdersResponse() { }

    public List<ShopOrderResponse> Orders { get; set; } = new();
}

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, IShopOrderService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public GetMyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (IShopOrderService orders) =>
            {
                return await HandleAsync(new GetMyOrdersRequest(), orders);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, IShopOrderService orders)
    {
        var buyerId = BuyerIdentity.Require(_httpContextAccessor);
        var result = await orders.ListMineAsync(buyerId, _httpContextAccessor.HttpContext?.RequestAborted ?? default);
        var response = new GetMyOrdersResponse(request.CorrelationId())
        {
            Orders = result.Select(o => ShopOrderResponse.From(o, request.CorrelationId())).ToList()
        };
        return Results.Ok(response);
    }
}
