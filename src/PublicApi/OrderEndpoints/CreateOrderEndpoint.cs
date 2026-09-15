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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// POST /api/orders — places an order for the signed-in shopper from catalog items. The order starts
/// awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(user);

                var lines = request.Items
                    .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                    .ToList();

                var address = request.ShipToAddress is { } a
                    ? new Address(a.Street, a.City, a.State ?? string.Empty, a.Country, a.ZipCode)
                    : new Address("N/A", "N/A", "N/A", "N/A", "00000");

                var order = await paymentService.PlaceOrderAsync(buyerId, lines, address, cancellationToken);

                return Results.Created($"api/orders/{order.Id}", new
                {
                    orderId = order.Id,
                    status = order.Status.ToString(),
                    total = order.Total(),
                    currency = order.Payment?.Currency,
                    order = order.ToDto()
                });
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("OrderEndpoints");
    }
}
