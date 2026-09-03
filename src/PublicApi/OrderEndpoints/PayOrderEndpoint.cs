using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, ICheckoutService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, ICheckoutService checkout) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, checkout);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, ICheckoutService checkoutService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var order = await checkoutService.PayAsync(
            new PayOrderCommand(
                request.OrderId,
                HttpCaller.RequireUserName(httpContext),
                request.Card?.ToDetails(),
                request.PaymentMethodId),
            httpContext.RequestAborted);

        return Results.Ok(new PayOrderResponse(request.CorrelationId()) { Order = OrderDto.From(order) });
    }
}
