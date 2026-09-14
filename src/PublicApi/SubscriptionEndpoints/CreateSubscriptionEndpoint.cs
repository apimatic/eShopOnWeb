using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.MaxioBilling.Exceptions;
using Microsoft.eShopWeb.MaxioBilling.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan.
/// <para>
/// Idempotent by design: the customer is looked up by a reference derived from the caller's
/// identity before one is created, and an existing live subscription on the same plan is returned
/// instead of a second being created — so a double-click produces one customer and one subscription.
/// </para>
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, HttpContext httpContext, ISubscriptionBillingService billingService,
             CancellationToken cancellationToken) =>
            {
                request ??= new CreateSubscriptionRequest();

                var subscriber = httpContext.User.ToSubscriber();
                if (subscriber is null)
                {
                    return BillingResults.MissingIdentity();
                }

                request.Subscriber = subscriber;

                return await HandleAsync(request, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
        HandleAsync(request, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request,
        ISubscriptionBillingService billingService,
        CancellationToken cancellationToken)
    {
        if (request.Subscriber is null)
        {
            return BillingResults.MissingIdentity();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());

        try
        {
            var result = await billingService.SubscribeAsync(request.Subscriber, request.PlanHandle, cancellationToken);

            response.Subscription = result.Subscription.ToDto();
            response.AlreadySubscribed = result.AlreadyExisted;

            // A repeat of a request that already succeeded is not a new creation, so it answers 200.
            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (BillingException exception)
        {
            return BillingResults.Problem(exception);
        }
    }
}
