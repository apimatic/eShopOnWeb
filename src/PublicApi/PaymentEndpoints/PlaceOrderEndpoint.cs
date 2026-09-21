using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — a signed-in shopper places an order from catalog items. The order starts
/// awaiting payment; amounts come from catalog prices. Returns the new order id.
/// </summary>
public class PlaceOrderEndpoint : PaymentEndpointBase, IEndpoint<IResult, CreateOrderRequest, IPaymentService>
{
    public PlaceOrderEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IPaymentService service) => await HandleAsync(request, service))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService service)
    {
        var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
        var orderId = await service.PlaceOrderAsync(BuyerId, lines, PaymentMappings.ToAddress(request.ShipToAddress), RequestAborted);
        return Results.Created($"api/orders/{orderId}",
            new CreateOrderResponse { OrderId = orderId, PaymentStatus = PaymentStatus.AwaitingPayment.ToString() });
    }
}
