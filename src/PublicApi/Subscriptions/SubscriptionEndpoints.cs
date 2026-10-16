using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.Infrastructure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed record SubscribeRequest(string ProductHandle);

public sealed class SubscriptionEndpoints : IEndpoint<IResult, HttpContext>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Subscriptions")
            .RequireAuthorization(new AuthorizeAttribute { AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme });
        group.MapGet("/subscription-plans", (Func<HttpContext, Task<IResult>>)(context => ExecuteAsync(context, "plans")))
            .Produces<SubscriptionPlan[]>().ProducesProblem(503);
        group.MapPost("/subscriptions", (SubscribeRequest request, HttpContext context) => ExecuteAsync(context, "subscribe", request))
            .Produces<SubscribeResult>(201).Produces<SubscribeResult>().Produces<SubscribeResult>(202)
            .ProducesProblem(400).ProducesProblem(422).ProducesProblem(503);
        group.MapGet("/my-subscriptions", (Func<HttpContext, Task<IResult>>)HandleAsync)
            .Produces<MySubscriptions>().ProducesProblem(503);
    }

    public Task<IResult> HandleAsync(HttpContext context) => ExecuteAsync(context, "mine");

    private static async Task<IResult> ExecuteAsync(HttpContext context, string operation, SubscribeRequest? request = null)
    {
        var users = context.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId == null ? null : await users.FindByIdAsync(userId);
        if (user == null) return Results.Unauthorized();
        if (operation == "subscribe" && (string.IsNullOrWhiteSpace(request?.ProductHandle) || request.ProductHandle.Length > 255))
            return Results.Problem("A productHandle from subscription-plans is required.", statusCode: 400);
        var service = context.RequestServices.GetRequiredService<SubscriptionService>();
        var logger = context.RequestServices.GetRequiredService<ILogger<SubscriptionEndpoints>>();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(45));
        try
        {
            if (operation == "plans") return Results.Ok(await service.PlansAsync(budget.Token));
            if (operation == "mine") return Results.Ok(await service.MineAsync(user, budget.Token));
            var result = await service.SubscribeAsync(user, request!.ProductHandle, budget.Token);
            logger.LogInformation("Subscription request completed with {Status}; trace {TraceId}.", result.Status, context.TraceIdentifier);
            if (result.Status == "created") return Results.Created("/api/my-subscriptions", result);
            if (result.Status == "pending")
            {
                context.Response.Headers.RetryAfter = "5";
                return Results.Accepted("/api/my-subscriptions", result);
            }
            return Results.Ok(result);
        }
        catch (InvalidPlanException ex) { return Results.Problem(ex.Message, statusCode: 400); }
        catch (BillingRejectedException ex) { return Results.Problem(ex.Message, statusCode: 422); }
        catch (Exception ex) when (ex is MaxioAdvancedBilling.Core.Exceptions.SdkException or BillingUnavailableException or OperationCanceledException)
        {
            var status = ex is MaxioAdvancedBilling.Core.Exceptions.ApiException api ? (int?)api.StatusCode : null;
            string? providerRequestId = null;
            if (ex is MaxioAdvancedBilling.Core.Exceptions.ApiException response && response.Headers.TryGetValues("X-Request-Id", out var values))
                providerRequestId = string.Join(",", values);
            logger.LogWarning("Billing unavailable ({FailureType}, HTTP {Status}, provider request {ProviderRequestId}); trace {TraceId}.", ex.GetType().Name, status, providerRequestId, context.TraceIdentifier);
            return Results.Problem("Billing is temporarily unavailable. Please try again shortly.", statusCode: 503,
                extensions: new System.Collections.Generic.Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
        }
    }
}
