using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the authenticated shopper directly from catalog item ids and quantities,
/// reusing the app's existing order/order-item model. The caller's identity comes from the token.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderPlacementService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderPlacementService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(buyerId))
                    return Results.Unauthorized();

                request.BuyerId = buyerId;
                return await HandleAsync(request, service, ct);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderPlacementService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            var address = MapAddress(request.ShipToAddress);
            var lines = (request.Items ?? new List<OrderItemDto>())
                .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                .ToList();

            var order = await service.PlaceOrderAsync(request.BuyerId, address, lines, ct);

            var response = new CreateOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Total = order.Total(),
                ItemCount = order.OrderItems.Sum(oi => oi.Units),
            };
            return Results.Created($"api/orders/{order.Id}", response);
        });
    }

    private static Address MapAddress(AddressDto? dto) =>
        dto is null
            ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
            : new Address(dto.Street ?? "N/A", dto.City ?? "N/A", dto.State ?? "N/A", dto.Country ?? "N/A", dto.ZipCode ?? "00000");
}

public class CreateOrderRequest : BaseRequest
{
    public List<OrderItemDto> Items { get; set; } = new();
    public AddressDto? ShipToAddress { get; set; }

    // Server-populated from the token; never bound from the request body.
    [JsonIgnore] public string BuyerId { get; set; } = string.Empty;
}

public class OrderItemDto
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class AddressDto
{
    public string? Street { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Country { get; set; }
    public string? ZipCode { get; set; }
}

public class CreateOrderResponse : BaseResponse
{
    public CreateOrderResponse(Guid correlationId) : base(correlationId) { }

    public CreateOrderResponse() { }

    public int OrderId { get; set; }
    public decimal Total { get; set; }
    public int ItemCount { get; set; }
}
