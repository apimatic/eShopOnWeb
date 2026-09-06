using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribe the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// Idempotent by design. The billing customer is looked up before it is created, the customer's
/// subscriptions are read before a new one is created, and concurrent requests for the same shopper are
/// serialized — so clicking Subscribe twice returns the same subscription with 200 instead of enrolling
/// the shopper twice.
/// </remarks>
public class CreateSubscriptionEndpoint
    : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, HttpContext>
{
    private readonly IOptions<MaxioSettings> _settings;

    public CreateSubscriptionEndpoint(IOptions<MaxioSettings> settings)
    {
        _settings = settings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billingService, HttpContext httpContext) =>
            {
                return await HandleAsync(request, billingService, httpContext);
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
        ISubscriptionBillingService billingService,
        HttpContext httpContext)
    {
        var subscriber = SubscriptionEndpointHelpers.ResolveSubscriber(
            httpContext.User, request.FirstName, request.LastName);

        if (subscriber is null)
        {
            return SubscriptionEndpointHelpers.Unauthenticated();
        }

        var planHandle = string.IsNullOrWhiteSpace(request.PlanHandle)
            ? _settings.Value.DefaultPlanHandle
            : request.PlanHandle;

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            return SubscriptionEndpointHelpers.BadRequest(
                "A planHandle is required. Call GET /api/subscription-plans for the available handles, "
                + $"or configure '{MaxioSettings.SectionName}:{nameof(MaxioSettings.DefaultPlanHandle)}' to make one the default.");
        }

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, planHandle, httpContext.RequestAborted);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadySubscribed = result.AlreadySubscribed,
                CustomerCreated = result.CustomerCreated
            };

            // A replay is not a creation, so it does not answer 201. Both carry the same body, so a
            // client that does not care can ignore the distinction.
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created(
                    $"api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}",
                    response);
        }
        catch (BillingException ex)
        {
            return SubscriptionEndpointHelpers.ToProblem(ex);
        }
    }
}
