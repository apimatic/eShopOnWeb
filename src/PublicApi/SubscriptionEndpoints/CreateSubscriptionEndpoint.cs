using System.Security.Claims;
using System.Threading;
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
/// Subscribe the authenticated user to a plan (idempotent per user and plan)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            {
                return await HandleAsync(request, user, service, ct);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct)
    {
        var userName = SubscriptionEndpointHelpers.UserName(user);
        if (userName is null)
            return Results.Unauthorized();

        var response = new CreateSubscriptionResponse(request.CorrelationId());
        try
        {
            var result = await service.SubscribeAsync(userName, request.ProductHandle, ct);
            response.Subscription = result.Subscription;
            return result.Created
                ? Results.Created("api/my-subscriptions", response)
                : Results.Ok(response);
        }
        catch (SubscriptionException ex)
        {
            return SubscriptionEndpointHelpers.Failure(ex);
        }
    }
}
