using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, string, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService paymentService) =>
                await HandleAsync(user.GetBuyerId(), paymentService))
            .Produces<List<OrderSummaryDto>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(string buyerId, IOrderPaymentService paymentService)
    {
        var orders = await paymentService.GetOrdersForBuyerAsync(buyerId);
        var dtos = orders
            .OrderByDescending(o => o.OrderDate)
            .Select(PaymentDtoMapper.ToDto)
            .ToList();
        return Results.Ok(dtos);
    }
}
