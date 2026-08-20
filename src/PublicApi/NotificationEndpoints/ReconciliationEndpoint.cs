using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationEndpoint : IEndpoint<IResult, IOrderSmsService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string from, string to, IOrderSmsService service) =>
            {
                if (!DateTimeOffset.TryParse(from, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var fromValue)
                    || !DateTimeOffset.TryParse(to, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var toValue))
                {
                    return Results.BadRequest(new { errors = new[] { "from and to must be ISO-8601 date-times." } });
                }

                var result = await service.ReconcileAsync(fromValue, toValue);
                return result.ToHttpResult(report => Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    Matched = report.Matched.Select(ToDto).ToList(),
                    ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                    ApplicationOnly = report.ApplicationOnly.Select(ToDto).ToList()
                }));
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOrderSmsService orderSmsService)
        => Task.FromResult(Results.Ok());

    private static ReconciliationRowDto ToDto(ReconciliationRow row)
        => new()
        {
            NotificationId = int.TryParse(row.NotificationId, out var id) ? id : null,
            ProviderMessageSid = row.ProviderMessageSid,
            ApplicationStatus = row.ApplicationStatus,
            ProviderStatus = row.ProviderStatus,
            Match = row.Match
        };
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationRowDto> Matched { get; set; } = new();
    public List<ReconciliationRowDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationRowDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationRowDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ApplicationStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public string Match { get; set; } = string.Empty;
}
