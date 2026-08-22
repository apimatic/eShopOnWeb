using System;
using System.Collections.Generic;
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

public class ReconciliationEndpoint : IEndpoint<IResult, IOperatorNotificationService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/notifications/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (DateTimeOffset from, DateTimeOffset to, IOperatorNotificationService service, CancellationToken ct) =>
            {
                var result = await service.ReconcileAsync(from, to, ct);
                return ResultHttp.ToHttp(result, report => Results.Ok(new ReconciliationResponse
                {
                    From = report.From,
                    To = report.To,
                    FromNumber = report.FromNumber,
                    ProviderMessages = report.ProviderMessages.Select(m => new ProviderMessageDto
                    {
                        Sid = m.Sid,
                        Status = m.Status,
                        DateSent = m.DateSent,
                        DateCreated = m.DateCreated,
                        ErrorCode = m.ErrorCode,
                        ErrorMessage = m.ErrorMessage
                    }).ToList(),
                    ApplicationMessages = report.ApplicationMessages.Select(NotificationDto.From).ToList(),
                    OnlyInProvider = report.OnlyInProvider.ToList(),
                    OnlyInApplication = report.OnlyInApplication.ToList(),
                    Matched = report.Matched.ToList()
                }));
            })
            .Produces<ReconciliationResponse>()
            .WithTags("NotificationEndpoints");
    }

    public Task<IResult> HandleAsync(IOperatorNotificationService service) => Task.FromResult(Results.Unauthorized());
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public List<ProviderMessageDto> ProviderMessages { get; set; } = new();
    public List<NotificationDto> ApplicationMessages { get; set; } = new();
    public List<string> OnlyInProvider { get; set; } = new();
    public List<string> OnlyInApplication { get; set; } = new();
    public List<string> Matched { get; set; } = new();
}

public class ProviderMessageDto
{
    public string? Sid { get; set; }
    public string? Status { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}
