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

/// <summary>Places an order from catalog items for the signed-in shopper. Starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderCheckoutService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderCheckoutService service, HttpContext http) =>
                await HandleAsync(request, service, http))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentOrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderCheckoutService service, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var lines = request.Items.Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipping = request.ShipTo is null
            ? null
            : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City, request.ShipTo.State,
                request.ShipTo.Country, request.ShipTo.ZipCode);

        var order = await service.PlaceOrderAsync(buyerId, lines, shipping, http.RequestAborted);
        var gateway = http.RequestServices.GetService(typeof(IPaymentGateway)) as IPaymentGateway;

        return Results.Created($"api/orders/{order.Id}", new PlaceOrderResponse
        {
            OrderId = order.Id,
            PaymentStatus = order.PaymentStatus.ToString(),
            Total = order.Total(),
            Currency = gateway?.CurrencyCode ?? string.Empty
        });
    }
}
