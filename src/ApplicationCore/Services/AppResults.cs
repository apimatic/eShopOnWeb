using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

internal static class AppResults
{
    public static Result<T> Invalid<T>(string identifier, string message)
    {
        return Result<T>.Invalid(new List<ValidationError>
        {
            new() { Identifier = identifier, ErrorMessage = message }
        });
    }

    public static Result Invalid(string identifier, string message)
    {
        return Result.Invalid(new List<ValidationError>
        {
            new() { Identifier = identifier, ErrorMessage = message }
        });
    }
}
