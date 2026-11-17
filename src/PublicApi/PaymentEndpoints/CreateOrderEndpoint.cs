using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
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
                (CreateOrderRequest request, IPaymentService paymentService) =>
                {
                    return await HandleAsync(request, paymentService);
                })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.RequireBuyerId(_httpContextAccessor);
        var input = new CreateOrderInput
        {
            Items = request.Items
                .Select(item => new OrderLineInput
                {
                    CatalogItemId = item.CatalogItemId,
                    Quantity = item.Quantity
                })
                .ToList(),
            ShippingAddress = PaymentDtoMapper.ToAddress(request.ShippingAddress)
        };

        var order = await paymentService.CreateOrderAsync(buyerId, input,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Created($"api/orders/{order.Id}", new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = PaymentDtoMapper.ToResponse(order)
        });
    }
}
