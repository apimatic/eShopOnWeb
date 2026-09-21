using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Returns the new order's identifier as a top-level <c>orderId</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderPaymentService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service, HttpContext http)
    {
        var buyerId = http.BuyerId();

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipTo is null
            ? null
            : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await service.PlaceOrderAsync(buyerId, lines, shipTo, http.RequestAborted);

        var currency = http.RequestServices.GetService(typeof(IPayPalGateway)) is IPayPalGateway gw ? gw.Currency : string.Empty;
        var response = new CreateOrderResponse(order.Id, order.Status.ToString(), order.Total(), currency);
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
