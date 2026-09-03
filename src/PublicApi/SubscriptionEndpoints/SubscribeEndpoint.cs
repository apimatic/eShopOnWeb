using System.Threading;
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
/// Subscribes the authenticated shopper to a plan. Ensures a Maxio customer exists for the user
/// (idempotent) and enrolls them; a double-click never creates two customers or two subscriptions.
/// </summary>
public class SubscribeEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SubscribeEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (SubscribeRequest request, ISubscriptionBillingService billingService) =>
                await HandleAsync(request, billingService))
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(SubscribeRequest request, ISubscriptionBillingService billingService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var subscriber = SubscriptionMapping.GetSubscriber(httpContext?.User!);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        var ct = httpContext?.RequestAborted ?? CancellationToken.None;
        try
        {
            var result = await billingService.SubscribeAsync(subscriber.Value, request.PlanHandle, ct);
            var response = new SubscribeResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto(),
                AlreadySubscribed = result.AlreadySubscribed,
            };

            // Fresh subscription → 201 Created; idempotent re-subscribe → 200 OK.
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions/{response.Subscription.SubscriptionId}", response);
        }
        catch (SubscriptionBillingException ex)
        {
            return ex.ToResult();
        }
    }
}
