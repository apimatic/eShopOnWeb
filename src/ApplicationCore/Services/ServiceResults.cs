using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>
/// Small helpers over <see cref="Result"/> to keep call sites terse. In this version of
/// Ardalis.Result, <c>Invalid</c> takes a list of <see cref="ValidationError"/>; these wrap that.
/// </summary>
internal static class ServiceResults
{
    public static Result<T> Invalid<T>(string message) =>
        Result<T>.Invalid(new List<ValidationError> { new() { ErrorMessage = message } });

    public static Result Invalid(string message) =>
        Result.Invalid(new List<ValidationError> { new() { ErrorMessage = message } });
}
