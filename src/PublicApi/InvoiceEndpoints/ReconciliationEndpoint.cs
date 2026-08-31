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

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reconciliation report (operator action): lists the provider's own record of bills raised in a date
/// range and lines them up against what eShop believes it raised, so a bill the provider knows about and
/// eShop doesn't — or the reverse — is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint
{
    private readonly IInvoiceManagementService _invoiceService;

    public ReconciliationEndpoint(IInvoiceManagementService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) =>
            {
                return await HandleAsync(from, to);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public async Task<IResult> HandleAsync(string? from, string? to)
    {
        if (!TryParseIso(from, out var fromValue))
        {
            return Results.BadRequest("'from' must be an ISO-8601 date-time (e.g. 2026-08-31T00:00:00Z).");
        }

        if (!TryParseIso(to, out var toValue))
        {
            return Results.BadRequest("'to' must be an ISO-8601 date-time (e.g. 2026-08-31T23:59:59Z).");
        }

        if (toValue < fromValue)
        {
            return Results.BadRequest("'to' must not be earlier than 'from'.");
        }

        var report = await _invoiceService.ReconcileAsync(fromValue, toValue);
        return Results.Ok(ReconciliationResponse.FromReport(report));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
