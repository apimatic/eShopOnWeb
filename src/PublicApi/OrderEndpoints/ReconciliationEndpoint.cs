using System;
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, ClaimsPrincipal, CancellationToken>
{
    private readonly IPaymentService _payments;

    public ReconciliationEndpoint(IPaymentService payments)
    {
        _payments = payments;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset from, DateTimeOffset to, ClaimsPrincipal user, CancellationToken cancellationToken) =>
                    await HandleAsync(new ReconciliationRequest { From = from, To = to }, user, cancellationToken))
            .Produces<ReconciliationResult>()
            .WithTags("OrderEndpoints");
    }

    public async Task<IResult> HandleAsync(
        ReconciliationRequest request,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        _ = user;
        var result = await EndpointBudget.RunAsync(
            ct => _payments.GetReconciliationAsync(request.From, request.To, ct),
            cancellationToken);
        return Results.Ok(result);
    }
}
