using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.DigitalFileEndpoints;

/// <summary>
/// Maps digital file storage failures to caller-facing responses. None of them is the caller's fault, so all
/// are 5xx; the message is the caller-safe one carried by the exception.
/// </summary>
public static class StorageProblem
{
    public static IResult From(DigitalFileStorageException exception) => Results.Problem(
        title: "The merchant's file storage is unavailable.",
        detail: exception.Message,
        statusCode: exception.Failure switch
        {
            DigitalFileStorageFailure.Timeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway,
        });
}
