using System;
using System.Collections.Generic;
using System.Linq;
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

public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderEndpoint.Request, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (Request request, HttpContext http) => await HandleAsync(request, http))
            .Produces<Response>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(Request request, HttpContext http)
    {
        var buyerId = http.GetBuyerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { error = "At least one catalog item is required." });
        }

        try
        {
            Address? address = request.ShipTo is null
                ? null
                : new Address(
                    request.ShipTo.Street ?? "N/A",
                    request.ShipTo.City ?? "N/A",
                    request.ShipTo.State ?? "N/A",
                    request.ShipTo.Country ?? "USA",
                    request.ShipTo.ZipCode ?? "00000");

            var lines = request.Items
                .Select(i => new CatalogOrderLine(i.CatalogItemId, i.Quantity))
                .ToList();
            var orders = http.GetRequired<IShopperOrderService>();
            var result = await orders.PlaceOrderAsync(buyerId, lines, address);
            var response = new Response
            {
                OrderId = result.Order.Id,
                Status = result.Status.ToString(),
                Notifications = result.Notifications.Select(NotificationDto.From).ToList()
            };
            return Results.Created($"api/orders/{result.Order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    public class Request
    {
        public List<Item> Items { get; set; } = new();
        public AddressDto? ShipTo { get; set; }
    }

    public class Item
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

    public class Response
    {
        public int OrderId { get; set; }
        public string Status { get; set; } = string.Empty;
        public List<NotificationDto> Notifications { get; set; } = new();
    }
}
