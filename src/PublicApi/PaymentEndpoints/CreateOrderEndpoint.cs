using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// awaiting payment; no money is taken here. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderApiRequest request, IPaymentService service, ClaimsPrincipal user,
                CancellationToken ct) =>
            {
                var items = request.Items
                    .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
                    .ToList();
                var shipTo = request.ShipTo is null
                    ? null
                    : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City,
                        request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

                var orderId = await service.PlaceOrderAsync(user.BuyerId(), items, shipTo, ct);
                return Results.Created($"/api/orders/{orderId}", new { orderId });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
