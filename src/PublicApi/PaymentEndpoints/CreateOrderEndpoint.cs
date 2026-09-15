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
using Microsoft.eShopWeb.Infrastructure.Services.PayPal;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — place an order from catalog items for the signed-in shopper.
/// The order starts awaiting payment. Returns the new order id as a top-level field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderService orderService,
                IOptions<PayPalSettings> settings) =>
            {
                var buyerId = PaymentMapping.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }

                if (request.Items is null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { message = "At least one order item is required." });
                }

                var address = request.ShipToAddress is null
                    ? new Address("N/A", "N/A", "N/A", "N/A", "00000")
                    : new Address(
                        request.ShipToAddress.Street,
                        request.ShipToAddress.City,
                        request.ShipToAddress.State,
                        request.ShipToAddress.Country,
                        request.ShipToAddress.ZipCode);

                var items = request.Items.Select(i => new OrderItemRequest(i.CatalogItemId, i.Quantity));
                var order = await orderService.CreateOrderFromItemsAsync(buyerId, items, address);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    PaymentStatus = order.PaymentStatus.ToString(),
                    Total = order.Total(),
                    Currency = settings.Value.Currency ?? "USD"
                };

                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
