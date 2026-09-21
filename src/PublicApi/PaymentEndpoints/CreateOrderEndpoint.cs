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
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class CreateOrderRequest
{
    public List<OrderLineInput> Items { get; set; } = new();
    public AddressInput? ShipTo { get; set; }
}

public class OrderLineInput
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressInput
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public record CreateOrderResponse(int OrderId, string Status);

/// <summary>POST /api/orders — places an order from catalog items; it starts awaiting payment.</summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IOrderPaymentService service,
                ClaimsPrincipal user,
                CancellationToken ct) =>
            await PaymentApiSupport.ExecuteAsync(async () =>
            {
                var buyerId = PaymentApiSupport.RequireBuyerId(user);
                var lines = (request.Items ?? new())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();
                ShippingAddressInput? shipping = request.ShipTo is null
                    ? null
                    : new ShippingAddressInput(request.ShipTo.Street, request.ShipTo.City,
                        request.ShipTo.State, request.ShipTo.Country, request.ShipTo.ZipCode);

                var orderId = await service.PlaceOrderAsync(buyerId, lines, shipping, ct);
                return Results.Created($"api/my-orders/{orderId}", new CreateOrderResponse(orderId, "AwaitingPayment"));
            }))
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("Payments");
    }
}
