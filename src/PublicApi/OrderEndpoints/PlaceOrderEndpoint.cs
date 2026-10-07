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
/// Places an order for the caller from catalog items, priced from the catalog. The order starts awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderService>
{
    private const int MaxLines = 100;
    private const int MaxQuantity = 1000;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, ClaimsPrincipal user, IOrderService orderService, IPaymentGateway paymentGateway) =>
            {
                request.BuyerId = user.Identity?.Name ?? string.Empty;
                request.Currency = paymentGateway.Currency;
                return await HandleAsync(request, orderService);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderService orderService)
    {
        if (string.IsNullOrEmpty(request.BuyerId))
            return Results.Unauthorized();

        var errors = Validate(request);
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var address = request.ShipToAddress is { } a
            ? new Address(a.Street!.Trim(), a.City!.Trim(), a.State?.Trim() ?? string.Empty, a.Country!.Trim(), a.ZipCode!.Trim())
            : null;

        Order order;
        try
        {
            order = await orderService.CreateOrderAsync(request.BuyerId,
                request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList(), address);
        }
        catch (CatalogItemsNotFoundException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = new[] { ex.Message } });
        }

        var response = new PlaceOrderResponse(request.CorrelationId())
        {
            OrderId = order.Id,
            Order = OrderDtoMapper.ToSummary(order, request.Currency),
        };
        return Results.Created("api/my-orders", response);
    }

    private static Dictionary<string, string[]> Validate(PlaceOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        var items = request.Items?.Where(i => i is not null).ToList() ?? new List<PlaceOrderItem>();
        if (items.Count == 0)
            errors["items"] = new[] { "At least one item is required." };
        else if (items.Count > MaxLines)
            errors["items"] = new[] { $"An order can have at most {MaxLines} lines." };
        else
        {
            var itemErrors = new List<string>();
            if (items.Any(i => i.CatalogItemId <= 0))
                itemErrors.Add("Every item needs a valid catalogItemId.");
            if (items.Any(i => i.Quantity < 1 || i.Quantity > MaxQuantity))
                itemErrors.Add($"Every quantity must be between 1 and {MaxQuantity}.");
            else if (items.GroupBy(i => i.CatalogItemId).Any(g => g.Sum(i => i.Quantity) > MaxQuantity))
                itemErrors.Add($"The total quantity of one catalog item must not exceed {MaxQuantity}.");
            if (itemErrors.Count > 0)
                errors["items"] = itemErrors.ToArray();
        }

        if (request.ShipToAddress is { } address)
        {
            var missing = new List<string>();
            if (string.IsNullOrWhiteSpace(address.Street)) missing.Add("street");
            if (string.IsNullOrWhiteSpace(address.City)) missing.Add("city");
            if (string.IsNullOrWhiteSpace(address.Country)) missing.Add("country");
            if (string.IsNullOrWhiteSpace(address.ZipCode)) missing.Add("zipCode");
            if (missing.Count > 0)
                errors["shipToAddress"] = new[] { $"Missing: {string.Join(", ", missing)}." };
        }
        return errors;
    }
}
