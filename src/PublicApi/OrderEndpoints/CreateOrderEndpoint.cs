using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }

    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    /// <summary>Identifier of the created order, returned as a top-level field to drive the flow.</summary>
    public int OrderId { get; set; }
    public OrderPaymentStateDto Order { get; set; } = new();
}

/// <summary>
/// Places an order for the authenticated shopper from catalog items. The order starts awaiting
/// payment; pay for it via <c>POST /api/orders/{orderId}/pay</c>.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                request.BuyerId = user.GetBuyerId() ?? string.Empty;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints")
            .WithSummary("Place an order awaiting payment");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var shipTo = ToAddress(request.ShipToAddress);

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, shipTo);

        var response = new CreateOrderResponse
        {
            OrderId = order.Id,
            Order = OrderPaymentStateDto.From(order)
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }

    private static Address ToAddress(ShipToAddressDto? dto)
    {
        // ShipToAddress is required by the order model; use the caller's address when supplied,
        // otherwise a clearly-marked placeholder (this API is about payment, not fulfilment).
        return new Address(
            street: Fallback(dto?.Street, "N/A"),
            city: Fallback(dto?.City, "N/A"),
            state: dto?.State ?? "N/A",
            country: Fallback(dto?.Country, "US"),
            zipcode: Fallback(dto?.ZipCode, "00000"));
    }

    private static string Fallback(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value!;
}
