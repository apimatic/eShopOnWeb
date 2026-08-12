using System.Linq;
using System.Security.Claims;
using System.Threading;
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
/// Places an order from catalog items for the signed-in shopper, reusing the app's existing order
/// model, and tells the shopper it was placed.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint<IResult, PlaceOrderRequest, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (PlaceOrderRequest request, IOrderNotificationService service, ClaimsPrincipal user, CancellationToken ct) =>
            {
                var callerId = user.GetCallerId();
                if (string.IsNullOrEmpty(callerId))
                    return Results.Unauthorized();

                if (request?.Items is null || request.Items.Count == 0)
                    return Results.BadRequest(new { message = "An order must contain at least one item." });

                var lines = request.Items
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = BuildAddress(request.ShipToAddress);

                var order = await service.PlaceOrderAsync(callerId, lines, address, ct);
                var response = OrderResponseFactory.ToPlaceOrderResponse(order);
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address BuildAddress(ShipToAddressRequest? a)
    {
        // The notification feature does not require a real address; use the caller's when supplied and
        // sensible placeholders otherwise, so the existing (address-required) order model is satisfied.
        return new Address(
            string.IsNullOrWhiteSpace(a?.Street) ? "N/A" : a!.Street,
            string.IsNullOrWhiteSpace(a?.City) ? "N/A" : a!.City,
            string.IsNullOrWhiteSpace(a?.State) ? "N/A" : a!.State,
            string.IsNullOrWhiteSpace(a?.Country) ? "N/A" : a!.Country,
            string.IsNullOrWhiteSpace(a?.ZipCode) ? "00000" : a!.ZipCode);
    }

    public Task<IResult> HandleAsync(PlaceOrderRequest request, IOrderNotificationService service)
        => Task.FromResult<IResult>(Results.Empty);
}
