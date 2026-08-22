using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public ReconciliationReport Report { get; set; } = new();
}

public class ReconciliationEndpoint : IEndpoint<IResult, string, IPaymentReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IPaymentReconciliationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(string from, IPaymentReconciliationService service) =>
        throw new NotSupportedException();

    private async Task<IResult> HandleAsync(string from, string to, IPaymentReconciliationService service)
    {
        if (!DateTimeOffset.TryParse(from, out var fromDate))
        {
            throw new CheckoutException(400, "`from` must be an ISO-8601 date-time.");
        }

        if (!DateTimeOffset.TryParse(to, out var toDate))
        {
            throw new CheckoutException(400, "`to` must be an ISO-8601 date-time.");
        }

        var report = await service.ReconcileAsync(fromDate, toDate);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            Report = report
        });
    }
}
