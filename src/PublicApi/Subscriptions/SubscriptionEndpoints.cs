using System;
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
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public record CreateSubscriptionRequest(string ProductHandle);

public sealed class SubscriptionEndpoints : IEndpoint<IResult, HttpContext, SubscriptionService, UserManager<ApplicationUser>>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/subscription-plans", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext context, SubscriptionService service, UserManager<ApplicationUser> users) => Run(context, users, (_, ct) => service.Plans(ct))).WithTags("Subscriptions");
        app.MapPost("api/subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (CreateSubscriptionRequest request, HttpContext context, SubscriptionService service, UserManager<ApplicationUser> users) => Run(context, users, (user, ct) => service.Enroll(user, request.ProductHandle, ct))).WithTags("Subscriptions");
        app.MapGet("api/my-subscriptions", [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
            (HttpContext context, SubscriptionService service, UserManager<ApplicationUser> users) => HandleAsync(context, service, users)).WithTags("Subscriptions");
    }

    public Task<IResult> HandleAsync(HttpContext context, SubscriptionService service, UserManager<ApplicationUser> users) => Run(context, users, (user, ct) => service.Mine(user, ct));

    private static async Task<IResult> Run<T>(HttpContext context, UserManager<ApplicationUser> users, Func<ApplicationUser, CancellationToken, Task<T>> action)
    {
        var name = context.User.Identity?.Name;
        if (string.IsNullOrEmpty(name)) return Results.Unauthorized();
        var user = await users.FindByNameAsync(name);
        if (user == null) return Results.Unauthorized();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(60));
        try { return Results.Ok(await action(user, budget.Token)); }
        catch (BillingException ex) { return Error(context, ex.Code, ex.Status); }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateCustomerError> ex)
        {
            if (ex.Error.TryGetCustomerErrorResponse1(out _)) return Error(context, "billing-customer-rejected", 422);
            if (ex.Error.TryGetRawError(out _)) return ProviderError(context, ex);
            return ProviderError(context, ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.CreateSubscriptionError> ex)
        {
            if (ex.Error.TryGetErrorListResponse1(out _)) return Error(context, "billing-subscription-rejected", 422);
            if (ex.Error.TryGetRawError(out _)) return ProviderError(context, ex);
            return ProviderError(context, ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException<MaxioAdvancedBilling.Errors.ListProductsForProductFamilyError> ex)
        {
            if (ex.Error.TryGetString(out _)) return Error(context, "billing-catalog-unavailable", 502);
            if (ex.Error.TryGetRawError(out _)) return ProviderError(context, ex);
            return ProviderError(context, ex);
        }
        catch (MaxioAdvancedBilling.Core.Exceptions.ApiException ex) { return ProviderError(context, ex); }
        catch (MaxioAdvancedBilling.Core.Exceptions.SdkException) { return Error(context, "billing-unavailable", 503); }
        catch (OperationCanceledException) { return Error(context, "billing-timeout", 504); }
    }

    private static IResult ProviderError(HttpContext context, MaxioAdvancedBilling.Core.Exceptions.ApiException ex)
    {
        var status = (int)ex.StatusCode;
        return Error(context, "billing-provider-error", status == 429 ? 503 : status is 400 or 422 ? 422 : 502);
    }

    private static IResult Error(HttpContext context, string code, int status)
    {
        var log = (ILogger<SubscriptionEndpoints>?)context.RequestServices.GetService(typeof(ILogger<SubscriptionEndpoints>));
        log?.LogWarning("Billing operation returned {Code} with status {Status}; trace {TraceId}", code, status, context.TraceIdentifier);
        return Results.Problem(statusCode: status, title: code, extensions: new System.Collections.Generic.Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier });
    }
}
