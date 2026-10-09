using System.Net;
using System.Text.Json;
using Microsoft.eShopWeb.SquareCheck.SignIn;
using Microsoft.eShopWeb.SquareCheck.SquareAccess;
using Square.Core.ErrorResponse;
using Square.Core.Exceptions;

namespace Microsoft.eShopWeb.SquareCheck;

/// <summary>
/// How a failed run ends: the exit code, the one line the operator reads, and the page the browser shows.
/// </summary>
public sealed record Failure(int ExitCode, string Message, BrowserPage Page);

/// <summary>
/// Turns whatever stopped the run into one plain line and an exit code — never a stack trace.
/// </summary>
public static class FailureClassifier
{
    private const int MaxDetailLength = 300;

    public static Failure Classify(Exception exception, CancellationToken userCancellation, TimeSpan requestTimeout)
    {
        if (userCancellation.IsCancellationRequested)
        {
            return new Failure(ExitCodes.Cancelled, "Cancelled.", BrowserPage.Expired);
        }

        var cause = Unwrap(exception);
        return cause switch
        {
            SignInDeclinedException declined => new Failure(
                ExitCodes.SignInNotCompleted,
                Sentence("Sign-in declined: access to the Square account was not approved" + DescribeOAuthError(declined)),
                BrowserPage.Declined),

            SignInTimedOutException timedOut => new Failure(
                ExitCodes.SignInNotCompleted,
                $"Sign-in not completed: nobody finished signing in within {BrowserSignIn.Describe(timedOut.Timeout)}.",
                BrowserPage.Expired),

            SignInAlreadyAttemptedException => new Failure(
                ExitCodes.SquareFailure,
                "Square refused the access granted by this sign-in and asked to sign in again; SquareCheck signs in only once per run.",
                BrowserPage.Failed),

            ApiException<RawError> api => new Failure(
                ExitCodes.SquareFailure,
                Sentence($"Square refused the {Subject(api)} ({Call(api)}): HTTP {(int)api.StatusCode} {api.StatusCode}{SquareErrorDetail(api.Error)}"),
                BrowserPage.Failed),

            ResponseDeserializationException unreadable => new Failure(
                ExitCodes.SquareFailure,
                $"Square sent a response SquareCheck could not read ({Call(unreadable)}, HTTP {(int)unreadable.StatusCode} {unreadable.StatusCode}).",
                BrowserPage.Failed),

            ApiException api => new Failure(
                ExitCodes.SquareFailure,
                $"Square failed the {Subject(api)} ({Call(api)}): HTTP {(int)api.StatusCode} {api.StatusCode}.",
                BrowserPage.Failed),

            SdkTimeoutException timeout => new Failure(
                ExitCodes.SquareFailure,
                $"Square did not respond in time ({Call(timeout)}).",
                BrowserPage.Failed),

            SdkConnectionException connection => new Failure(
                ExitCodes.SquareFailure,
                $"Could not reach Square ({Call(connection)}): {OneLine(connection.InnerException?.Message ?? connection.Message)}",
                BrowserPage.Failed),

            SdkException sdk => new Failure(
                ExitCodes.SquareFailure,
                $"Could not complete the request to Square ({Call(sdk)}): {OneLine(sdk.InnerException?.Message ?? sdk.Message)}",
                BrowserPage.Failed),

            UnexpectedSquareResponseException unexpected => new Failure(
                ExitCodes.SquareFailure,
                OneLine(unexpected.Message),
                BrowserPage.Failed),

            // Our own deadline (not the operator's Ctrl+C, handled above).
            OperationCanceledException => new Failure(
                ExitCodes.SquareFailure,
                $"Square did not respond within {BrowserSignIn.Describe(requestTimeout)}.",
                BrowserPage.Failed),

            _ => new Failure(
                ExitCodes.SquareFailure,
                $"SquareCheck failed unexpectedly: {OneLine(cause.Message)}",
                BrowserPage.Failed),
        };
    }

    /// <summary>
    /// The SDK reports anything thrown while applying credentials — our sign-in prompt, or the token
    /// exchange it triggers — as an <see cref="AuthSchemeException"/> whose cause is the real failure.
    /// </summary>
    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is AuthSchemeException auth)
        {
            var inner = auth.SchemeFailures.FirstOrDefault(f => f is not null) ?? auth.InnerException;
            if (inner is null)
            {
                break;
            }

            current = inner is AggregateException aggregate ? aggregate.InnerExceptions.FirstOrDefault() ?? inner : inner;
        }

        return current;
    }

    private static string Subject(SdkException exception) =>
        exception.RequestUri.AbsolutePath.StartsWith("/oauth2/", StringComparison.OrdinalIgnoreCase)
            ? "sign-in"
            : "request";

    private static string Call(SdkException exception) =>
        $"{exception.Method} {exception.RequestUri.AbsolutePath}";

    private static string DescribeOAuthError(SignInDeclinedException declined)
    {
        if (string.IsNullOrEmpty(declined.Error))
        {
            return string.Empty;
        }

        var description = string.IsNullOrWhiteSpace(declined.ErrorDescription) ? "" : $": {declined.ErrorDescription}";
        return $" (Square reported {OneLine(declined.Error + description)})";
    }

    /// <summary>
    /// Square's error bodies list <c>errors[]</c> with <c>code</c> and <c>detail</c>; any other body is ignored.
    /// </summary>
    internal static string SquareErrorDetail(RawError error)
    {
        try
        {
            using var document = JsonDocument.Parse(error.ReadAsString());
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("errors", out var errors) &&
                errors.ValueKind == JsonValueKind.Array &&
                errors.GetArrayLength() > 0)
            {
                var first = errors[0];
                var code = first.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
                var detail = first.TryGetProperty("detail", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                var text = string.Join(": ", new[] { code, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return text.Length == 0 ? string.Empty : $" – {OneLine(text)}";
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy or gateway page): the status line is all we can report.
        }

        return string.Empty;
    }

    /// <summary>Ends with a period unless the text (often Square's own detail) already ends a sentence.</summary>
    private static string Sentence(string text) =>
        text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?') || text.EndsWith('…') ? text : text + ".";

    internal static string OneLine(string text)
    {
        var cleaned = new string(text.Select(ch => char.IsControl(ch) ? ' ' : ch).ToArray()).Trim();
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        return cleaned.Length <= MaxDetailLength ? cleaned : cleaned[..MaxDetailLength] + "…";
    }
}
