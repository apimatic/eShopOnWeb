using System.Linq;
using System.Security.Claims;
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
/// Places an order from catalog items for the signed-in shopper. The order starts
/// in a state awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    private readonly IPaymentGateway _gateway;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateOrderEndpoint(IPaymentGateway gateway, IHttpContextAccessor httpContextAccessor)
    {
        _gateway = gateway;
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
            .Produces<CreateOrderResponse>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService)
    {
        var buyerId = PaymentEndpointHelpers.GetBuyerId(_httpContextAccessor.HttpContext?.User ??
            new ClaimsPrincipal());
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        var input = new PlaceOrderInput
        {
            Items = request.Items.Select(i => new OrderItemInput
            {
                CatalogItemId = i.CatalogItemId,
                Quantity = i.Quantity
            }).ToList(),
            ShipToAddress = new AddressInput
            {
                Street = request.ShipToAddress?.Street ?? string.Empty,
                City = request.ShipToAddress?.City ?? string.Empty,
                State = request.ShipToAddress?.State ?? string.Empty,
                Country = request.ShipToAddress?.Country ?? string.Empty,
                ZipCode = request.ShipToAddress?.ZipCode ?? string.Empty
            }
        };

        var result = await paymentService.PlaceOrderAsync(buyerId, input, default);
        if (!result.Succeeded)
        {
            return PaymentEndpointHelpers.FromError(result.Error!);
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = result.OrderId,
            Status = "AwaitingPayment",
            Total = result.Total,
            Currency = _gateway.Currency
        };

        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}



