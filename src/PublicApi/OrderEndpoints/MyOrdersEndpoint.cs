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
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (ClaimsPrincipal user, IPaymentProcessingService service, CancellationToken ct) =>
            {
                var buyerId = CallerIdentity.BuyerId(user);
                var orders = await service.GetOrdersForBuyerAsync(buyerId, ct);
                var dtos = orders.Select(OrderPresentation.ToDto).ToList();
                return Results.Ok(dtos);
            })
            .Produces<List<OrderDto>>()
            .WithTags("OrderEndpoints");
    }
}
