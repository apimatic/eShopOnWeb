using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: subscribing again to the
/// same plan returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, ISubscriptionBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .RequireAuthorization()
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var user = await CurrentUserResolver.ResolveAsync(_httpContextAccessor, _userManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { errors = new[] { "PlanHandle is required." } });
        }

        var (firstName, lastName) = CurrentUserResolver.DeriveCustomerName(user);
        var command = new SubscribeCommand(CurrentUserResolver.GetUserBillingReference(user),
            user.Email ?? user.UserName ?? string.Empty,
            firstName, lastName, request.PlanHandle.Trim());

        try
        {
            var subscription = await billingService.SubscribeAsync(command);

            response.Subscription = new SubscriptionDto
            {
                SubscriptionId = subscription.SubscriptionId,
                State = subscription.State,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                PriceInCents = subscription.PriceInCents,
                Price = subscription.Price.ToString("0.00"),
                NextBillingDate = subscription.NextBillingDate,
                ActivatedAt = subscription.ActivatedAt,
                AlreadySubscribed = subscription.AlreadySubscribed
            };
            return Results.Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            // Unknown plan or misconfiguration on our side.
            return Results.Problem(title: "Subscription request could not be processed", detail: ex.Message, statusCode: 400);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                title: "Maxio Advanced Billing rejected the request",
                detail: string.Join(" | ", ex.Errors),
                statusCode: 502);
        }
    }
}
