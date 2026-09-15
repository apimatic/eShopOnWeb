using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore;

internal static class AppResult
{
    public static Result<T> Invalid<T>(string message) =>
        Result<T>.Invalid(new List<ValidationError> { new() { ErrorMessage = message } });

    public static Result Invalid(string message) =>
        Result.Invalid(new List<ValidationError> { new() { ErrorMessage = message } });

    public static Result<T> Conflict<T>(string message) => Invalid<T>(message);
}
