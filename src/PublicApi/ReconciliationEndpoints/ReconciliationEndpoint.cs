using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

/// <summary>
/// Operator action: lists Finix's own transactions for a date range and matches them
/// against eShop orders, covering every page of the range.
/// </summary>
public sealed class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS,
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                return await PaymentEndpointHelpers.ExecuteAsync(async () =>
                {
                    if (!TryParse(from, out var fromDate) || !TryParse(to, out var toDate))
                    {
                        throw new PaymentOperationException(PaymentErrorKind.Validation,
                            "Query parameters 'from' and 'to' must be ISO-8601 date-times, for example 2026-11-17T00:00:00Z.");
                    }

                    var report = await paymentService.ReconcileAsync(fromDate, toDate, cancellationToken);
                    return Results.Ok(report);
                });
            })
            .Produces<ReconciliationReport>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status502BadGateway)
            .WithTags("ReconciliationEndpoints");
    }

    private static bool TryParse(string? value, out DateTimeOffset result)
    {
        result = default;
        return !string.IsNullOrWhiteSpace(value) &&
               DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind, out result);
    }
}
