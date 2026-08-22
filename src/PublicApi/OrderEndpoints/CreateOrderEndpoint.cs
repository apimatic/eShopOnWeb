using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutService>
{
    private readonly IPaymentSettings _paymentSettings;

    public CreateOrderEndpoint(IPaymentSettings paymentSettings)
    {
        _paymentSettings = paymentSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, ICheckoutService checkout) =>
            {
                request.BuyerId = user.GetBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutService checkout)
    {
        Address? address = null;
        if (request.ShipToAddress is not null)
        {
            address = new Address(
                request.ShipToAddress.Street ?? string.Empty,
                request.ShipToAddress.City ?? string.Empty,
                request.ShipToAddress.State ?? string.Empty,
                request.ShipToAddress.Country ?? "US",
                request.ShipToAddress.ZipCode ?? string.Empty);
        }

        var items = (request.Items ?? new List<CreateOrderLineRequest>())
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await checkout.PlaceOrderAsync(request.BuyerId, items, address);
        var payment = await checkout.GetPaymentAsync(order.Id);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.Map(order, payment, _paymentSettings.Currency)
        };

        return Results.Created($"api/orders/{order.Id}", response);
    }
}

public class CreateOrderRequest : BaseRequest
{
    public string BuyerId { get; set; } = string.Empty;
    public List<CreateOrderLineRequest> Items { get; set; } = new();
    public ShippingAddressRequest? ShipToAddress { get; set; }
}

public class CreateOrderLineRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class ShippingAddressRequest
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
