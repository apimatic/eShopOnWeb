using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the caller from catalog items, with an optional gift message. The order is also created in
/// Square at the merchant's location; the gift message is kept only on the Square order.
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, ClaimsPrincipal, SquareOrderService>
{
    public const int MaxLines = 100;
    public const int MaxQuantity = 1000;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, SquareOrderService orders, HttpContext context) =>
            {
                return await HandleAsync(request, user, orders, context.RequestAborted);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .Produces<CreateOrderResponse>(StatusCodes.Status202Accepted)
            .ProducesValidationProblem()
            .WithTags("OrderEndpoints");
    }

    public Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, SquareOrderService orders) =>
        HandleAsync(request, user, orders, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateOrderRequest request, ClaimsPrincipal user, SquareOrderService orders,
        CancellationToken requestAborted)
    {
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

        var errors = Validate(request);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var command = new PlaceOrderCommand(
            request.Items!.Select(i => new PlaceOrderLine(i.CatalogItemId, i.Quantity)).ToList(),
            request.GiftMessage,
            request.ShipToAddress is { } a ? new PlaceOrderAddress(a.Street!, a.City!, a.State, a.Country!, a.ZipCode!) : null);

        using var deadline = SquareTimeouts.Deadline(requestAborted, SquareTimeouts.Request);
        PlacedOrder placed;
        try
        {
            placed = await orders.PlaceOrderAsync(buyerId, command, deadline.Token);
        }
        catch (OrderRequestException ex)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = [ex.Message] });
        }

        var response = new CreateOrderResponse(request.CorrelationId())
        {
            OrderId = placed.OrderId,
            SquareOrderId = placed.SquareOrderId,
            SquareStatus = placed.SquareStatus,
            GiftMessageSaved = placed.GiftMessageSaved,
            Warning = placed.Warning,
        };
        var location = $"api/my-orders/{placed.OrderId}";
        return placed.SquareStatus == SquareOrderStatus.Created && placed.Warning is null
            ? Results.Created(location, response)
            : Results.Json(response, statusCode: StatusCodes.Status202Accepted);
    }

    private static Dictionary<string, string[]> Validate(CreateOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Items is null || request.Items.Count == 0)
            errors["items"] = ["At least one item is required."];
        else if (request.Items.Count > MaxLines)
            errors["items"] = [$"At most {MaxLines} items are allowed."];
        else if (request.Items.Any(i => i.CatalogItemId <= 0 || i.Quantity < 1 || i.Quantity > MaxQuantity))
            errors["items"] = [$"Each item needs a catalogItemId and a quantity between 1 and {MaxQuantity}."];

        if (request.GiftMessage is { } message)
        {
            var trimmed = message.Trim();
            if (trimmed.Length > SquareGiftMessageStore.MaxLength)
                errors["giftMessage"] = [$"The gift message can be at most {SquareGiftMessageStore.MaxLength} characters."];
            else if (trimmed.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
                errors["giftMessage"] = ["The gift message contains invalid characters."];
        }

        if (request.ShipToAddress is { } address)
        {
            if (string.IsNullOrWhiteSpace(address.Street) || address.Street.Length > 180)
                errors["shipToAddress.street"] = ["Street is required (at most 180 characters)."];
            if (string.IsNullOrWhiteSpace(address.City) || address.City.Length > 100)
                errors["shipToAddress.city"] = ["City is required (at most 100 characters)."];
            if (address.State is { Length: > 60 })
                errors["shipToAddress.state"] = ["State can be at most 60 characters."];
            if (string.IsNullOrWhiteSpace(address.Country) || address.Country.Length > 90)
                errors["shipToAddress.country"] = ["Country is required (at most 90 characters)."];
            if (string.IsNullOrWhiteSpace(address.ZipCode) || address.ZipCode.Length > 18)
                errors["shipToAddress.zipCode"] = ["ZipCode is required (at most 18 characters)."];
        }
        return errors;
    }
}
