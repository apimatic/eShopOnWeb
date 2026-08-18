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
using Microsoft.eShopWeb.PublicApi.Notifications;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public record OrderLineDto(int CatalogItemId, int Quantity);
public record ShipToAddressDto(string? Street, string? City, string? State, string? Country, string? ZipCode);

public class CreateOrderRequest
{
    public List<OrderLineDto> Items { get; set; } = new();
    public ShipToAddressDto? ShipToAddress { get; set; }

    [JsonIgnore] public string? CallerId { get; set; }
    [JsonIgnore] public CancellationToken Ct { get; set; }
}

/// <summary><see cref="OrderId"/> is the top-level identifier so the flow can be driven end to end.</summary>
public record CreateOrderResponse(int OrderId);

/// <summary>
/// Place an order from catalog item ids and quantities, reusing the app's existing Order/OrderItem
/// model. The caller's identity comes from the token. The shopper is told their order was placed
/// (a messaging failure does not fail the placement).
/// </summary>
public class CreateOrderEndpoint : IEndpoint<IResult, CreateOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateOrderRequest request, ClaimsPrincipal user, IOrderNotificationService service, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (callerId is null)
                {
                    return Results.Unauthorized();
                }

                request.CallerId = callerId;
                request.Ct = ct;
                return await HandleAsync(request, service);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateOrderRequest request, IOrderNotificationService service)
    {
        if (request.Items is null || request.Items.Count == 0)
        {
            return Results.BadRequest(new { message = "At least one order line is required." });
        }
        if (request.Items.Any(i => i.Quantity <= 0))
        {
            return Results.BadRequest(new { message = "Every order line must have a quantity greater than zero." });
        }

        var address = BuildAddress(request.ShipToAddress);
        var lines = request.Items.Select(i => new OrderLine(i.CatalogItemId, i.Quantity)).ToList();

        try
        {
            var orderId = await service.PlaceOrderAsync(request.CallerId!, lines, address, request.Ct);
            return Results.Created($"api/orders/{orderId}", new CreateOrderResponse(orderId));
        }
        catch (UnknownCatalogItemException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static Address BuildAddress(ShipToAddressDto? dto) => new(
        string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street,
        string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City,
        string.IsNullOrWhiteSpace(dto?.State) ? "N/A" : dto!.State,
        string.IsNullOrWhiteSpace(dto?.Country) ? "N/A" : dto!.Country,
        string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode);
}
