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

/// <summary>Operator report: PayPal's transactions for a date range lined up against eShop orders.</summary>
public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                       AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService svc, CancellationToken ct) =>
                await PaymentEndpointHelpers.Guarded(async () =>
                {
                    if (to < from)
                        return PaymentEndpointHelpers.MapException(
                            new Microsoft.eShopWeb.ApplicationCore.Payments.InvalidPaymentOperationException("'to' must be on or after 'from'."));
                    return Results.Ok(await svc.ReconcileAsync(from, to, ct));
                }))
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }
}
