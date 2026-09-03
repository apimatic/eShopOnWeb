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
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>Places an order from catalog items for the signed-in shopper. Starts awaiting payment.</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService svc, CancellationToken ct) =>
                await PaymentEndpointHelpers.Guarded(user, async buyerId =>
                {
                    var lines = (request.Items ?? new())
                        .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                        .ToList();
                    var a = request.ShipToAddress;
                    var address = new ShippingAddressInput(a.Street, a.City, a.State, a.Country, a.ZipCode);

                    var summary = await svc.PlaceOrderAsync(buyerId, lines, address, ct);
                    var response = new PlaceOrderResponse(summary.OrderId, summary.PaymentStatus, summary.Total, summary.CurrencyCode);
                    return Results.Created($"api/my-orders/{summary.OrderId}", response);
                }))
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
