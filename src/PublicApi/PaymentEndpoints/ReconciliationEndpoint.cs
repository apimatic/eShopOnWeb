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
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report listing PayPal's own record of
/// transactions for a date range, lined up against eShop orders, over the whole range (all pages).
/// <c>from</c> and <c>to</c> are ISO-8601 date-times. Administrator-only.
/// </summary>
public class ReconciliationEndpoint : PaymentEndpointBase, IEndpoint<IResult, ReconciliationRequest, IPaymentService>
{
    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor) { }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentService paymentService) =>
                await HandleAsync(new ReconciliationRequest { From = from, To = to }, paymentService))
            .WithTags("OrderPaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IPaymentService paymentService)
    {
        if (!TryParseIso(request.From, out var from) || !TryParseIso(request.To, out var to))
        {
            return Results.BadRequest(new { message = "'from' and 'to' must be ISO-8601 date-times, e.g. 2024-01-01T00:00:00Z." });
        }

        var report = await paymentService.ReconcileAsync(from, to, RequestAborted);
        return Results.Ok(report);
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
}
