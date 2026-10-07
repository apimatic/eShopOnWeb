using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, UserManager<ApplicationUser> userManager,
                ISubscriptionService subscriptionService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, userManager, subscriptionService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ClaimsPrincipal user,
        UserManager<ApplicationUser> userManager,
        ISubscriptionService subscriptionService,
        CancellationToken cancellationToken)
    {
        var planHandle = string.IsNullOrWhiteSpace(request?.PlanHandle) ? request?.ProductHandle : request.PlanHandle;
        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return Results.Problem(
                title: "A plan handle is required.",
                detail: "Provide 'planHandle' in the request body, using a handle returned by GET /api/subscription-plans.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var subscriber = await SubscriberIdentityResolver.ResolveAsync(user, userManager);
        if (subscriber == null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(subscriber, planHandle, cancellationToken);
            var response = new CreateSubscriptionResponse(request!.CorrelationId())
            {
                Subscription = SubscriptionDtoMapper.ToDto(result.Subscription),
                AlreadySubscribed = !result.Created
            };

            return result.Created
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return SubscriptionErrorResults.PlanNotFound(ex);
        }
        catch (BillingProviderException ex)
        {
            return SubscriptionErrorResults.From(ex);
        }
    }
}
