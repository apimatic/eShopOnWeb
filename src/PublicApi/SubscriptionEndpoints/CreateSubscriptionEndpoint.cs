using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan. Safe to repeat: a second request for the same shopper never
/// creates a second Maxio customer or subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ShopperContext, ISubscriptionService>
{
    private const int MaxNameLength = 100;

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, [AsParameters] ShopperContext shopper, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, shopper, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status202Accepted)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status400BadRequest)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status409Conflict)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ShopperContext shopper, ISubscriptionService subscriptionService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var planHandle = request.PlanHandle?.Trim();
        if (string.IsNullOrEmpty(planHandle))
        {
            response.Outcome = nameof(SubscribeOutcome.UnknownPlan);
            response.Message = "planHandle is required. Choose one from GET /api/subscription-plans.";
            return Results.BadRequest(response);
        }

        if (request.FirstName?.Length > MaxNameLength || request.LastName?.Length > MaxNameLength)
        {
            response.Message = $"firstName and lastName must be at most {MaxNameLength} characters.";
            return Results.BadRequest(response);
        }

        var account = await shopper.FindAccountAsync();
        if (account is null)
        {
            return Results.Unauthorized();
        }

        var email = string.IsNullOrWhiteSpace(account.Email) ? account.UserName! : account.Email;
        var subscriber = new Subscriber(
            BuyerId: account.UserName!,
            Email: email,
            FirstName: NameOrDefault(request.FirstName, DefaultFirstName(email)),
            LastName: NameOrDefault(request.LastName, "Customer"));

        var result = await subscriptionService.SubscribeAsync(subscriber, planHandle, shopper.CancellationToken);

        response.Outcome = result.Outcome.ToString();
        response.Message = result.Message;
        response.Subscription = result.Subscription is null ? null : SubscriptionDto.FromModel(result.Subscription);

        return result.Outcome switch
        {
            SubscribeOutcome.Created => Results.Created("api/my-subscriptions", response),
            SubscribeOutcome.AlreadySubscribed => Results.Ok(response),
            SubscribeOutcome.OutcomeUnknown => Results.Accepted("api/my-subscriptions", response),
            SubscribeOutcome.UnknownPlan => Results.BadRequest(response),
            _ => Results.Conflict(response) // SubscribedToDifferentPlan, InProgress
        };
    }

    private static string NameOrDefault(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static string DefaultFirstName(string email)
    {
        var at = email.IndexOf('@', StringComparison.Ordinal);
        var localPart = at > 0 ? email[..at] : email;
        return localPart.Length > MaxNameLength ? localPart[..MaxNameLength] : localPart;
    }
}
