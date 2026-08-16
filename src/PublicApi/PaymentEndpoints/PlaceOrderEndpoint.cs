using System;
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

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class PlaceOrderRequest : BaseRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }

    /// <summary>Set from the authenticated token, never from the request body.</summary>
    [JsonIgnore]
    public string BuyerId { get; set; } = string.Empty;
}

public class PlaceOrderResponse : BaseResponse
{
    public PlaceOrderResponse(Guid correlationId) : base(correlationId) { }

    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<OrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order starts awaiting payment.
/// Reuses the existing Order/OrderItem model; the caller's identity comes from the token.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderPaymentService service) =>
            {
                var buyerId = user.BuyerId();
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();
                request.BuyerId = buyerId;
                return await HandleAsync(request, service);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderPaymentService service)
    {
        var response = new PlaceOrderResponse(request.CorrelationId());

        var lines = (request.Items ?? new List<OrderLineDto>())
            .Select(i => new OrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        var order = await service.PlaceOrderAsync(request.BuyerId, lines, ToAddress(request.ShipToAddress));

        response.OrderId = order.Id;
        response.Status = order.Status.ToString();
        response.Total = order.Total();
        response.Items = order.OrderItems.Select(i => new OrderItemDto
        {
            CatalogItemId = i.ItemOrdered.CatalogItemId,
            ProductName = i.ItemOrdered.ProductName,
            UnitPrice = i.UnitPrice,
            Units = i.Units
        }).ToList();

        return Results.Created($"api/orders/{order.Id}", response);
    }

    // Shipping address is not the focus of this flow; use the supplied address or a sensible placeholder.
    private static Address ToAddress(ShipToAddressDto? dto) => new(
        street: string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street!,
        city: string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City!,
        state: dto?.State ?? "N/A",
        country: string.IsNullOrWhiteSpace(dto?.Country) ? "N/A" : dto!.Country!,
        zipcode: string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode!);
}
