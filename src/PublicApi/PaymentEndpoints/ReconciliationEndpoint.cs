using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator: reconciles PayPal's own transaction records against eShop orders over an ISO-8601 date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service) =>
                await HandleAsync(new ReconciliationRequest(from, to), service))
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, CancellationToken.None);
        return Results.Ok(report);
    }
}
