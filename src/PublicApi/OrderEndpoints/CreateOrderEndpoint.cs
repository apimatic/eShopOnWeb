using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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

public class CreateOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShippingAddressDto? ShipToAddress { get; set; }

    [JsonIgnore]
    public string? BuyerId { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }
    public CreateOrderResponse() { }

    /// <summary>Identifier of the order that was created.</summary>
    public int OrderId { get; set; }

    public OrderSummaryDto Order { get; set; } = new();
}

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting
/// payment; the buyer comes from the token, not the request.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, HttpContext http, IOrderPaymentService service) =>
            {
                request.BuyerId = CallerIdentity.GetBuyerId(http);
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPaymentService service)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        try
        {
            var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
            var address = BuildAddress(request.ShipToAddress);

            var order = await service.PlaceOrderAsync(request.BuyerId, lines, address);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Order = OrderMapping.ToSummary(order)
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (Exception ex)
        {
            return PaymentProblem.ToResult(ex);
        }
    }

    private static Address BuildAddress(ShippingAddressDto? dto) => new(
        dto?.Street ?? "123 Main St.",
        dto?.City ?? "Kent",
        dto?.State ?? "OH",
        dto?.Country ?? "United States",
        dto?.ZipCode ?? "44240");
}
