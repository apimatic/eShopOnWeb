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

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconcileNotificationsRequest, IOrderNotificationWorkflow>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOrderNotificationWorkflow workflow) =>
            {
                return await HandleAsync(new ReconcileNotificationsRequest { From = from, To = to }, workflow);
            })
            .Produces<ReconcileNotificationsResponse>()
            .Produces(StatusCodes.Status400BadRequest)
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconcileNotificationsRequest request, IOrderNotificationWorkflow workflow)
    {
        var result = await workflow.ReconcileAsync(request.From, request.To);
        if (!result.Succeeded)
        {
            return ApiResults.From(result.StatusCode, error: result.Error);
        }

        return Results.Ok(new ReconcileNotificationsResponse
        {
            From = result.From,
            To = result.To,
            FromNumber = result.FromNumber,
            Matched = result.Matched.Select(ToDto).ToList(),
            ProviderOnly = result.ProviderOnly.Select(ToDto).ToList(),
            ApplicationOnly = result.ApplicationOnly.Select(ToDto).ToList()
        });
    }

    private static ReconciliationItemDto ToDto(ReconciliationItem item)
    {
        return new ReconciliationItemDto
        {
            NotificationId = item.NotificationId,
            ProviderMessageSid = item.ProviderMessageSid,
            Status = item.Status,
            Kind = item.Kind,
            OrderId = item.OrderId,
            ProviderDate = item.ProviderDate
        };
    }
}

public class ReconcileNotificationsRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconcileNotificationsResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string? FromNumber { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> ApplicationOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public string? Kind { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? ProviderDate { get; set; }
}
