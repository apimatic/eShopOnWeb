using System.Net;
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
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call
/// for the same plan returns the existing subscription instead of duplicating it.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest>
{
    private readonly MaxioSubscriptionService _subscriptionService;
    private readonly CurrentUserAccessor _currentUserAccessor;

    public CreateSubscriptionEndpoint(MaxioSubscriptionService subscriptionService, CurrentUserAccessor currentUserAccessor)
    {
        _subscriptionService = subscriptionService;
        _currentUserAccessor = currentUserAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request) =>
            {
                return await HandleAsync(request);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.BadRequest("ProductHandle is required.");
        }

        var (userId, email) = await _currentUserAccessor.GetCurrentUserAsync();

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(userId, email, request.ProductHandle.Trim());
            response.Subscription = SubscriptionDto.FromMaxio(subscription);
            return Results.Ok(response);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Results.BadRequest(ex.Message);
        }
    }
}
