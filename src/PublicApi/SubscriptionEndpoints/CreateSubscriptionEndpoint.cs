using System;
using System.Net.Http;
using System.Threading;
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

public sealed class CreateSubscriptionEndpoint : IEndpoint<IResult, SubscribeRequest, HttpContext>
{
    public Task<IResult> HandleAsync(SubscribeRequest request, HttpContext context) =>
        HandleAsync(request, context, context.RequestServices.GetRequiredService<ISubscriptionBillingService>(),
            context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>());

    private static async Task<IResult> HandleAsync(SubscribeRequest request, HttpContext context,
        ISubscriptionBillingService service, UserManager<ApplicationUser> userManager)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "planHandle is required." });
        }

        var user = await SubscriptionEndpointHelpers.GetCurrentUserAsync(context, userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await service.SubscribeAsync(user, request.PlanHandle, context.RequestAborted);
            return Results.Ok(new CreateSubscriptionResponse { Subscription = subscription });
        }
        catch (SubscriptionPlanNotFoundException)
        {
            return Results.NotFound(new { error = "The requested subscription plan was not found." });
        }
        catch (SubscriptionAlreadyExistsException exception)
        {
            return Results.Conflict(new { error = exception.Message });
        }
        catch (MaxioApiException)
        {
            return SubscriptionEndpointHelpers.MaxioFailure();
        }
        catch (HttpRequestException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
        catch (InvalidOperationException)
        {
            return SubscriptionEndpointHelpers.ServiceUnavailable();
        }
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions", async (SubscribeRequest request, HttpContext context) =>
            await HandleAsync(request, context))
            .RequireAuthorization(new AuthorizeAttribute
            {
                AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }
}

public sealed class SubscribeRequest
{
    public string PlanHandle { get; init; } = string.Empty;
}

public sealed class CreateSubscriptionResponse : BaseResponse
{
    public CreateSubscriptionResponse(Guid correlationId) : base(correlationId) { }
    public CreateSubscriptionResponse() { }

    public SubscriptionDto Subscription { get; init; } = new();
}
