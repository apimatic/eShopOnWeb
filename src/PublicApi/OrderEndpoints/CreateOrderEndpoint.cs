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
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the calling shopper from catalog items
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderService>
{
    private const int MaxQuantity = 1000;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, IOrderService orderService, ClaimsPrincipal user, HttpContext httpContext) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                request.CancellationToken = httpContext.RequestAborted;
                return await HandleAsync(request, orderService);
            })
            .Produces<CreateOrderResponse>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderService orderService)
    {
        var response = new CreateOrderResponse(request.CorrelationId());

        if (string.IsNullOrEmpty(request.BuyerId))
        {
            return Results.Unauthorized();
        }

        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var lines = request.Items.Select(item => new OrderLineRequest(item.CatalogItemId, item.Quantity)).ToList();
        var address = request.ShipToAddress is { } a
            ? new Address(a.Street, a.City, a.State, a.Country, a.ZipCode)
            : DigitalDeliveryAddress();

        Order order;
        try
        {
            order = await orderService.CreateOrderAsync(request.BuyerId, lines, address, request.CancellationToken);
        }
        catch (CatalogItemsNotFoundException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.Items)] = new[] { ex.Message },
            });
        }

        response.OrderId = order.Id;
        response.Total = order.Total();
        return Results.Ok(response);
    }

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0)
        {
            errors[nameof(request.Items)] = new[] { "At least one item is required." };
            return errors;
        }

        if (request.Items.Any(item => item.CatalogItemId <= 0))
        {
            errors["Items.CatalogItemId"] = new[] { "Catalog item ids must be positive." };
        }

        if (request.Items.Any(item => item.Quantity is <= 0 or > MaxQuantity))
        {
            errors["Items.Quantity"] = new[] { $"Quantities must be between 1 and {MaxQuantity}." };
        }

        if (request.ShipToAddress is { } a &&
            (string.IsNullOrWhiteSpace(a.Street) || string.IsNullOrWhiteSpace(a.City) ||
             string.IsNullOrWhiteSpace(a.Country) || string.IsNullOrWhiteSpace(a.ZipCode)))
        {
            errors[nameof(request.ShipToAddress)] = new[] { "Street, city, country and zip code are required when an address is given." };
        }

        return errors;
    }

    // The order model requires an address; digital editions are delivered by download instead.
    private static Address DigitalDeliveryAddress() =>
        new("Digital delivery", "N/A", "N/A", "N/A", "N/A");
}
