using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.Extensions.Logging;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Turns billing failures into problem responses. Callers get a status code they can act on
/// and the provider's own message when it is useful, while transport details stay in the log.
/// </summary>
public static class SubscriptionProblems
{
    /// <summary>
    /// Runs an endpoint body and maps the known billing failures onto problem responses.
    /// </summary>
    public static async Task<IResult> ExecuteAsync(Func<Task<IResult>> handler, ILogger logger, Guid correlationId)
    {
        try
        {
            return await handler();
        }
        catch (UnknownSubscriberException ex)
        {
            logger.LogWarning(ex, "Subscription request {CorrelationId} could not be attributed to a user.", correlationId);
            return Problem(StatusCodes.Status401Unauthorized, "Unknown subscriber", ex.Message, correlationId);
        }
        catch (SubscriptionPlanNotFoundException ex)
        {
            logger.LogWarning("Subscription request {CorrelationId} named unknown plan '{PlanHandle}'.", correlationId, ex.PlanHandle);
            return Problem(StatusCodes.Status404NotFound, "Unknown subscription plan", ex.Message, correlationId);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Subscription request {CorrelationId} was invalid.", correlationId);
            return Problem(StatusCodes.Status400BadRequest, "Invalid request", ex.Message, correlationId);
        }
        catch (BillingConfigurationException ex)
        {
            logger.LogError(ex, "Subscription request {CorrelationId} could not be served: billing is not configured.", correlationId);
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                "Subscription billing unavailable",
                "Subscription billing is not configured on this server.",
                correlationId,
                ex.Errors);
        }
        catch (BillingProviderException ex)
        {
            var status = ex.IsRequestRejected ? StatusCodes.Status422UnprocessableEntity : StatusCodes.Status502BadGateway;
            logger.LogError(ex, "Subscription request {CorrelationId} failed at the billing provider.", correlationId);
            return Problem(
                status,
                ex.IsRequestRejected ? "Billing provider rejected the request" : "Billing provider unavailable",
                ex.Message,
                correlationId,
                ex.ProviderErrors);
        }
    }

    private static IResult Problem(
        int statusCode,
        string title,
        string detail,
        Guid correlationId,
        IReadOnlyCollection<string>? errors = null)
    {
        var extensions = new Dictionary<string, object?> { ["correlationId"] = correlationId };
        if (errors is { Count: > 0 })
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(statusCode: statusCode, title: title, detail: detail, extensions: extensions);
    }
}
