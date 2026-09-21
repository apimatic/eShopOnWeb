using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: ensures a single Maxio customer for the
/// user and returns the existing subscription on replay, so a double-click never creates duplicates.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MaxioOptions _options;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor, IOptions<MaxioOptions> options)
    {
        _httpContextAccessor = httpContextAccessor;
        _options = options.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        var cancellationToken = _httpContextAccessor.HttpContext?.RequestAborted ?? CancellationToken.None;
        var subscriber = SubscriptionEndpointHelpers.ResolveSubscriber(_httpContextAccessor.HttpContext?.User);
        if (subscriber is null)
            return Results.Unauthorized();

        var planHandle = request?.PlanHandle;
        if (string.IsNullOrWhiteSpace(planHandle))
            planHandle = _options.DefaultPlanHandle;
        if (string.IsNullOrWhiteSpace(planHandle))
            return Results.Problem(
                detail: "A plan handle is required (no default plan is configured).",
                statusCode: StatusCodes.Status400BadRequest,
                title: "Missing plan handle");

        try
        {
            var result = await billingService.SubscribeAsync(subscriber, planHandle, cancellationToken);
            var response = new SubscribeResponse
            {
                Subscription = result.Subscription.ToDto(),
                AlreadyExisted = result.AlreadyExisted,
            };

            return result.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions/{response.Subscription!.Id}", response);
        }
        catch (PlanNotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status404NotFound, title: "Plan not found");
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToProblem();
        }
    }
}
