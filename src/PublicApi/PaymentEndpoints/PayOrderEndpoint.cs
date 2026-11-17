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
/// Authorizes the order total, placing a hold on the shopper's card.
/// </summary>
public class PayOrderEndpoint : IEndpoint<IResult, PayOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public PayOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders/{orderId:int}/pay",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (int orderId, PayOrderRequest request) =>
            {
                return await HandleAsync(orderId, request);
            })
            .Produces<PayOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(PayOrderRequest request) => HandleAsync(0, request);

    public async Task<IResult> HandleAsync(int orderId, PayOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var input = new PayOrderInput(request.ResolveCard(), request.ResolvePaymentMethodId());
        var order = await _paymentService.PayOrderAsync(buyerId, orderId, input);

        var response = new PayOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Payment = order.Payment is null ? null : PaymentDtoMapper.ToDto(order.Payment),
            Order = PaymentDtoMapper.ToDto(order)
        };

        return Results.Ok(response);
    }
}
