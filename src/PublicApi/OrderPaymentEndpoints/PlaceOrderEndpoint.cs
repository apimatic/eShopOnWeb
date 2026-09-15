using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// Places an order from catalog items. The caller's identity comes from the token; the order
/// starts awaiting payment. Responds with the created order (orderId as a top-level field).
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    private readonly PayPalSettings _settings;

    public PlaceOrderEndpoint(PayPalSettings settings) => _settings = settings;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.BuyerId = PaymentMapper.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var lines = request.Items
            .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
            .ToList();

        var shipTo = request.ShipToAddress is null
            ? null
            : new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
                request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, shipTo);
        var dto = PaymentMapper.ToOrderDto(order, _settings.Currency);
        return Results.Created($"api/orders/{order.Id}", dto);
    }
}
