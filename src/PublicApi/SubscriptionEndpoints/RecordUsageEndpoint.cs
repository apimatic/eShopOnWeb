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

/// <summary>
/// UC2: record usage against the caller's own subscription; administrators may target any
/// subscription. Authorization scope (own vs. any) is enforced by <see cref="ISubscriptionService"/>.
/// </summary>
public class RecordUsageEndpoint : IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (RecordUsageRequest request, HttpContext httpContext, ISubscriptionService subscriptionService) =>
            {
                request.UserReference = httpContext.User.Identity!.Name!;
                request.IsAdmin = httpContext.User.IsInRole(Constants.Roles.ADMINISTRATORS);
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var response = new RecordUsageResponse(request.CorrelationId());
        var result = await subscriptionService.RecordUsageAsync(request.UserReference, request.SubscriptionId, request.Quantity, request.Memo, request.IsAdmin);
        response.RecordedQuantity = result.RecordedQuantity;
        response.PeriodToDateBalance = result.PeriodToDateBalance;
        return Results.Ok(response);
    }
}
