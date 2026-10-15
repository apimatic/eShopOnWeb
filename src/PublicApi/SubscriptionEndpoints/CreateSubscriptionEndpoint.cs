using System.Net.Mail;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Billing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the calling user to a plan. Idempotent per user and plan: repeating the call returns
/// the existing subscription instead of creating another.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, ISubscriptionBillingService>
{
    private const int MaxFieldLength = 255;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken) =>
            {
                return await HandleAsync(request, user, billingService, cancellationToken);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .WithTags("SubscriptionEndpoints");
    }

    public Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService) =>
        HandleAsync(request, user, billingService, CancellationToken.None);

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal user, ISubscriptionBillingService billingService, CancellationToken cancellationToken)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        // The buyer is whoever the token says it is; the body only chooses the plan.
        var buyerId = user.Identity?.Name;
        if (string.IsNullOrWhiteSpace(buyerId))
        {
            return Results.Unauthorized();
        }

        Validate(request);

        var email = ResolveEmail(user, buyerId)
            ?? throw new BillingException(BillingErrorKind.InvalidRequest, "The account has no e-mail address to bill.");
        var command = new SubscribeCommand(buyerId, email, request.FirstName, request.LastName, request.PlanHandle.Trim());

        var result = await billingService.SubscribeAsync(command, cancellationToken);

        response.SubscriptionReference = result.SubscriptionReference;
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.From(result.Subscription);

        switch (result.Outcome)
        {
            case SubscribeOutcome.Created:
                response.Outcome = "created";
                return Results.Created("api/my-subscriptions", response);
            case SubscribeOutcome.AlreadySubscribed:
                response.Outcome = "already_subscribed";
                return Results.Ok(response);
            default:
                response.Outcome = "pending_confirmation";
                return Results.Accepted("api/my-subscriptions", response);
        }
    }

    private static void Validate(CreateSubscriptionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, "planHandle is required.");
        }
        if (request.PlanHandle.Length > MaxFieldLength
            || request.FirstName?.Length > MaxFieldLength
            || request.LastName?.Length > MaxFieldLength)
        {
            throw new BillingException(BillingErrorKind.InvalidRequest, $"planHandle, firstName and lastName must be at most {MaxFieldLength} characters.");
        }
    }

    // eShopOnWeb user names are e-mail addresses; an explicit e-mail claim wins when the token carries one.
    private static string? ResolveEmail(ClaimsPrincipal user, string buyerId)
    {
        var candidate = user.FindFirst(ClaimTypes.Email)?.Value ?? buyerId;
        return MailAddress.TryCreate(candidate, out var address) ? address.Address : null;
    }
}
