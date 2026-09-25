using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public record MyOrdersRequest(string BuyerId, CancellationToken Ct);

/// <summary>GET /api/my-orders — the caller's orders with their payment state.</summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async (
                ClaimsPrincipal user, IOrderPaymentService service, CancellationToken ct) =>
                await HandleAsync(new MyOrdersRequest(user.BuyerId(), ct), service))
            .Produces<IReadOnlyList<OrderPaymentView>>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(MyOrdersRequest request, IOrderPaymentService service)
    {
        var orders = await service.GetMyOrdersAsync(request.BuyerId, request.Ct);
        return Results.Ok(orders);
    }
}
