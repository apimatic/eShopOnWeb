using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;
using static Microsoft.eShopWeb.PublicApi.PaymentEndpoints.PaymentApiHelpers;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

/// <summary>
/// GET /api/reconciliation?from={from}&amp;to={to} — operator report lining PayPal's own transaction
/// records up against eShop orders over the whole range, so a payment one side knows and the other doesn't
/// is visible. from/to are ISO-8601 date-times. Restricted to the administrator role.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest>
{
    private readonly IPaymentService _paymentService;

    public ReconciliationEndpoint(IPaymentService paymentService) => _paymentService = paymentService;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to) => await HandleAsync(new ReconciliationRequest(from, to)))
            .Produces<ReconciliationReport>()
            .WithTags("PaymentEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request)
    {
        if (!TryParseIso(request.From, out var from))
        {
            return Results.Json(new { errors = new[] { "'from' must be an ISO-8601 date-time." } }, statusCode: StatusCodes.Status400BadRequest);
        }

        if (!TryParseIso(request.To, out var to))
        {
            return Results.Json(new { errors = new[] { "'to' must be an ISO-8601 date-time." } }, statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await _paymentService.ReconcileAsync(from, to);
        return ToHttp(result, report => Results.Ok(report));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result);
    }
}
