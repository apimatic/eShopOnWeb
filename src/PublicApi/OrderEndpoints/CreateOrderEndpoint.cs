using System;
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

public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, service, user);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service) =>
        HandleAsync(request, service, new ClaimsPrincipal());

    private async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user)
    {
        var unauthorized = user.RequireBuyerId(out var buyerId);
        if (unauthorized is not null)
        {
            return unauthorized;
        }

        var lines = (request.Items ?? new List<CreateOrderItemRequest>())
            .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
            .ToList();

        Address? shipTo = null;
        if (request.ShipTo is not null)
        {
            shipTo = new Address(
                request.ShipTo.Street,
                request.ShipTo.City,
                request.ShipTo.State,
                request.ShipTo.Country,
                request.ShipTo.ZipCode);
        }

        var created = await service.PlaceOrderAsync(buyerId, lines, shipTo);
        var dto = ShopperOrderDto.From(created);
        var response = new CreateOrderResponse
        {
            OrderId = dto.OrderId,
            Status = dto.Status,
            OrderDate = dto.OrderDate,
            Total = dto.Total,
            Items = dto.Items,
            Notifications = dto.Notifications
        };
        return Results.Created($"api/orders/{response.OrderId}", response);
    }
}

public class CreateOrderRequest
{
    public List<CreateOrderItemRequest>? Items { get; set; }
    public CreateOrderAddressRequest? ShipTo { get; set; }
}

public class CreateOrderItemRequest
{
    public int CatalogItemId { get; set; }
    public int Quantity { get; set; }
}

public class CreateOrderAddressRequest
{
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string ZipCode { get; set; } = string.Empty;
}

public class CreateOrderResponse
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public List<OrderNotificationDto> Notifications { get; set; } = new();
}
