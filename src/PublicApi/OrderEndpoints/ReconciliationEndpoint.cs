using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.PayPal;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.OrderEndpoints;

/// <summary>
/// Operator report: PayPal's own record of transactions for a date range, lined up against eShop
/// orders, so a payment PayPal knows about that eShop doesn't — or the reverse — is visible. Covers
/// the whole range (all pages), not just the first page. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IReconciliationService service) =>
            {
                var fromDate = ParseIso(from, nameof(from));
                var toDate = ParseIso(to, nameof(to));
                if (toDate < fromDate)
                {
                    throw new PaymentOperationException("'to' must be on or after 'from'.");
                }
                var report = await service.ReconcileAsync(fromDate, toDate);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("OrderEndpoints");
    }

    // Required by IEndpoint; the real work is in the route lambda because it binds two query values.
    public Task<IResult> HandleAsync(IReconciliationService service) =>
        Task.FromResult(Results.BadRequest("Provide 'from' and 'to' ISO-8601 date-times."));

    private static DateTimeOffset ParseIso(string value, string field)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            throw new PaymentOperationException($"'{field}' must be an ISO-8601 date-time (e.g. 2026-08-01T00:00:00Z).");
        }
        return parsed;
    }
}
