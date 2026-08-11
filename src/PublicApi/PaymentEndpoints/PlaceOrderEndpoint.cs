using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Places an order from catalog items for the signed-in shopper. The order is created awaiting payment.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IOrderPaymentService service,
                CancellationToken ct) =>
            {
                var buyerId = PaymentEndpointHelpers.GetBuyerId(user);
                if (string.IsNullOrEmpty(buyerId)) return Results.Unauthorized();

                var lines = (request.Items ?? new())
                    .Select(i => new OrderLineRequest { CatalogItemId = i.CatalogItemId, Quantity = i.Quantity })
                    .ToList();

                var orderId = await service.PlaceOrderAsync(buyerId, lines, PaymentEndpointHelpers.ToAddress(request.ShipToAddress), ct);

                var summary = (await service.GetMyOrdersAsync(buyerId, ct)).FirstOrDefault(o => o.OrderId == orderId);
                var response = new PlaceOrderResponse
                {
                    OrderId = orderId,
                    Total = summary?.Total ?? 0m,
                    Currency = summary?.Currency ?? string.Empty,
                    PaymentStatus = summary?.PaymentStatus ?? "AwaitingPayment"
                };
                return Results.Created($"api/orders/{orderId}", response);
            })
            .Produces<PlaceOrderResponse>(StatusCodes.Status201Created)
            .WithTags("PaymentEndpoints");
    }
}
