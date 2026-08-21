using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest, IOrderPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IPaymentSettings _paymentSettings;

    public PayOrderEndpoint(IHttpContextAccessor httpContextAccessor, IPaymentSettings paymentSettings)
    {
        _httpContextAccessor = httpContextAccessor;
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (int orderId, PayOrderRequest request, IOrderPaymentService paymentService) =>
            {
                request.OrderId = orderId;
                return await HandleAsync(request, paymentService);
            })
            .Produces<PayOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PayOrderRequest request, IOrderPaymentService paymentService)
    {
        var httpContext = _httpContextAccessor.HttpContext!;
        var buyerId = Caller.Name(httpContext);
        var order = await paymentService.PayAsync(
            request.OrderId,
            buyerId,
            request.Card?.ToCardDetails(),
            request.PaymentMethodId,
            httpContext.RequestAborted);

        return Results.Ok(new PayOrderResponse
        {
            Order = OrderDto.From(order, _paymentSettings.Currency)
        });
    }
}
