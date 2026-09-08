using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions.SubscriptionEndpoints;

public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest? request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request ?? new CreateSubscriptionRequest(), subscriptionService);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>(201)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return ErrorResult.BadRequest("A 'planHandle' must be supplied in the request body.");
        }

        var userName = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrEmpty(userName))
        {
            return Results.Unauthorized();
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            response.Subscription = await subscriptionService.SubscribeAsync(userName, request.PlanHandle.Trim());
            return response.Subscription.AlreadySubscribed ? Results.Ok(response) : Results.Created(string.Empty, response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return ErrorResult.From(ex);
        }
        catch (MaxioConfigurationException)
        {
            throw;
        }
        catch (MaxioApiException ex)
        {
            return ErrorResult.From(ex);
        }
    }
}
