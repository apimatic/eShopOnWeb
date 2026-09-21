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

/// <summary>Places an order from catalog items for the signed-in shopper. The order awaits payment.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                var lines = (request.Items ?? new()).Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
                var shipTo = request.ShipToAddress is null
                    ? null
                    : new ShippingAddressRequest(request.ShipToAddress.Street, request.ShipToAddress.City,
                        request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var orderId = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);
                return Results.Created($"/api/my-orders", new CreateOrderResponse(orderId));
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
