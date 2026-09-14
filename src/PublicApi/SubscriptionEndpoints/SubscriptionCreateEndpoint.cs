using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AutoMapper;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;
using Swashbuckle.AspNetCore.Annotations;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated shopper to a plan.
/// </summary>
/// <remarks>
/// The call is idempotent per shopper and plan: a shopper who is already subscribed gets their
/// existing subscription back with 200 OK, so a double-click cannot produce two enrolments or two
/// billing customers. Callers that want to guard a retry explicitly may send an
/// <c>Idempotency-Key</c> header.
/// </remarks>
public class SubscriptionCreateEndpoint : IEndpoint<IResult, SubscribeRequest, ISubscriberResolver, ISubscriptionService>
{
    /// <summary>Header a caller may send to collapse retries of the same logical subscribe request.</summary>
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IMapper _mapper;

    public SubscriptionCreateEndpoint(IMapper mapper)
    {
        _mapper = mapper;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (SubscribeRequestBody? body,
             ClaimsPrincipal user,
             HttpContext httpContext,
             ISubscriberResolver subscriberResolver,
             ISubscriptionService subscriptionService,
             CancellationToken cancellationToken) =>
            {
                var idempotencyKey = httpContext.Request.Headers[IdempotencyKeyHeader].ToString();

                var request = new SubscribeRequest(
                    user.Identity?.Name,
                    body?.PlanHandle,
                    string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                    cancellationToken);

                return await HandleAsync(request, subscriberResolver, subscriptionService);
            })
            .Produces<SubscribeResponse>(StatusCodes.Status201Created)
            .Produces<SubscribeResponse>(StatusCodes.Status200OK)
            .WithTags("SubscriptionEndpoints")
            .WithMetadata(new SwaggerOperationAttribute(
                summary: "Subscribes the caller to a plan",
                description: "Ensures a billing customer exists for the caller and enrols them in the requested plan. " +
                             "Idempotent: returns 200 with the existing subscription if the caller is already subscribed, 201 otherwise.")
            {
                OperationId = "subscriptions.subscribe"
            });
    }

    public async Task<IResult> HandleAsync(
        SubscribeRequest request,
        ISubscriberResolver subscriberResolver,
        ISubscriptionService subscriptionService)
    {
        var response = new SubscribeResponse(request.CorrelationId());

        var subscriber = await subscriberResolver.ResolveAsync(request.UserName ?? string.Empty, request.CancellationToken);
        if (subscriber is null)
        {
            return Results.Unauthorized();
        }

        // Deliberately not forwarding the request-aborted token to the enrolment itself: abandoning a
        // subscribe mid-flight would leave the caller unable to tell whether Maxio had accepted it.
        var result = await subscriptionService.SubscribeAsync(subscriber, request.PlanHandle, request.IdempotencyKey);

        response.Subscription = _mapper.Map<SubscriptionDto>(result.Subscription);
        response.AlreadySubscribed = result.AlreadySubscribed;
        response.Message = BuildMessage(result.AlreadySubscribed, response.Subscription);

        return result.AlreadySubscribed
            ? Results.Ok(response)
            : Results.Created($"/api/my-subscriptions#{result.Subscription.Id.ToString(CultureInfo.InvariantCulture)}", response);
    }

    private static string BuildMessage(bool alreadySubscribed, SubscriptionDto subscription)
    {
        var verb = alreadySubscribed ? "You are already subscribed to" : "You are now subscribed to";
        var price = subscription.Price.ToString("0.00", CultureInfo.InvariantCulture);
        var period = string.IsNullOrEmpty(subscription.BillingPeriod) ? string.Empty : $" {subscription.BillingPeriod}";
        var nextBilling = subscription.NextBillingAt is { } next
            ? $" Next billing date: {next.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}."
            : string.Empty;

        return $"{verb} {subscription.PlanName} at {subscription.Currency} {price}{period}. " +
               $"Status: {subscription.State}.{nextBilling}";
    }
}
