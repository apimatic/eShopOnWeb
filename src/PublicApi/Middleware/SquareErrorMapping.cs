using System;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.Infrastructure.SquareIntegration;

namespace Microsoft.eShopWeb.PublicApi.Middleware;

/// <summary>
/// The one mapping from Square integration failures to HTTP statuses. Messages are caller-safe
/// (built by the integration; never an SDK exception message).
/// </summary>
public static class SquareErrorMapping
{
    public static bool TryMap(Exception exception, CancellationToken requestAborted, out int statusCode, out string message)
    {
        switch (exception)
        {
            case SquareRequestException request:
                statusCode = request.StatusCode;
                message = request.Message;
                return true;

            case SquareOperationInProgressException inProgress:
                statusCode = StatusCodes.Status409Conflict;
                message = inProgress.Message;
                return true;

            case SquareIntegrationException square:
                statusCode = square.Kind switch
                {
                    // The shop is not connected — an operator must act; the caller can retry later.
                    SquareFailureKind.NotConnected => StatusCodes.Status503ServiceUnavailable,
                    // Our credentials or our request were refused by Square: not the caller's fault.
                    SquareFailureKind.AuthorizationFailed => StatusCodes.Status502BadGateway,
                    SquareFailureKind.Rejected => StatusCodes.Status502BadGateway,
                    SquareFailureKind.UnreadableResponse => StatusCodes.Status502BadGateway,
                    // Throttled, failing or unreachable.
                    SquareFailureKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
                    // Square did not confirm a write.
                    SquareFailureKind.OutcomeUnknown => StatusCodes.Status504GatewayTimeout,
                    _ => StatusCodes.Status502BadGateway,
                };
                message = square.Message;
                return true;

            case OperationCanceledException when !requestAborted.IsCancellationRequested:
                // Our own request budget ran out while waiting for Square.
                statusCode = StatusCodes.Status504GatewayTimeout;
                message = "Square did not answer in time. Check the result before retrying.";
                return true;

            default:
                statusCode = 0;
                message = string.Empty;
                return false;
        }
    }
}
