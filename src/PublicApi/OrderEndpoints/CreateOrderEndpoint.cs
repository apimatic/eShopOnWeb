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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — a logged-in shopper places an order from catalog items. Prices come from the
/// catalog; the order starts awaiting payment. Returns the new order's id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (PlaceOrderRequest request, ClaimsPrincipal user, IPaymentProcessingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();

                var order = await service.PlaceOrderAsync(buyerId, lines, request.ShipTo?.ToAddress(), ct);

                var dto = OrderPresentation.ToDto(order);
                return Results.Created($"api/orders/{dto.OrderId}", dto);
            })
            .Produces<OrderDto>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
