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
            async (string? from, string? to, IReconciliationService reconciliation) =>
            {
                if (!TryParseTimestamp(from, out var fromValue) || !TryParseTimestamp(to, out var toValue))
                {
                    throw new CheckoutException(400, "Query parameters 'from' and 'to' must be ISO-8601 date-times.");
                }

                var report = await reconciliation.ReconcileAsync(fromValue, toValue);
                return Results.Ok(report);
            })
            .Produces<ReconciliationReport>()
            .WithTags("ReconciliationEndpoints");
    }

    public Task<IResult> HandleAsync(IReconciliationService reconciliation) =>
        throw new System.NotSupportedException("Use the route handler.");

    private static bool TryParseTimestamp(string? value, out DateTimeOffset parsed)
    {
        parsed = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed);
    }
}
