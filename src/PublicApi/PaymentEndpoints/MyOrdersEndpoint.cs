using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Configuration;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/my-orders — the caller's orders with their payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IOrderPaymentService orderPaymentService,
                PayPalSettings settings,
                CancellationToken cancellationToken) =>
            await PaymentProblem.ExecuteAsync(async () =>
            {
                var buyerId = user.GetBuyerId();
                var orders = await orderPaymentService.GetMyOrdersAsync(buyerId, cancellationToken);

                var result = orders
                    .Select(o => OrderSummaryDto.From(o.Order, o.Payment, settings.Currency))
                    .ToList();

                return Results.Ok(result);
            }))
            .Produces<System.Collections.Generic.List<OrderSummaryDto>>()
            .WithTags("OrderEndpoints");
    }
}
