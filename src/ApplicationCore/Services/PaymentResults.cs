using System.Collections.Generic;
using Ardalis.Result;

namespace Microsoft.eShopWeb.ApplicationCore.Services;

/// <summary>Small helpers for building <see cref="Result"/> values used by the payment services.</summary>
public static class PaymentResults
{
    public static Result<T> Invalid<T>(string message) =>
        Result<T>.Invalid(new List<ValidationError>
        {
            new() { ErrorMessage = message, Severity = ValidationSeverity.Error }
        });

    public static Result Invalid(string message) =>
        Result.Invalid(new List<ValidationError>
        {
            new() { ErrorMessage = message, Severity = ValidationSeverity.Error }
        });
}
