using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order from catalog items for the signed-in shopper.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IPaymentOrderService service,
                PayPalSettings settings,
                HttpContext http,
                System.Threading.CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();
                var shipTo = request.ShipTo is null
                    ? null
                    : new ShippingAddressRequest(request.ShipTo.Street, request.ShipTo.City,
                        request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

                var order = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);
                return Results.Created($"api/orders/{order.Id}",
                    OrderPaymentResponse.From(order, settings.Currency));
            })
            .Produces<OrderPaymentResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
