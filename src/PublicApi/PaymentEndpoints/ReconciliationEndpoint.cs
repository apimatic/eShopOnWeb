using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Services;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// Operator action: reconciles Finix transactions against eShop orders for a date range.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IFinixPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
                [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
                (DateTimeOffset? from, DateTimeOffset? to, IFinixPaymentService paymentService) =>
                {
                    if (!from.HasValue || !to.HasValue)
                    {
                        return Results.BadRequest(new
                        {
                            error = "Both from and to ISO-8601 date-times are required, for example ?from=2026-11-01T00:00:00Z&to=2026-11-30T23:59:59Z."
                        });
                    }

                    return await HandleAsync(new ReconciliationRequest { From = from.Value, To = to.Value }, paymentService);
                })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IFinixPaymentService paymentService)
    {
        var response = await paymentService.ReconcileAsync(request);
        return Results.Ok(response);
    }
}
