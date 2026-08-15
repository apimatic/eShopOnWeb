using System;
using System.Collections.Generic;
using System.Linq;
using PayPalServerSdk.Core.ErrorResponse;
using PayPalServerSdk.Models;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Uniform view of a failed PayPal call, distilled from either the typed error body or the raw
/// response. Keeps the gateway's catch blocks small and lets it branch on well-known conditions
/// (expired authorization, non-reauthorizable) without repeating extraction logic.
/// </summary>
internal sealed record PayPalFailure(
    int? StatusCode,
    string? Name,
    string Message,
    string? DebugId,
    IReadOnlyList<string> Issues)
{
    private string Haystack => string.Join(" ",
        new[] { Name, Message }.Concat(Issues).Where(s => !string.IsNullOrEmpty(s))!)
        .ToUpperInvariant();

    /// <summary>True when the failure indicates the authorization is no longer valid to capture
    /// because its honor period has passed and it must be reauthorized.</summary>
    public bool IsAuthorizationExpired =>
        Haystack.Contains("AUTHORIZATION_EXPIRED") ||
        (Haystack.Contains("EXPIR") && Haystack.Contains("AUTH"));

    public override string ToString()
    {
        var issueText = Issues.Count > 0 ? $" [{string.Join("; ", Issues)}]" : string.Empty;
        var debug = string.IsNullOrEmpty(DebugId) ? string.Empty : $" (debug_id: {DebugId})";
        var status = StatusCode.HasValue ? $"HTTP {StatusCode} " : string.Empty;
        return $"{status}{Name}: {Message}{issueText}{debug}".Trim();
    }
}

internal static class PayPalErrorTranslator
{
    /// <summary>
    /// Build a <see cref="PayPalFailure"/> from an SDK error. <paramref name="typedError"/> is the
    /// operation-specific typed body (may be null); every error type also exposes the raw fallback
    /// through its <see cref="ApiError"/> base, which gives the status code and raw body.
    /// </summary>
    public static PayPalFailure Translate(ApiError apiError, Error? typedError, string fallbackMessage)
    {
        int? status = null;
        string? rawBody = null;
        if (apiError.TryGetRawError(out var raw))
        {
            status = (int)raw.StatusCode;
            try { rawBody = raw.ReadAsString(); } catch { /* body already consumed / not text */ }
        }

        var issues = typedError?.Details?
            .Select(d => d.Issue)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .ToList() ?? new List<string>();

        // A raw body (no typed parse) can still carry the issue string; fold it into the scan set.
        if (typedError is null && !string.IsNullOrWhiteSpace(rawBody))
        {
            issues.Add(rawBody!);
        }

        var message = typedError?.Message
            ?? (string.IsNullOrWhiteSpace(rawBody) ? null : rawBody)
            ?? fallbackMessage;

        return new PayPalFailure(status, typedError?.Name, message, typedError?.DebugId, issues);
    }
}
