using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public MyOrdersEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
                [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new MyOrdersRequest(), user, cancellationToken))
            .Produces<IReadOnlyList<PaymentView>>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        MyOrdersRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        _ = request;
        var buyerId = EndpointIdentity.GetBuyerId(user);
        var result = await EndpointBudget.RunAsync(
            ct => _payments.GetMyOrdersAsync(buyerId, ct),
            cancellationToken);
        return Results.Ok(result);
    }
}

public sealed class MyOrdersRequest
{
}
