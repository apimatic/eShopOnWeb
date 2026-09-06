using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Result of a subscription operation. Expected failures (bad plan handle, upstream
/// validation, upstream outage) are values rather than exceptions so that the API layer can
/// translate each one into the right status code without catching infrastructure types.
/// </summary>
public sealed class SubscriptionResult<T>
{
    private static readonly IReadOnlyList<string> NoErrors = Array.Empty<string>();

    private SubscriptionResult(bool isSuccess, T? value, SubscriptionFailure failure, string message, IReadOnlyList<string> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
        Message = message;
        Errors = errors;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public SubscriptionFailure Failure { get; }

    /// <summary>Human readable summary, safe to return to the caller. Empty when successful.</summary>
    public string Message { get; }

    /// <summary>Individual messages reported by the billing system, when it supplied any.</summary>
    public IReadOnlyList<string> Errors { get; }

    public static SubscriptionResult<T> Success(T value) =>
        new(true, value, SubscriptionFailure.None, string.Empty, NoErrors);

    public static SubscriptionResult<T> Failed(SubscriptionFailure failure, string message, IReadOnlyList<string>? errors = null)
    {
        if (failure == SubscriptionFailure.None)
        {
            throw new ArgumentException("A failed result requires a failure reason.", nameof(failure));
        }

        return new SubscriptionResult<T>(false, default, failure, message, errors ?? NoErrors);
    }
}
