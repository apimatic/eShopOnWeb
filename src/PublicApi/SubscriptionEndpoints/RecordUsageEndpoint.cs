using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Records pay-as-you-go usage (UC2). Callers report against their own subscription; targeting
/// another subscription by id is an administrator action.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscription-usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ClaimsPrincipal user, ISubscriptionService subscriptionService) =>
            {
                request.UserName = user.Identity?.Name;
                request.IsAdministrator = user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrEmpty(request.UserName))
        {
            return Results.Unauthorized();
        }

        if (request.SubscriptionId.HasValue && !request.IsAdministrator)
        {
            // Forbid() would defer to the cookie scheme Identity registers and redirect; this host
            // is a JWT API, so answer with the status code directly.
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        var report = request.SubscriptionId.HasValue
            ? await subscriptionService.RecordUsageForSubscriptionAsync(request.SubscriptionId.Value,
                request.Quantity, request.Memo)
            : await subscriptionService.RecordUsageAsync(request.UserName, request.Quantity, request.Memo);

        response.UsageId = report.Record.Id;
        response.SubscriptionId = report.Record.SubscriptionId;
        response.ComponentHandle = report.Record.ComponentHandle;
        response.Quantity = report.Record.Quantity;
        response.Memo = report.Record.Memo;
        response.PeriodToDateBalance = report.PeriodToDateBalance;
        response.BalanceUnavailable = report.BalanceUnavailable;

        return Results.Ok(response);
    }
}
