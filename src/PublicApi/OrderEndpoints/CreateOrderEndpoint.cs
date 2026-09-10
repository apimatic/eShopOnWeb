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
using Microsoft.eShopWeb.ApplicationCore.Models.Payments;
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Places an order for the signed-in shopper from catalog items. The order starts awaiting payment; no money moves.
/// Returns the new order's id as a top-level <c>orderId</c> field.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateOrderRequest request, ClaimsPrincipal user, IOrderPaymentService orderPaymentService,
                IPaymentSettings paymentSettings, CancellationToken cancellationToken) =>
            {
                var buyerId = user.FindFirstValue(ClaimTypes.Name);
                if (string.IsNullOrEmpty(buyerId))
                {
                    return Results.Unauthorized();
                }
                if (request?.Items == null || request.Items.Count == 0)
                {
                    return Results.BadRequest(new { error = "An order must contain at least one item." });
                }

                var lines = request.Items.Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity)).ToList();
                var order = await orderPaymentService.PlaceOrderAsync(buyerId, lines, BuildAddress(request.ShippingAddress), cancellationToken);

                var dto = PaymentMapping.ToOrderDto(order, paymentSettings.Currency);
                return Results.Created($"api/orders/{order.Id}", new { orderId = order.Id, order = dto });
            })
            .Produces(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("OrderEndpoints");
    }

    private static Address BuildAddress(OrderAddressDto? dto)
    {
        // The domain requires a shipping address; when the caller omits one, use a neutral placeholder.
        return new Address(
            street: string.IsNullOrWhiteSpace(dto?.Street) ? "N/A" : dto!.Street!,
            city: string.IsNullOrWhiteSpace(dto?.City) ? "N/A" : dto!.City!,
            state: dto?.State ?? string.Empty,
            country: string.IsNullOrWhiteSpace(dto?.Country) ? "US" : dto!.Country!,
            zipcode: string.IsNullOrWhiteSpace(dto?.ZipCode) ? "00000" : dto!.ZipCode!);
    }
}
