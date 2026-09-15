using System;
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
/// Operator report: lists PayPal's own record of transactions for a date range and lines them up against
/// eShop orders, so a payment one side knows about and the other doesn't is visible. Covers the whole range
/// (all pages), not just the first page. <c>from</c>/<c>to</c> are ISO-8601 date-times. Administrator only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentOrchestrationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IPaymentOrchestrationService service, CancellationToken ct) =>
                await ExecuteAsync(new ReconciliationRequest(from, to), service, ct))
            .Produces<ReconciliationReport>()
            .WithTags("Orders");
    }

    public Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentOrchestrationService service) =>
        ExecuteAsync(request, service, CancellationToken.None);

    private static async Task<IResult> ExecuteAsync(ReconciliationRequest request, IPaymentOrchestrationService service, CancellationToken ct)
    {
        var result = await service.ReconcileAsync(request.From, request.To, ct);
        return result.ToHttpResult(Results.Ok);
    }
}
