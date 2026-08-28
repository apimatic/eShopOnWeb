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

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items. No money moves here — the order starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IPaymentService paymentService, HttpContext context) =>
            {
                return await HandleAsync(request, paymentService, context);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IPaymentService paymentService,
        HttpContext context)
    {
        var buyerId = context.BuyerId();
        if (buyerId is null)
        {
            return Results.Unauthorized();
        }

        var address = request.ShipToAddress;
        var shipTo = new Address(
            address?.Street ?? "n/a",
            address?.City ?? "n/a",
            address?.State ?? "n/a",
            address?.Country ?? "n/a",
            address?.ZipCode ?? "n/a");

        var order = await paymentService.PlaceOrderAsync(
            buyerId,
            request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(),
            shipTo,
            context.RequestAborted);

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.OrderId,
            Order = order
        };

        return Results.Created($"api/orders/{order.OrderId}", response);
    }
}
