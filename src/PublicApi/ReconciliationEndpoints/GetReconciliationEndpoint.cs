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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class GetReconciliationEndpoint : IEndpoint<IResult, IReconciliationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string? from, string? to, IReconciliationService service, HttpContext http) =>
                await HandleAsync(from, to, service, http))
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IReconciliationService service) =>
        throw new InvalidOperationException("Use the route handler.");

    private async Task<IResult> HandleAsync(string? from, string? to, IReconciliationService service, HttpContext http)
    {
        if (!TryParseDate(from, out var fromDate))
        {
            throw new CheckoutException("'from' must be an ISO-8601 date-time.");
        }

        if (!TryParseDate(to, out var toDate))
        {
            throw new CheckoutException("'to' must be an ISO-8601 date-time.");
        }

        var report = await service.ReconcileAsync(fromDate, toDate, http.RequestAborted);
        return Results.Ok(new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            MatchedCount = report.MatchedCount,
            PayPalOnlyCount = report.PayPalOnlyCount,
            EshopOnlyCount = report.EshopOnlyCount,
            Rows = report.Rows
        });
    }

    private static bool TryParseDate(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
    }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int MatchedCount { get; set; }
    public int PayPalOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public System.Collections.Generic.IReadOnlyList<ApplicationCore.Payments.ReconciliationRow> Rows { get; set; }
        = System.Array.Empty<ApplicationCore.Payments.ReconciliationRow>();
}
