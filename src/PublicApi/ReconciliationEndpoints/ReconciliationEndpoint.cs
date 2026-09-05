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

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// An operator's report: PayPal's own record of transactions for a date range, lined up against this
/// application's payments, so a payment PayPal knows about and eShop does not — or the reverse — is
/// visible. The whole range is covered, not just the first page of it.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IPaymentProcessingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentProcessingService payments) =>
            {
                return await HandleAsync(new ReconciliationRequest(from, to), payments);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentProcessingService payments)
    {
        if (!TryRead(request.From, out var from) || !TryRead(request.To, out var to))
        {
            return Results.BadRequest(new
            {
                message = "from and to are required ISO-8601 date-times, e.g. 2026-09-01T00:00:00Z."
            });
        }

        if (to <= from)
        {
            return Results.BadRequest(new { message = "to must be later than from." });
        }

        var report = await payments.ReconcileAsync(from, to);
        return Results.Ok(ReconciliationResponse.Build(report, request.CorrelationId()));
    }

    private static bool TryRead(string? value, out DateTimeOffset parsed)
        => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
}

/// <summary>The date range an operator asked about, taken from the query string.</summary>
public class ReconciliationRequest : BaseRequest
{
    public ReconciliationRequest(string? from, string? to)
    {
        From = from;
        To = to;
    }

    public string? From { get; }
    public string? To { get; }
}
