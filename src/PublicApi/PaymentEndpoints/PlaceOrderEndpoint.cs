using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order starts
/// awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderPaymentService service, HttpContext http) =>
            {
                request.BuyerId = http.BuyerId();
                return await HandleAsync(request, service);
            })
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ValidationException("An order must contain at least one item.");
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = ToAddress(request.ShipToAddress);

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, shipTo);

        var response = OrderResponse.From(order);
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(ShippingAddressInput? input) =>
        input is null
            // Same placeholder the Web storefront checkout uses when no address is captured.
            ? new Address("123 Main St.", "Kent", "OH", "United States", "44240")
            : new Address(input.Street, input.City, input.State, input.Country, input.ZipCode);
}
