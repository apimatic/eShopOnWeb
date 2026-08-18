using System;
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

/// <summary>
/// Places an order for the signed-in shopper from catalog items, reusing the app's existing order model. The
/// shopper is told their order was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, ClaimsPrincipal>
{
    private const string PlaceholderField = "N/A";

    private readonly IOrderNotificationService _service;

    public PlaceOrderEndpoint(IOrderNotificationService service)
    {
        _service = service;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user) =>
            {
                return await HandleAsync(request, user);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, ClaimsPrincipal user)
    {
        var buyerId = user.CallerId();
        if (string.IsNullOrEmpty(buyerId))
        {
            return Results.Unauthorized();
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest("An order must contain at least one item.");
        }

        var lines = request.Items
            .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
            .ToList();

        var address = BuildAddress(request.ShipToAddress);

        try
        {
            var order = await _service.PlaceOrderAsync(buyerId, lines, address);
            var response = new PlaceOrderResponse(request.CorrelationId())
            {
                OrderId = order.Id,
                Status = order.Status.ToString(),
                Total = order.Total()
            };
            return Results.Created($"api/orders/{order.Id}", response);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(ex.Message);
        }
    }

    private static Address BuildAddress(ShippingAddressDto? dto)
    {
        if (dto is null)
        {
            return new Address(PlaceholderField, PlaceholderField, PlaceholderField, PlaceholderField, PlaceholderField);
        }

        return new Address(
            Fallback(dto.Street),
            Fallback(dto.City),
            dto.State ?? string.Empty,
            Fallback(dto.Country),
            Fallback(dto.ZipCode));
    }

    private static string Fallback(string? value) =>
        string.IsNullOrWhiteSpace(value) ? PlaceholderField : value;
}
