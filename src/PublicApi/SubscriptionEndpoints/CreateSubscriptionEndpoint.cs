using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// POST /api/subscriptions — subscribes the authenticated shopper to a plan. Ensures a Maxio customer
/// exists for the eShopOnWeb user and enrolls them, idempotently: a double-click never creates two
/// customers or two subscriptions. JWT-authenticated; the caller identity comes from the token.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService, BillingUser>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             HttpContext httpContext) =>
            {
                var user = await BillingUserResolver.ResolveAsync(httpContext.User, userManager);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request, subscriptionService, user.Value);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService, BillingUser user)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { message = "planHandle is required. Choose one from GET /api/subscription-plans." });
        }

        try
        {
            var result = await subscriptionService.SubscribeAsync(user, request.PlanHandle.Trim());

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = SubscriptionDto.FromMaxio(result.Subscription),
                AlreadyExisted = result.AlreadyExisted
            };

            // A repeat subscribe (double-click / retry) is an idempotent 200; a fresh enrollment is a 201.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions/{response.Subscription.Id}", response);
        }
        catch (MaxioApiException ex)
        {
            return MaxioProblem.ToResult(ex);
        }
    }
}
