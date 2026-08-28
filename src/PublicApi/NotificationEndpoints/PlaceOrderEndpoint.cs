using System.Linq;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Messaging;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public sealed class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                HttpContext context,
                OrderNotificationService service,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    if (request.Items is null || request.Address is null)
                    {
                        return Results.BadRequest(new { error = "Items and a shipping address are required." });
                    }

                    var orderId = await service.PlaceOrderAsync(
                        context.User.Identity!.Name!,
                        request.Items.Select(x => new OrderLineInput(x.CatalogItemId, x.Quantity)).ToList(),
                        new ShippingAddressInput(
                            request.Address.Street,
                            request.Address.City,
                            request.Address.State,
                            request.Address.Country,
                            request.Address.ZipCode),
                        cancellationToken);
                    return Results.Created($"/api/orders/{orderId}", new { orderId });
                }
                catch (OrderRequestValidationException ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            })
            .WithTags("Orders")
            .Produces(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest);
    }
}
