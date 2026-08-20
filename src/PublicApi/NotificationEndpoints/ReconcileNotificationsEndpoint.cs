using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconcileNotificationsEndpoint : IEndpoint<IResult, ReconciliationQueryRequest, INotificationOperatorService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, INotificationOperatorService service) =>
            {
                return await HandleAsync(new ReconciliationQueryRequest(from, to), service);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationQueryRequest request, INotificationOperatorService service)
    {
        var report = await service.ReconcileAsync(request.From, request.To);
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            MatchedCount = report.Matched.Count,
            ProviderOnlyCount = report.ProviderOnly.Count,
            ApplicationOnlyCount = report.ApplicationOnly.Count,
            Matched = report.Matched.Select(m => new ReconciliationMatchDto
            {
                Application = OrderApiMapper.ToDto(m.Application),
                Provider = ToProviderDto(m.Provider)
            }).ToList(),
            ProviderOnly = report.ProviderOnly.Select(ToProviderDto).ToList(),
            ApplicationOnly = report.ApplicationOnly.Select(OrderApiMapper.ToDto).ToList()
        };

        return Results.Ok(response);
    }

    private static ProviderMessageDto ToProviderDto(ApplicationCore.Messaging.ProviderMessage message) => new()
    {
        Sid = message.Sid,
        Status = message.Status,
        Body = message.Body,
        DateSent = message.DateSent,
        DateCreated = message.DateCreated,
        ErrorCode = message.ErrorCode,
        ErrorMessage = message.ErrorMessage
    };
}
