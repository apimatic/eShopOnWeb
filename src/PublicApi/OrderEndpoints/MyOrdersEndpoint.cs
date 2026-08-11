using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>Lists the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (IOrderPaymentService service) => await HandleAsync(service))
            .Produces<List<OrderPaymentResponse>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(IOrderPaymentService service)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var orders = await service.GetOrdersForBuyerAsync(buyerId);
        return Results.Ok(orders.Select(PaymentApiMapper.ToResponse).ToList());
    }
}
