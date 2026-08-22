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

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, IOrderNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (string from, string to, IOrderNotificationService service) =>
            {
                return await HandleAsync(from, to, service);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderNotificationService request)
        => Task.FromResult(Results.BadRequest(new { message = "from and to query parameters are required." }));

    private async Task<IResult> HandleAsync(string from, string to, IOrderNotificationService service)
    {
        if (!TryParseTimestamp(from, out var fromTimestamp) || !TryParseTimestamp(to, out var toTimestamp))
        {
            return Results.BadRequest(new { message = "from and to must be ISO-8601 date-times." });
        }

        try
        {
            var report = await service.ReconcileAsync(fromTimestamp, toTimestamp);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Entries = [.. report.Entries]
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static bool TryParseTimestamp(string? value, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        var space = normalized.LastIndexOf(' ');
        if (space > 0 && space == normalized.Length - 6)
        {
            normalized = normalized[..space] + "+" + normalized[(space + 1)..];
        }

        return DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out timestamp);
    }
}
