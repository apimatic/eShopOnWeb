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

namespace Microsoft.eShopWeb.PublicApi.Payments.OrderEndpoints;

public class PlaceOrderRequest
{
    public List<PlaceOrderLine> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }
}

public class PlaceOrderLine
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShipToAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class PlaceOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
}

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper. Reuses the
/// existing order/order-item model. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service,
             CancellationToken ct) =>
            {
                var buyerId = user.GetBuyerId();
                var lines = (request.Items ?? new List<PlaceOrderLine>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var a = request.ShipToAddress;
                var shipTo = new Address(
                    string.IsNullOrWhiteSpace(a?.Street) ? "N/A" : a!.Street!,
                    string.IsNullOrWhiteSpace(a?.City) ? "N/A" : a!.City!,
                    string.IsNullOrWhiteSpace(a?.State) ? "N/A" : a!.State!,
                    string.IsNullOrWhiteSpace(a?.Country) ? "US" : a!.Country!,
                    string.IsNullOrWhiteSpace(a?.ZipCode) ? "00000" : a!.ZipCode!);

                var payment = await service.PlaceOrderAsync(buyerId, lines, shipTo, ct);

                var response = new PlaceOrderResponse
                {
                    OrderId = payment.OrderId,
                    Status = payment.Status.ToString(),
                    Total = payment.Amount,
                    Currency = payment.CurrencyCode
                };
                return Results.Created($"api/orders/{payment.OrderId}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
