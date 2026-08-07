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
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.PaymentShared;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderPaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper. The order is
/// created awaiting payment; pay for it via POST /api/orders/{orderId}/pay.
/// </summary>
public class CreateOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                CreateOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CallerIdentity.GetBuyerId(user);

                var items = (request.Items ?? new List<OrderItemRequest>())
                    .Select(i => (i.CatalogItemId, i.Quantity));

                var order = await orderPaymentService.PlaceOrderAsync(
                    buyerId, items, MapAddress(request.ShipToAddress), cancellationToken);

                var response = new CreateOrderResponse(request.CorrelationId())
                {
                    OrderId = order.Id,
                    Order = OrderSummaryFactory.ToSummary(order)
                };
                return Results.Created($"api/orders/{order.Id}", response);
            })
            .Produces<CreateOrderResponse>(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }

    private static Address MapAddress(ShipToAddressDto? dto)
    {
        // Ship-to is not required to drive the payment flow; use a placeholder when omitted.
        return new Address(
            street: Value(dto?.Street, "N/A"),
            city: Value(dto?.City, "N/A"),
            state: Value(dto?.State, "N/A"),
            country: Value(dto?.Country, "US"),
            zipcode: Value(dto?.ZipCode, "00000"));
    }

    private static string Value(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
