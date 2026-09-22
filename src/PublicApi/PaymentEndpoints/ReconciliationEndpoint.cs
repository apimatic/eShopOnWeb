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
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction record
/// up against eShop orders over the whole [from,to] range (ISO-8601 date-times).
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (HttpContext http, System.Threading.CancellationToken ct) => await HandleAsync(http))
            .Produces<ReconciliationReport>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(HttpContext http)
    {
        try
        {
            if (!TryParseIso(http.Request.Query["from"], out var from))
                return PaymentEndpointHelpers.MapError(new PaymentOperationException(
                    PaymentOperationErrorKind.Validation, "Query parameter 'from' is required and must be an ISO-8601 date-time."));
            if (!TryParseIso(http.Request.Query["to"], out var to))
                return PaymentEndpointHelpers.MapError(new PaymentOperationException(
                    PaymentOperationErrorKind.Validation, "Query parameter 'to' is required and must be an ISO-8601 date-time."));

            var svc = http.RequestServices.GetRequiredService<IPaymentOrchestrationService>();
            var report = await svc.ReconcileAsync(from, to, http.RequestAborted);
            return Results.Ok(report);
        }
        catch (Exception ex) when (ex is PaymentOperationException or PaymentGatewayException)
        {
            return PaymentEndpointHelpers.MapError(ex);
        }
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
