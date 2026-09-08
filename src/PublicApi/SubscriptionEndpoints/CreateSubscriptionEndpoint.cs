using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a subscription plan. Re-submitting the same plan
/// (double-click) is idempotent and returns the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, HttpContext>
{
    private readonly IMaxioSubscriptionService _subscriptionService;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService)
    {
        _subscriptionService = subscriptionService;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, HttpContext httpContext) =>
            {
                return await HandleAsync(request, httpContext);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, HttpContext httpContext)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return SubscriptionEndpointHelpers.BadRequest("A planHandle is required to subscribe.");
        }

        var customer = await SubscriptionEndpointHelpers.ResolveCustomerProfileAsync(httpContext, httpContext.RequestAborted);
        if (customer is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await _subscriptionService.SubscribeAsync(
                customer,
                request.PlanHandle.Trim(),
                httpContext.RequestAborted);

            var response = new CreateSubscriptionResponse
            {
                Subscription = SubscriptionEndpointHelpers.ToSubscriptionDto(result.Subscription)
            };

            return result.Created
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (MaxioException ex)
        {
            return SubscriptionEndpointHelpers.ToErrorResult(ex);
        }
    }
}
