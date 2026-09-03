using System;
using System.Globalization;
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
/// Operator action: a reconciliation report listing PayPal's own record of transactions for a date
/// range and lining them up against eShop orders. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, string, string, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IReconciliationService service) =>
                await HandleAsync(from!, to!, service))
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public Task<IResult> HandleAsync(string? from, string? to, IReconciliationService service) =>
        PaymentApiHelpers.RunAsync(async () =>
        {
            if (!TryParseIso(from, out var fromDate))
                return Results.BadRequest("'from' is required and must be an ISO-8601 date-time.");
            if (!TryParseIso(to, out var toDate))
                return Results.BadRequest("'to' is required and must be an ISO-8601 date-time.");
            if (fromDate > toDate)
                return Results.BadRequest("'from' must not be later than 'to'.");

            var report = await service.BuildReportAsync(fromDate, toDate);
            return Results.Ok(report);
        });

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
