using System;
using System.Collections.Generic;
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

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService operators) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, operators);
            })
            .Produces<ReconciliationResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, INotificationOperatorService operators)
    {
        try
        {
            var report = await operators.ReconcileAsync(request.From, request.To);
            return Results.Ok(new ReconciliationResponse
            {
                From = report.From,
                To = report.To,
                FromNumber = report.FromNumber,
                Matched = report.Matched.Select(ToDto).ToList(),
                ProviderOnly = report.ProviderOnly.Select(ToDto).ToList(),
                LocalOnly = report.LocalOnly.Select(ToDto).ToList()
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static ReconciliationEntryDto ToDto(ReconciliationEntry entry)
    {
        return new ReconciliationEntryDto
        {
            NotificationId = entry.NotificationId,
            ProviderMessageSid = entry.ProviderMessageSid,
            LocalStatus = entry.LocalStatus,
            ProviderStatus = entry.ProviderStatus,
            Body = entry.Body
        };
    }
}

public class ReconciliationRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> LocalOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? LocalStatus { get; set; }
    public string? ProviderStatus { get; set; }
    public string? Body { get; set; }
}
