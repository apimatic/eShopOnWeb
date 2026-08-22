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

public class GetMyOrdersEndpoint : IEndpoint<IResult, GetMyOrdersRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public GetMyOrdersEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                return await HandleAsync(new GetMyOrdersRequest { BuyerId = user.GetBuyerId() }, checkout);
            })
            .Produces<GetMyOrdersResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(GetMyOrdersRequest request, ICheckoutService checkout)
    {
        var orders = await checkout.GetMyOrdersAsync(request.BuyerId);
        return Results.Ok(new GetMyOrdersResponse
        {
            Orders = orders.Select(o => OrderDtoMapper.Map(o.Order, o.Payment, _paymentSettings.Currency)).ToList()
        });
    }
}

public class GetMyOrdersRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
}

public class GetMyOrdersResponse
{
    public List<OrderDto> Orders { get; set; } = new();
}
