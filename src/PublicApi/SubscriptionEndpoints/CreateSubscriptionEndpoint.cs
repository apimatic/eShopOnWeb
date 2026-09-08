using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: repeating the call for the same
/// user + plan returns the existing subscription rather than creating a duplicate.
/// POST /api/subscriptions
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, string>
{
    private readonly IMaxioSubscriptionService _subscriptionService;
    private readonly MaxioSettings _maxioSettings;

    public CreateSubscriptionEndpoint(IMaxioSubscriptionService subscriptionService, IOptions<MaxioSettings> maxioSettings)
    {
        _subscriptionService = subscriptionService;
        _maxioSettings = maxioSettings.Value;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal user) =>
            {
                var reference = SubscriberIdentity.RequireUserReference(user);
                return await HandleAsync(request, reference);
            })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, string userReference)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.BadRequest(new { error = "A 'planHandle' is required." });
        }

        var planHandle = request.PlanHandle.Trim();

        // Validate the plan belongs to the configured family before enrolling — turns a Maxio 422
        // into a clear 400 for an unknown plan.
        var plans = await _subscriptionService.GetAvailablePlansAsync(_maxioSettings.ProductFamilyHandle ?? string.Empty);
        if (!plans.Any(p => string.Equals(p.Handle, planHandle, StringComparison.OrdinalIgnoreCase)))
        {
            return Results.BadRequest(new { error = $"Unknown plan '{planHandle}'.", availablePlans = plans.Select(p => p.Handle) });
        }

        var customer = SubscriberIdentity.ToCustomerInput(userReference);

        try
        {
            var subscription = await _subscriptionService.SubscribeAsync(customer, planHandle);
            response.Subscription = SubscriptionDto.From(subscription);
            return Results.Created($"api/my-subscriptions/{subscription.Id}", response);
        }
        catch (MaxioApiException ex)
        {
            return Results.Problem(
                detail: string.Join(" ", ex.Errors.DefaultIfEmpty(ex.Message)),
                statusCode: (int)System.Net.HttpStatusCode.BadGateway,
                title: "Maxio subscription could not be created.");
        }
    }
}
