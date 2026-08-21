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

/// <summary>GET /api/my-orders — the caller's own orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersQuery, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new MyOrdersQuery(PaymentUser.BuyerId(user), ct), service);
            })
            .Produces<MyOrdersResponse>()
            .WithTags("Orders");
    }

    public async Task<IResult> HandleAsync(MyOrdersQuery query, IOrderPaymentService service)
    {
        var orders = await service.GetOrdersForBuyerAsync(query.BuyerId, query.Ct);
        var response = new MyOrdersResponse
        {
            Orders = orders.Select(PaymentApiMapper.ToOrderSummaryDto).ToList()
        };
        return Results.Ok(response);
    }
}

public record MyOrdersQuery(string BuyerId, CancellationToken Ct);
