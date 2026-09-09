using System;
using System.Collections.Generic;
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
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan (identified by its Maxio
/// product handle). The operation is idempotent: a repeated call returns the
/// existing subscription instead of creating a second one.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, IMaxioBillingService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CreateSubscriptionEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, IMaxioBillingService billingService) =>
            {
                return await HandleAsync(request, billingService);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, IMaxioBillingService billingService)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        if (string.IsNullOrWhiteSpace(request.PlanHandle))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [nameof(request.PlanHandle)] = new[] { "PlanHandle is required." }
            });
        }

        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("No active HTTP context.");
        var appUser = await CurrentUser.ResolveAsync(context.User, context.RequestServices);
        if (appUser == null)
        {
            return Results.Unauthorized();
        }

        var email = appUser.Email ?? appUser.UserName
            ?? throw new InvalidOperationException($"User {appUser.Id} has neither email nor username.");

        try
        {
            var subscription = await billingService.SubscribeAsync(
                appUser.Id, email, request.PlanHandle.Trim(), context.RequestAborted);

            response.Subscription = new SubscriptionDto
            {
                Id = subscription.Id,
                Reference = subscription.Reference,
                PlanHandle = subscription.PlanHandle,
                PlanName = subscription.PlanName,
                Price = subscription.Price,
                State = subscription.State,
                NextBillingDate = subscription.NextBillingDate,
                CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
                Balance = subscription.Balance
            };
            return Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioBillingException ex)
        {
            return MaxioBillingResults.Problem(ex, response.CorrelationId());
        }
    }
}
