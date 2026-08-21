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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Lists the signed-in shopper's own orders together with each order's payment state.
/// </summary>
public class MyOrdersEndpoint : IEndpoint<IResult, MyOrdersRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/my-orders",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (ClaimsPrincipal user, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new MyOrdersRequest(user.Identity!.Name!), service, ct))
            .Produces<IReadOnlyList<OrderSummaryView>>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(MyOrdersRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(MyOrdersRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.GetMyOrdersAsync(request.BuyerId, ct);
        return result.ToHttpResult(Results.Ok);
    }
}
