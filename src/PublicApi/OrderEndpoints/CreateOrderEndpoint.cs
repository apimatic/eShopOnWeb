using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ICheckoutOrderService>
{
    private readonly IOptions<PayPalSettings> _payPalSettings;

    public CreateOrderEndpoint(IOptions<PayPalSettings> payPalSettings)
    {
        _payPalSettings = payPalSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, HttpContext http, ICheckoutOrderService checkout) =>
            {
                request.BuyerId = http.RequireBuyerId();
                return await HandleAsync(request, checkout);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ICheckoutOrderService checkout)
    {
        var shipTo = request.ShipTo is null
            ? new Address("123 Main St", "Seattle", "WA", "US", "98101")
            : new Address(
                request.ShipTo.Street ?? "123 Main St",
                request.ShipTo.City ?? "Seattle",
                request.ShipTo.State ?? "WA",
                request.ShipTo.Country ?? "US",
                request.ShipTo.ZipCode ?? "98101");

        var lines = new List<CatalogOrderLine>();
        foreach (var item in request.Items ?? new List<CreateOrderItemRequest>())
        {
            lines.Add(new CatalogOrderLine(item.CatalogItemId, item.Quantity));
        }

        var order = await checkout.PlaceOrderAsync(request.BuyerId, lines, shipTo, default);
        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderDto.From(order, _payPalSettings.Value.Currency)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
