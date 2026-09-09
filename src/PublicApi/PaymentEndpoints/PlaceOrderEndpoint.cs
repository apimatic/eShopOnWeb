using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// POST /api/orders — places an order from catalog items for the signed-in shopper.
/// The order starts awaiting payment; no money is taken here.
/// </summary>
public class PlaceOrderEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                PlaceOrderRequest request,
                ClaimsPrincipal user,
                IPaymentOrderService service,
                CancellationToken ct) =>
            {
                try
                {
                    var buyerId = user.GetBuyerId();
                    var lines = (request.Items ?? new())
                        .Select(i => new OrderLineRequest(i.CatalogItemId, i.Quantity))
                        .ToList();

                    var order = await service.PlaceOrderAsync(buyerId, lines, request.ShipToAddress?.ToDomain(), ct);
                    return Results.Created($"api/orders/{order.Id}", new
                    {
                        orderId = order.Id,
                        order = OrderDto.From(order)
                    });
                }
                catch (Exception ex)
                {
                    return PaymentProblems.ToResult(ex);
                }
            })
            .Produces(StatusCodes.Status201Created)
            .WithTags("OrderPaymentEndpoints");
    }
}
