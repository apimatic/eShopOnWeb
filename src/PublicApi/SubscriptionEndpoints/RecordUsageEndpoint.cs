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
/// Records pay-as-you-go usage against the caller's subscription (UC2). Administrators may
/// report usage for any user by supplying a user reference.
/// </summary>
public class RecordUsageEndpoint : SubscriptionEndpointBase,
    IEndpoint<IResult, RecordUsageRequest, ISubscriptionService>
{
    public RecordUsageEndpoint(IHttpContextAccessor httpContextAccessor) : base(httpContextAccessor)
    {
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions/usage",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (RecordUsageRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<RecordUsageResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(RecordUsageRequest request, ISubscriptionService subscriptionService)
    {
        var userReference = ResolveUserReference(request.UserReference);
        if (userReference is null)
        {
            return Denied();
        }

        if (request.Quantity <= 0)
        {
            return Results.BadRequest("Usage quantity must be greater than zero.");
        }

        var response = new RecordUsageResponse(request.CorrelationId());

        var usage = await subscriptionService.RecordUsageAsync(userReference, request.Quantity, request.Memo);

        response.SubscriptionId = usage.SubscriptionId;
        response.ComponentHandle = usage.ComponentHandle;
        response.Quantity = usage.Quantity;
        response.PeriodToDateTotal = usage.PeriodToDateTotal;

        return Results.Ok(response);
    }
}
