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
/// Subscribes the authenticated user to a plan. The caller's identity comes from the JWT.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, IMaxioBillingService billing, CancellationToken ct) =>
            {
                return await HandleAsync(request, user.Identity?.Name, billing, ct);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billing) =>
        HandleAsync(request, null, billing, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string? userName,
        IMaxioBillingService billing, CancellationToken ct)
    {
        try
        {
            var result = await billing.SubscribeAsync(userName, request.PlanHandle, ct);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription,
                AlreadySubscribed = result.AlreadySubscribed
            };
            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return BillingResults.From(ex, request.CorrelationId());
        }
    }
}
