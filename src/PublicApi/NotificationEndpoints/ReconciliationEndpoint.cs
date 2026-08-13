using System;
using System.Globalization;
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
/// Operator report: the provider's own record of messages sent from this application's configured number
/// over a date range, lined up against what eShop believes it sent, so a gap either way is visible.
/// </summary>
public class ReconciliationEndpoint : IEndpoint<IResult, DateTimeOffset, DateTimeOffset>
{
    private readonly IOrderNotificationService _service;

    public ReconciliationEndpoint(IOrderNotificationService service) => _service = service;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string? from, string? to, CancellationToken ct) =>
            {
                if (!TryParseIso(from, out var fromDt))
                    return Results.BadRequest("'from' must be an ISO-8601 date-time.");
                if (!TryParseIso(to, out var toDt))
                    return Results.BadRequest("'to' must be an ISO-8601 date-time.");

                return await HandleAsync(fromDt, toDt, ct);
            })
            .Produces<ReconciliationResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to) => HandleAsync(from, to, default);

    public async Task<IResult> HandleAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var report = await _service.ReconcileAsync(from, to, ct);
        return Results.Ok(ReconciliationResponse.Create(report, Guid.NewGuid()));
    }

    private static bool TryParseIso(string? value, out DateTimeOffset result)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
