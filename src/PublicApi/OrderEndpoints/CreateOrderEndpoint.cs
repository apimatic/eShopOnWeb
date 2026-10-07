using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ResultStatus = Ardalis.Result.ResultStatus;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Services;
using Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in user from catalog items
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, IOrderService>
{
    // The existing order model requires a shipping address; digital-only orders that send none get this one.
    private static readonly Address DigitalDeliveryAddress = new("Digital delivery", "n/a", "n/a", "n/a", "00000");

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService) =>
            {
                return await HandleAsync(request, user, orderService);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, IOrderService orderService)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId))
            return Results.Unauthorized();

        if (request.Items is null || request.Items.Count == 0)
            return DigitalFileErrors.Error(StatusCodes.Status400BadRequest, "items must contain at least one catalog item.");

        var address = DigitalDeliveryAddress;
        if (request.ShipToAddress is { } a)
        {
            if (string.IsNullOrWhiteSpace(a.Street) || string.IsNullOrWhiteSpace(a.City) ||
                string.IsNullOrWhiteSpace(a.Country) || string.IsNullOrWhiteSpace(a.ZipCode))
            {
                return DigitalFileErrors.Error(StatusCodes.Status400BadRequest,
                    "shipToAddress needs street, city, country and zipCode when it is given.");
            }
            address = new Address(a.Street, a.City, a.State ?? string.Empty, a.Country, a.ZipCode);
        }

        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();
        var result = await orderService.CreateOrderAsync(buyerId, lines, address);

        if (result.Status == ResultStatus.Invalid)
            return DigitalFileErrors.Error(StatusCodes.Status400BadRequest,
                string.Join(" ", result.ValidationErrors.Select(e => e.ErrorMessage)));
        if (result.Status == ResultStatus.NotFound)
            return DigitalFileErrors.Error(StatusCodes.Status400BadRequest, string.Join(" ", result.Errors));
        if (!result.IsSuccess)
            return DigitalFileErrors.Error(StatusCodes.Status500InternalServerError, "The order could not be placed.");

        var order = result.Value;
        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            OrderDate = order.OrderDate,
            Total = order.Total(),
            Items = order.OrderItems.Select(i => new OrderItemDto
            {
                CatalogItemId = i.ItemOrdered.CatalogItemId,
                ProductName = i.ItemOrdered.ProductName,
                UnitPrice = i.UnitPrice,
                Units = i.Units,
            }).ToList(),
        };
        return Results.Created($"api/orders/{order.Id}", response);
    }
}
