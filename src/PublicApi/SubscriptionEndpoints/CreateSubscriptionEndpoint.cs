using System.Threading;
using System.Threading.Tasks;
using BlazorShared.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (POST /api/subscriptions). Idempotent: repeating
/// the call (or a double-click racing it) returns the existing subscription instead of creating
/// a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                IMaxioSubscriptionService subscriptionService,
                ISubscriptionIdentityResolver identityResolver,
                CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, subscriptionService, identityResolver, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    private static async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        IMaxioSubscriptionService subscriptionService,
        ISubscriptionIdentityResolver identityResolver,
        CancellationToken cancellationToken)
    {
        var planHandle = request?.PlanHandle?.Trim() ?? string.Empty;
        if (planHandle.Length == 0)
        {
            return Results.BadRequest(new ErrorDetails
            {
                StatusCode = StatusCodes.Status400BadRequest,
                Message = "planHandle is required."
            });
        }

        var identity = await identityResolver.ResolveAsync();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        var customer = new SubscriptionCustomerProfile(
            identity.UserName,
            identity.Email,
            identity.FirstName,
            identity.LastName);

        var outcome = await subscriptionService.SubscribeAsync(
            new SubscribeToPlanRequest(customer, planHandle),
            cancellationToken);

        var response = new CreateSubscriptionResponse
        {
            Subscription = SubscriptionRecordMapping.ToDto(outcome.Subscription)
        };

        return outcome.IsNew
            ? Results.Created("/api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
