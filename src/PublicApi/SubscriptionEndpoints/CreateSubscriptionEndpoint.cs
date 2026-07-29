using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Ensures a Maxio customer exists
/// (idempotent) and enrolls them; a repeat call for the same plan is a safe no-op.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user,
                UserManager<ApplicationUser> userManager, IMaxioBillingService billingService) =>
            {
                return await SubscriptionResults.RunAsync(async () =>
                {
                    var identity = await SubscriberIdentity.ResolveAsync(user, userManager);
                    request.Reference = identity.Reference;
                    request.Email = identity.Email;
                    request.FirstName = identity.FirstName;
                    request.LastName = identity.LastName;
                    return await HandleAsync(request, billingService);
                });
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Subscribe to a plan",
                "Subscribes the authenticated caller to the given plan handle. Idempotent per user + plan."));
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        var subscription = await billingService.SubscribeAsync(
            request.Reference, request.Email, request.FirstName, request.LastName, request.PlanHandle);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = SubscriptionDto.FromDomain(subscription),
            AlreadyExisted = subscription.AlreadyExisted
        };

        return subscription.AlreadyExisted
            ? Results.Ok(response)
            : Results.Created($"api/my-subscriptions/{subscription.Id}", response);
    }
}
