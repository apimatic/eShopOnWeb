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
using Microsoft.eShopWeb.PublicApi.PaymentModels;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user,
                IOrderPaymentService paymentService,
                CancellationToken cancellationToken) =>
            {
                var buyerId = CurrentUser.BuyerId(user);
                var orders = await paymentService.GetMyOrdersAsync(buyerId, cancellationToken);

                return Results.Ok(new
                {
                    orders = orders
                        .OrderByDescending(o => o.OrderDate)
                        .Select(o => o.ToDto())
                        .ToList()
                });
            })
            .Produces(StatusCodes.Status200OK)
            .WithTags("OrderEndpoints");
    }
}
