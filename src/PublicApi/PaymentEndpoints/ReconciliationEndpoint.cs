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
/// GET /api/reconciliation?from={from}&amp;to={to} — PayPal's transaction records for the range lined up
/// against eShop orders. Admin only. <c>from</c>/<c>to</c> are ISO-8601 date-times.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, EmptyRequest, IPaymentReconciliationService>
{
    private readonly IHttpContextAccessor _http;

    public ReconciliationEndpoint(IHttpContextAccessor http) => _http = http;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentReconciliationService service) =>
                await PaymentEndpointSupport.ExecuteAsync(async () =>
                {
                    if (!TryParseIso(from, out var fromDt) || !TryParseIso(to, out var toDt))
                        return Results.Json(new { error = "'from' and 'to' must be ISO-8601 date-times." },
                            statusCode: StatusCodes.Status400BadRequest);
                    if (toDt <= fromDt)
                        return Results.Json(new { error = "'to' must be after 'from'." },
                            statusCode: StatusCodes.Status400BadRequest);

                    var ct = PaymentEndpointSupport.RequestAborted(_http);
                    var report = await service.ReconcileAsync(fromDt, toDt, ct);
                    return Results.Ok(report);
                }))
            .WithTags("PaymentOrderEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }

    public Task<IResult> HandleAsync(EmptyRequest request, IPaymentReconciliationService service) =>
        Task.FromResult(Results.BadRequest());
}
