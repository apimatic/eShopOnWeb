using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// <para>
/// Idempotent by design: repeating the call - a double-click, a client retry, a replay after a
/// timeout - never creates a second billing account or a second subscription. A repeat returns the
/// existing subscription with <c>created: false</c> and a 200 instead of a 201.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, httpContext, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        HttpContext httpContext,
        ISubscriptionBillingService billingService)
    {
        var cancellationToken = httpContext.RequestAborted;

        var subscriber = SubscriberFactory.FromPrincipal(httpContext.User, request.FirstName, request.LastName);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            var plans = await billingService.ListPlansAsync(cancellationToken);
            return Results.Problem(
                title: "A plan handle is required.",
                detail: plans.Count == 0
                    ? "No subscription plans are currently available."
                    : $"Set 'planHandle' to one of: {string.Join(", ", plans.Select(p => p.Handle))}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await billingService.SubscribeAsync(subscriber, request.PlanHandle, cancellationToken);

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Created = result.Created,
            Subscription = result.Subscription.ToDto()
        };

        return result.Created
            ? Results.Created($"api/my-subscriptions", response)
            : Results.Ok(response);
    }
}
