using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated caller in a subscription plan. Idempotent: a double-submit never
/// creates a second customer or a second subscription — an existing live subscription is returned.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly ISubscriptionBillingService _billing;
    private readonly UserManager<ApplicationUser> _users;

    public CreateSubscriptionEndpoint(ISubscriptionBillingService billing, UserManager<ApplicationUser> users)
    {
        _billing = billing;
        _users = users;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            async (CreateSubscriptionRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .Produces<CreateSubscriptionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribe to a plan",
                description: "Enrolls the authenticated caller in the given plan (idempotently) via Maxio."));
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user)
    {
        var planHandle = request.PlanHandle?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(planHandle))
        {
            return Results.Problem(detail: "planHandle is required.", statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid request");
        }

        var identity = await SubscriberIdentity.ResolveAsync(user, _users);
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var subscription = await _billing.SubscribeAsync(new SubscribeCommand(identity, planHandle));
            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = subscription.ToDto()
            };
            return Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest,
                title: "Unknown plan");
        }
        catch (BillingServiceException ex)
        {
            return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system unavailable");
        }
    }
}
