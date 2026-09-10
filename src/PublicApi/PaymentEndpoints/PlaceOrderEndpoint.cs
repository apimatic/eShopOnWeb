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
/// awaiting payment; no money moves yet. Returns the new order's id as a top-level field.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                var items = (request.Items ?? new())
                    .Select(i => new PlaceOrderItem(i.CatalogItemId, i.Quantity))
                    .ToList();

                ShippingAddressInfo? shipTo = request.ShipToAddress is { } a
                    ? new ShippingAddressInfo(a.Street ?? "N/A", a.City ?? "N/A", a.State ?? "N/A", a.Country ?? "N/A", a.ZipCode ?? "N/A")
                    : null;

                var orderId = await paymentService.PlaceOrderAsync(buyerId, items, shipTo, cancellationToken);

                return Results.Created($"api/orders/{orderId}", new { orderId, status = "AwaitingPayment" });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
