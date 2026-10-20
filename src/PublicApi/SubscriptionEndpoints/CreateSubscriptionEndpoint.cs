using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user (identity from the JWT) to a plan. Idempotent per (user, plan): repeating the
/// request returns the existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, MaxioSubscriptionService, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, MaxioSubscriptionService subscriptions, HttpContext httpContext) =>
            {
                return await HandleAsync(request, subscriptions, httpContext);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<SubscriptionErrorResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status409Conflict)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, MaxioSubscriptionService subscriptions, HttpContext httpContext)
    {
        var userName = httpContext.User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var userManager = httpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync(userName);
        var email = string.IsNullOrWhiteSpace(user?.Email) ? userName : user!.Email!;

        try
        {
            var outcome = await subscriptions.SubscribeAsync(userName, email, request.ProductHandle, request.CorrelationId(), httpContext.RequestAborted);
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Status = outcome.Kind.ToString().ToLowerInvariant(),
                Message = outcome.Message,
                Reference = outcome.Reference,
                Subscription = outcome.Subscription is null ? null : SubscriptionEndpointMapping.ToDto(outcome.Subscription),
            };

            return outcome.Kind switch
            {
                SubscribeOutcomeKind.Created => Results.Created("api/my-subscriptions", response),
                SubscribeOutcomeKind.Pending => Results.Accepted("api/my-subscriptions", response),
                _ => Results.Ok(response),
            };
        }
        catch (MaxioBillingException ex)
        {
            return SubscriptionEndpointMapping.Failure(ex);
        }
    }
}
