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

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public CancellationToken Cancellation { get; set; }
}

/// <summary>
/// Operator action: PayPal's own transactions for a date range, lined up against eShop orders so
/// mismatches in either direction are visible. Covers the whole range, not just the first page.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IOrderPaymentService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IOrderPaymentService service, CancellationToken ct) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to, Cancellation = ct }, service);
            })
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IOrderPaymentService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To, request.Cancellation);
        return Results.Ok(report);
    }
}
