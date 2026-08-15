using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan (the hero flow). Idempotent: ensures a Maxio
/// customer exists for the user and reuses any live subscription to the plan, so a repeated or
/// double-clicked call never creates a duplicate customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest? request, ISubscriptionBillingService billingService, HttpContext httpContext, CancellationToken ct) =>
            {
                request ??= new CreateSubscriptionRequest();
                request.Subscriber = SubscriberIdentityFactory.FromPrincipal(httpContext.User);
                request.CancellationToken = ct;
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (request.Subscriber is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle, request.CancellationToken);
            response.Subscription = subscription.ToDto();
            return Results.Ok(response);
        }
        catch (MaxioBillingException ex)
        {
            return BillingProblemResults.ToResult(ex);
        }
    }
}
