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
using Microsoft.eShopWeb.ApplicationCore.Models;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (idempotent)
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor, UserManager<ApplicationUser> userManager)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionService subscriptionService) =>
            {
                return await HandleAsync(request, subscriptionService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionService subscriptionService)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var user = httpContext?.User;
        var userName = user?.FindFirst(ClaimTypes.Name)?.Value;
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var applicationUser = await _userManager.FindByNameAsync(userName);
        if (applicationUser is null)
        {
            return Results.Unauthorized();
        }

        SubscriptionSummaryDto dto;
        try
        {
            var summary = await subscriptionService.SubscribeAsync(
                SubscriberProfiles.FromUser(applicationUser),
                request.PlanHandle,
                httpContext?.RequestAborted ?? default);
            dto = ToDto(summary);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingProblem.From(ex);
        }

        var response = new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = dto
        };
        return Results.Ok(response);
    }

    private static SubscriptionSummaryDto ToDto(SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        Reference = summary.Reference,
        State = summary.State,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        PriceInCents = summary.PriceInCents,
        NextBillingDate = summary.NextBillingDate,
        NextAssessmentAt = summary.NextAssessmentAt,
        CustomerId = summary.CustomerId,
        CustomerReference = summary.CustomerReference
    };
}