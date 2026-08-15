using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public record OrderLineInput(int CatalogItemId, int Quantity);

public class ShippingAddressInput
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "Unspecified" : Street,
        string.IsNullOrWhiteSpace(City) ? "Unspecified" : City,
        State ?? "Unspecified",
        string.IsNullOrWhiteSpace(Country) ? "US" : Country,
        string.IsNullOrWhiteSpace(ZipCode) ? "00000" : ZipCode);
}

public class PlaceOrderRequest
{
    public List<OrderLineInput> Items { get; set; } = new();
    public ShippingAddressInput? ShippingAddress { get; set; }
}

public record PlaceOrderResponse(int OrderId, OrderView Order);

/// <summary>
/// Places an order for the signed-in shopper from catalog item ids and quantities. The order starts
/// awaiting payment. Reuses the existing Order/OrderItem model.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            [SwaggerOperation(Summary = "Place an order awaiting payment", Tags = new[] { "OrderEndpoints" })]
            async (PlaceOrderRequest request, IOrderService orderService, IPaymentConfiguration config,
                   HttpContext http, CancellationToken ct) =>
            {
                var buyerId = http.User.GetBuyerId();
                var lines = (request.Items ?? new List<OrderLineInput>())
                    .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = (request.ShippingAddress ?? new ShippingAddressInput()).ToAddress();
                var order = await orderService.CreateOrderAsync(buyerId, lines, address);

                var response = new PlaceOrderResponse(order.Id, PaymentResponseFactory.MapOrder(order, config.Currency));
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
