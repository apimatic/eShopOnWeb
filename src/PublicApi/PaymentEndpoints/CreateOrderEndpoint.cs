using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog item ids and quantities. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IFinixPaymentService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (CreateOrderRequest request, IFinixPaymentService paymentService) =>
                {
                    return await HandleAsync(request, paymentService);
                })
            .Produces<OrderDto>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IFinixPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext);
        var order = await paymentService.CreateOrderAsync(buyerId, request);
        return Results.Created($"api/orders/{order.OrderId}", order);
    }
}
