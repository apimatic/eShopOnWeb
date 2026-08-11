using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentApi;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order reuses the app's
/// existing Order/OrderItem model and starts awaiting payment.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                IOrderPaymentService orderPaymentService,
                IPaymentGateway gateway,
                ClaimsPrincipal user,
                CancellationToken cancellationToken) =>
            {
                var buyerId = user.GetBuyerId();

                var lines = (request.Items ?? new List<OrderLineDto>())
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var shipToAddress = (request.ShipToAddress ?? ShippingAddressDto.Default).ToAddress();

                var order = await orderPaymentService.PlaceOrderAsync(buyerId, lines, shipToAddress, cancellationToken);

                var response = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Order = PaymentViewMapper.ToView(order, gateway.Currency)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}

public class CreateOrderRequest
{
    public List<OrderLineDto>? Items { get; set; }
    public ShippingAddressDto? ShipToAddress { get; set; }
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }

    /// <summary>A placeholder used when the caller does not supply a shipping address.</summary>
    public static ShippingAddressDto Default => new()
    {
        Street = "123 Main St",
        City = "Redmond",
        State = "WA",
        Country = "US",
        ZipCode = "98052"
    };

    public Address ToAddress() => new(
        string.IsNullOrWhiteSpace(Street) ? "123 Main St" : Street!,
        string.IsNullOrWhiteSpace(City) ? "Redmond" : City!,
        string.IsNullOrWhiteSpace(State) ? "WA" : State!,
        string.IsNullOrWhiteSpace(Country) ? "US" : Country!,
        string.IsNullOrWhiteSpace(ZipCode) ? "98052" : ZipCode!);
}

public class CreateOrderResponse
{
    /// <summary>The identifier of the created order.</summary>
    public int OrderId { get; set; }

    public OrderView Order { get; set; } = new();
}
