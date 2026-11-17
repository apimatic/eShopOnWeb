using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Payments;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.PaymentEndpoints;

public class ReconciliationEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IPaymentService paymentService, CancellationToken cancellationToken) =>
            {
                var fromDate = ParseDate(from, nameof(from));
                var toDate = ParseDate(to, nameof(to));
                var result = await paymentService.ReconcileAsync(fromDate, toDate, cancellationToken);
                return Results.Ok(result);
            })
            .Produces<ReconciliationResult>()
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .WithTags("ReconciliationEndpoints");
    }

    private static DateTimeOffset ParseDate(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw new PaymentOperationException(400, $"Query parameter '{parameterName}' must be an ISO-8601 date-time.");
        }

        return parsed;
    }
}
