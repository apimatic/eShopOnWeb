using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Ardalis.Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.SubscriptionBilling;
using MinimalApi.Endpoint;
using IResult = Microsoft.AspNetCore.Http.IResult;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Enrolls the authenticated user in a subscription plan. Idempotent: if the user
/// already has a live subscription on the plan, the existing subscription is
/// returned (AlreadySubscribed = true) and nothing new is created.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ISubscriptionBillingService, IHttpContextAccessor>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ISubscriptionBillingService billingService, IHttpContextAccessor httpContextAccessor) =>
            {
                return await HandleAsync(request, billingService, httpContextAccessor);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(
        CreateSubscriptionRequest request, ISubscriptionBillingService billingService, IHttpContextAccessor httpContextAccessor)
    {
        var userName = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.Name);
        if (string.IsNullOrWhiteSpace(userName))
        {
            return Results.Unauthorized();
        }

        var result = await billingService.SubscribeAsync(
            userName, request.PlanHandle ?? string.Empty, request.IdempotencyKey, CancellationToken.None);

        if (result.Status == ResultStatus.Invalid)
        {
            return Results.BadRequest(new
            {
                correlationId = request.CorrelationId(),
                errors = result.ValidationErrors.Select(e => e.ErrorMessage)
            });
        }

        if (result.Status == ResultStatus.NotFound)
        {
            return Results.NotFound(new { correlationId = request.CorrelationId(), errors = result.Errors });
        }

        if (result.Status != ResultStatus.Ok)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status502BadGateway,
                title: "Billing system error",
                detail: string.Join("; ", result.Errors));
        }

        var enrollment = result.Value;
        return Results.Ok(new CreateSubscriptionResponse(request.CorrelationId())
        {
            Subscription = ToDto(enrollment.Subscription),
            BillingCustomerId = enrollment.BillingCustomerId,
            BillingCustomerReference = enrollment.BillingCustomerReference,
            AlreadySubscribed = enrollment.AlreadySubscribed
        });
    }

    internal static SubscriptionSummaryDto ToDto(SubscriptionSummary summary) => new()
    {
        SubscriptionId = summary.SubscriptionId,
        PlanHandle = summary.PlanHandle,
        PlanName = summary.PlanName,
        State = summary.State,
        PriceCents = summary.PriceCents,
        Price = (summary.PriceCents / 100m).ToString("F2"),
        NextBillingAt = summary.NextBillingAt,
        ActivatedAt = summary.ActivatedAt,
        CanceledAt = summary.CanceledAt,
        CreatedAt = summary.CreatedAt
    };
}
