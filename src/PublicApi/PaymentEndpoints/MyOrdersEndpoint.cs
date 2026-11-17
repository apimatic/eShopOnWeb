using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public MyOrdersEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async () =>
            {
                return await HandleAsync(new MyOrdersRequest());
            })
            .Produces<MyOrdersResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var orders = await _paymentService.GetMyOrdersAsync(buyerId);

        var response = new MyOrdersResponse(request.CorrelationId());
        response.Orders.AddRange(orders.Select(PaymentDtoMapper.ToDto));
        return Results.Ok(response);
    }
}
