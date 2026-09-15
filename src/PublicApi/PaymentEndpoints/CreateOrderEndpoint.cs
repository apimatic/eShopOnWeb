using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class OrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressRequest
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderRequest
{
    public List<OrderLineRequest> Items { get; set; } = new();
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting
/// payment; no money is moved here. Reuses the app's existing order/order-item model.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IPaymentService paymentService,
                IPaymentSettings settings,
                CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                if (buyerId is null) return Results.Unauthorized();

                var lines = (request.Items ?? new List<OrderLineRequest>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = ToAddress(request.ShipToAddress);

                var result = await paymentService.PlaceOrderAsync(buyerId, lines, address, ct);
                if (!result.IsSuccess) return result.ToProblem();

                var order = result.Value;
                var dto = order.ToDto(settings.Currency);
                return Results.Created($"api/orders/{order.Id}", new { orderId = order.Id, order = dto });
            })
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    // The order aggregate requires a ship-to address; default missing fields to a placeholder so
    // the additive payment surface stays usable without collecting shipping here.
    private static Address ToAddress(ShipToAddressRequest? a) => new(
        street: a?.Street ?? "N/A",
        city: a?.City ?? "N/A",
        state: a?.State ?? "N/A",
        country: a?.Country ?? "N/A",
        zipcode: a?.ZipCode ?? "N/A");
}
