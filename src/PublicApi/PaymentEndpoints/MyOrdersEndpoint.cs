using System.Collections.Generic;
using System.Linq;
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

public record MyOrdersQuery(string BuyerId, CancellationToken Ct);

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersQuery, IPaymentApplicationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (HttpContext http, IPaymentApplicationService service) =>
            {
                var buyerId = CallerContext.GetBuyerId(http);
                if (buyerId is null) return Results.Unauthorized();
                return await HandleAsync(new MyOrdersQuery(buyerId, http.RequestAborted), service);
            })
            .Produces<IReadOnlyList<MyOrderDto>>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(MyOrdersQuery query, IPaymentApplicationService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(query.BuyerId, query.Ct);
        var dtos = orders
            .OrderByDescending(o => o.Order.OrderDate)
            .Select(o => PaymentMapper.ToDto(o.Order, o.Payment))
            .ToList();
        return Results.Ok(dtos);
    }
}
