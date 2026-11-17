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
/// Places an order from catalog items. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest>
{
    private readonly IPaymentService _paymentService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IPaymentService paymentService, IHttpContextAccessor httpContextAccessor)
    {
        _paymentService = paymentService;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (CreateOrderRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateOrderResponse>(201)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request)
    {
        var buyerId = _httpContextAccessor.HttpContext!.User.GetBuyerId();
        var order = await _paymentService.CreateOrderAsync(buyerId, request.ToInput());
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Order = PaymentDtoMapper.ToDto(order)
        };

        return Results.Created($"api/orders/{order.OrderId}", response);
    }
}
