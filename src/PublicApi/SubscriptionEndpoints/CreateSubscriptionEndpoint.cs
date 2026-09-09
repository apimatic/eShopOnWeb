using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.Infrastructure.Identity;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: a repeated request for a plan the
/// user already holds returns the existing subscription instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
    {
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billing) =>
            {
                return await HandleAsync(request, billing);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ISubscriptionBillingService billing)
    {
        if (string.IsNullOrWhiteSpace(request.ProductHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["productHandle"] = new[] { "A plan handle is required." }
            });
        }

        var httpContext = _httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        var userInfo = principal is null
            ? null
            : await SubscriptionUserResolver.ResolveAsync(principal, _userManager);
        if (userInfo is null)
        {
            return Results.Unauthorized();
        }

        try
        {
            var result = await billing.SubscribeAsync(
                userInfo,
                request.ProductHandle,
                httpContext?.RequestAborted ?? CancellationToken.None);

            var response = new CreateSubscriptionResponse(request.CorrelationId())
            {
                Subscription = result.Subscription.ToDto()
            };
            response.Subscription.AlreadySubscribed = result.AlreadySubscribed;

            return result.AlreadySubscribed
                ? Results.Ok(response)
                : Results.Created("api/my-subscriptions", response);
        }
        catch (ApplicationCore.Exceptions.MaxioBillingException ex)
        {
            return ex.ToProblemResult();
        }
    }
}
