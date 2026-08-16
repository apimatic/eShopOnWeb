using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>Lists the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, IPaymentService>
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
            (IPaymentService paymentService) => await HandleAsync(paymentService))
            .Produces<MyOrdersResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(IPaymentService paymentService)
    {
        var buyerId = _httpContextAccessor.HttpContext?.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        var result = await paymentService.GetOrdersForBuyerAsync(buyerId);
        if (!result.IsSuccess)
        {
            return result.ToProblem();
        }

        var response = new MyOrdersResponse
        {
            Orders = result.Value.Select(o => o.ToResponse()).ToList()
        };
        return Results.Ok(response);
    }
}
