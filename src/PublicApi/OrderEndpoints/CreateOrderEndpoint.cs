using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.BuyerId = http.User.Identity?.Name;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity));
        var address = (request.ShipToAddress ?? new AddressDto()).ToAddress();

        var payment = await service.PlaceOrderAsync(request.BuyerId, lines, address);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = payment.OrderId,
            Payment = PaymentStateDto.From(payment)
        };

        return Results.Created($"api/orders/{payment.OrderId}", response);
    }
}
