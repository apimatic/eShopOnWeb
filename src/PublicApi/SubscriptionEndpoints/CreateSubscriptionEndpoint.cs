using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated caller to a plan. Idempotent: ensures a single billing customer
/// for the user and never creates a duplicate subscription for a plan they already hold.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal>
{
    private readonly IBillingService _billingService;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IBillingService billingService, UserManager<ApplicationUser> userManager)
    {
        _billingService = billingService;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ClaimsPrincipal user) => await HandleAsync(request, user))
            .RequireAuthorization(SubscriptionAuth.JwtPolicy)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal principal)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.Problem(title: "Invalid request", detail: "PlanHandle is required.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var currentUser = await CurrentUserResolver.ResolveAsync(principal, _userManager);
        if (currentUser is null)
        {
            return Results.Problem(title: "Unauthorized", detail: "The caller could not be resolved to a user.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        try
        {
            // Validate the plan against the live catalog so an unknown handle yields a clean 400
            // rather than an opaque upstream rejection.
            var plans = await _billingService.GetPlansAsync();
            if (plans.All(p => !string.Equals(p.Handle, request.PlanHandle, System.StringComparison.OrdinalIgnoreCase)))
            {
                return Results.Problem(
                    title: "Unknown plan",
                    detail: $"No subscription plan with handle '{request.PlanHandle}' is available.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var enrollment = new SubscriptionEnrollment(
                UserReference: currentUser.UserReference,
                Email: currentUser.Email,
                FirstName: currentUser.FirstName,
                LastName: currentUser.LastName,
                PlanHandle: request.PlanHandle);

            var subscription = await _billingService.SubscribeAsync(enrollment);

            response.Subscription = subscription.ToDto();
            response.AlreadyExisted = subscription.AlreadyExisted;

            return subscription.AlreadyExisted
                ? Results.Ok(response)
                : Results.Created($"api/my-subscriptions", response);
        }
        catch (BillingException ex)
        {
            return BillingProblem.From(ex);
        }
    }
}
