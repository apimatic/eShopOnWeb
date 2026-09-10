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
using System.Security.Claims;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user (identity taken from the JWT) to a plan. Idempotent: ensures a
/// single Maxio customer per user and reuses an existing live subscription to the same plan, so a
/// double-click never creates two customers or two subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, CustomerRegistration, ISubscriptionService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request,
             ISubscriptionService subscriptionService,
             UserManager<ApplicationUser> userManager,
             ClaimsPrincipal principal) =>
            {
                var registration = await SubscriberIdentity.ResolveAsync(principal, userManager);
                if (registration is null)
                {
                    return Results.Unauthorized();
                }

                return await HandleAsync(request ?? new CreateSubscriptionRequest(), registration, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, CustomerRegistration customer, ISubscriptionService subscriptionService)
    {
        try
        {
            var result = await subscriptionService.SubscribeAsync(new SubscribeCommand(customer, request.PlanHandle));

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted,
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions#{response.Subscription!.Id}", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Problem(title: "Unknown plan.", detail: ex.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (MaxioApiException ex)
        {
            return SubscriptionErrorResults.FromMaxio(ex);
        }
    }
}
