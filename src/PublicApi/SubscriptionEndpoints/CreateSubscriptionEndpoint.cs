using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.PublicApi.Maxio;
using Microsoft.Extensions.Options;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Subscribes the authenticated user to a plan. Idempotent: the caller's Maxio customer
/// is looked up (or created) by reference, and if an active subscription to the same plan
/// already exists it is returned instead of creating a duplicate.
/// </summary>
public class CreateSubscriptionEndpoint : IEndpoint<IResult, CreateSubscriptionRequest, ClaimsPrincipal, IMaxioClient>
{
    // States in which a subscription still entitles the user; used for idempotent replay.
    private static readonly string[] ActiveStates = { "active", "trialing", "past_due" };

    private readonly IOptions<MaxioSettings> _maxioSettings;

    public CreateSubscriptionEndpoint(IOptions<MaxioSettings> maxioSettings)
    {
        _maxioSettings = maxioSettings;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapPost("api/subscriptions",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, IMaxioClient maxioClient) =>
            {
                return await HandleAsync(request, claimsPrincipal, maxioClient);
            })
            .Produces<CreateSubscriptionResponse>()
            .WithTags("SubscriptionEndpoints");
    }

    public async Task<IResult> HandleAsync(CreateSubscriptionRequest request, ClaimsPrincipal claimsPrincipal, IMaxioClient maxioClient)
    {
        var response = new CreateSubscriptionResponse(request.CorrelationId());

        var username = claimsPrincipal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(username))
        {
            return Results.Unauthorized();
        }

        var family = await maxioClient.GetProductFamilyByHandleAsync(_maxioSettings.Value.ProductFamilyHandle);
        if (family == null)
        {
            return Results.Problem($"No Maxio product family found with handle '{_maxioSettings.Value.ProductFamilyHandle}'.");
        }

        var products = await maxioClient.GetProductsByFamilyAsync(family.Id);
        var plan = products.FirstOrDefault(p =>
            string.Equals(p.Handle, request.ProductHandle, StringComparison.OrdinalIgnoreCase) && p.ArchivedAt == null);
        if (plan == null)
        {
            return Results.NotFound($"No available subscription plan with handle '{request.ProductHandle}'.");
        }

        var customer = await maxioClient.GetOrCreateCustomerAsync(
            reference: username,
            email: username,
            firstName: username.Split('@')[0],
            lastName: "eShopOnWeb");

        var subscriptions = await maxioClient.GetCustomerSubscriptionsAsync(customer.Id);
        var existing = subscriptions.FirstOrDefault(s =>
            string.Equals(s.Product?.Handle, plan.Handle, StringComparison.OrdinalIgnoreCase)
            && s.State != null
            && ActiveStates.Contains(s.State, StringComparer.OrdinalIgnoreCase));

        if (existing != null)
        {
            response.Subscription = SubscriptionDto.FromMaxio(existing);
            response.AlreadySubscribed = true;
            return Results.Ok(response);
        }

        try
        {
            var created = await maxioClient.CreateSubscriptionAsync(customer.Id, plan.Handle!, reference: username);
            response.Subscription = SubscriptionDto.FromMaxio(created);
            return Results.Created("api/my-subscriptions", response);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            return Results.BadRequest(new { ex.Errors });
        }
    }
}
