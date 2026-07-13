using System.Security.Claims;
using System.Threading.Tasks;
using BlazorShared.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>UC2: records a unit of metered usage against the caller's own subscription, or (Administrator) any subscription.</summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ISubscriptionService subscriptionService, ClaimsPrincipal user) =>
            {
                request.CallerReference = user.Identity!.Name!;
                request.CallerIsAdmin = user.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var (subscription, denied) = await SubscriptionAccess.ResolveAsync(
            subscriptionService, request.CallerReference, request.CallerIsAdmin, request.SubscriptionId);
        if (denied != null)
        {
            return denied;
        }

        var response = new RecordUsageResponse(request.CorrelationId());
        var usage = await subscriptionService.RecordUsageAsync(subscription!.Id, request.Quantity, request.Memo);

        response.PeriodToDateUnitBalance = usage.PeriodToDateUnitBalance;
        response.PeriodToDateUnavailable = usage.PeriodToDateUnavailable;

        return Results.Ok(response);
    }
}
