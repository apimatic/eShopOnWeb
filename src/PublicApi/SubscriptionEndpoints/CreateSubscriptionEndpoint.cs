using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.eShopWeb.Infrastructure.Maxio;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan. Idempotent: repeated
/// calls for the same user and plan return the existing subscription.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, SubscriptionEndpointServices>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            async (CreateSubscriptionRequest request, SubscriptionEndpointServices services) =>
            {
                return await HandleAsync(request, services);
            })
            .RequireAuthorization(new Microsoft.AspNetCore.Authorization.AuthorizeAttribute { AuthenticationSchemes = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme })
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status201Created)
            .Produces<CreateSubscriptionResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, SubscriptionEndpointServices services)
    {
        var user = await ResolveUserAsync(services, services.UserManager);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var summary = await services.SubscriptionService.SubscribeAsync(
                new SubscribeRequest(user.Id, user.Email ?? user.UserName ?? string.Empty, request.ProductHandle));

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = ToDto(summary)
            };

            return summary.Created ? Results.Created("/api/my-subscriptions", response) : Results.Ok(response);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            return Results.NotFound(new { correlationId = request.CorrelationId(), error = ex.Message });
        }
        catch (MaxioApiException ex)
        {
            return Results.Json(new
            {
                correlationId = request.CorrelationId(),
                error = $"The billing system rejected the subscription: {ex.ResponseBody}"
            }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    internal static async Task<ApplicationUser?> ResolveUserAsync(SubscriptionEndpointServices services,
        UserManager<ApplicationUser> userManager)
    {
        var username = services.Principal?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }
        return await userManager.FindByNameAsync(username);
    }

    internal static SubscriptionSummaryDto ToDto(ApplicationCore.Models.SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        Price = summary.Price,
        Currency = summary.Currency,
        State = summary.State,
        NextBillingDateUtc = summary.NextBillingDateUtc,
        Created = summary.Created
    };
}
