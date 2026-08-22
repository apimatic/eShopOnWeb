using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    private readonly PayPalSettings _payPalSettings;

    public CreateOrderEndpoint(PayPalSettings payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ICheckoutService checkout, ClaimsPrincipal user) =>
            {
                var buyerId = user.Identity?.Name;
                if (string.IsNullOrWhiteSpace(buyerId))
                {
                    return Results.Unauthorized();
                }

                var items = (request.Items ?? new List<CreateOrderItemRequest>())
                    .Select(i => new OrderCatalogItem(i.CatalogItemId, i.Quantity))
                    .ToList();

                Address? address = null;
                if (request.ShipToAddress is not null)
                {
                    address = new Address(
                        request.ShipToAddress.Street ?? "123 Main St.",
                        request.ShipToAddress.City ?? "Anytown",
                        request.ShipToAddress.State ?? "CA",
                        request.ShipToAddress.Country ?? "US",
                        request.ShipToAddress.ZipCode ?? "12345");
                }

                var order = await checkout.PlaceOrderAsync(buyerId, items, address);
                var body = new CreateOrderResponse
                {
                    OrderId = order.Id,
                    Order = OrderDtoMapper.From(order, _payPalSettings.Currency)
                };
                return Results.Created($"api/orders/{order.Id}", body);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout) =>
        throw new System.NotSupportedException("Use the route handler.");
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public ShipToAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderItemRequest
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

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public OrderDto Order { get; set; } = new();
}
