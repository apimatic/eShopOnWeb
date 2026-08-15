using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>POST /api/orders — place an order awaiting payment from catalog items (shopper-scoped).</summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        async (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentService service, CancellationToken ct) =>
            {
                var buyerId = CallerContext.BuyerId(user);

                var lines = request.Items
                    .Select(i => new OrderLineInput(i.CatalogItemId, i.Quantity))
                    .ToList();

                ShippingAddressInput? address = request.ShipToAddress is null
                    ? null
                    : new ShippingAddressInput(request.ShipToAddress.Street, request.ShipToAddress.City,
                        request.ShipToAddress.State, request.ShipToAddress.Country, request.ShipToAddress.ZipCode);

                var orderId = await service.PlaceOrderAsync(buyerId, lines, address, ct);
                var view = await service.GetOrderAsync(buyerId, orderId, ct);

                return Results.Created($"api/orders/{orderId}", new PlaceOrderResponse { OrderId = orderId, Order = view });
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
