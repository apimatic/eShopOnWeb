using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// Operator action: reports the provider's own record of messages sent from the configured sending
/// number over a date range and lines them up against what eShop believes it sent, so a discrepancy
/// either way is visible. <c>from</c> and <c>to</c> are ISO-8601 date-times. Administrators only.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, IOrderNotificationService service, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDate) || !TryParseIso(to, out var toDate))
                    return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });

                if (toDate < fromDate)
                    return Results.BadRequest(new { message = "to must not be earlier than from." });

                var result = await service.ReconcileAsync(fromDate, toDate, ct);

                var response = new ReconciliationResponse
                {
                    From = result.From,
                    To = result.To,
                    SendingNumber = result.SendingNumber,
                    Matched = result.Matched.Select(ReconciliationEntryDto.From).ToList(),
                    ProviderOnly = result.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
                    EShopOnly = result.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
                };
                return Results.Ok(response);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }

    public Task<IResult> HandleAsync(IOrderNotificationService service)
        => Task.FromResult<IResult>(Results.Empty);
}
