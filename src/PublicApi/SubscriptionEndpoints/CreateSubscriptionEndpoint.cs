using System.Security.Claims;
using System.Threading;
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
/// Subscribes the authenticated caller to a plan. Idempotent: ensures a single Maxio customer exists
/// for the user and reuses an existing live subscription to the same plan, so a double-click never
/// creates two customers or two subscriptions.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request,
                   ISubscriptionBillingService billingService,
                   ClaimsPrincipal user,
                   UserManager<ApplicationUser> userManager,
                   CancellationToken cancellationToken) =>
            {
                var subscriber = await SubscriberResolver.ResolveAsync(user, userManager);
                if (subscriber is null)
                {
                    return Results.Unauthorized();
                }

                request.Subscriber = subscriber;
                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute("Subscribes the caller to a plan", "Enrolls the authenticated user in the given subscription plan."));
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest("A planHandle is required.");
        }

        var subscription = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle, cancellationToken);

        var response = new SubscribeResponse(request.CorrelationId())
        {
            Subscription = CustomerSubscriptionDto.FromModel(subscription)
        };

        return Results.Created($"api/my-subscriptions/{subscription.Id}", response);
    }
}
