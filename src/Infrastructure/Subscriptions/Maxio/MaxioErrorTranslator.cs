using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using AdvancedBilling.Standard.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions.Maxio;

/// <summary>
/// Turns Maxio SDK failures into the vocabulary <see cref="SubscriptionBillingException"/> defines, so
/// nothing above the infrastructure layer needs to know about HTTP status codes or SDK exception types.
/// </summary>
internal static class MaxioErrorTranslator
{
    /// <summary>Maxio's message when a customer or subscription reference is already taken.</summary>
    private const string ReferenceTakenFragment = "must be unique";

    /// <summary>True when the failure is Maxio reporting "this reference already exists" (HTTP 422).</summary>
    public static bool IsReferenceConflict(ApiException exception) =>
        exception.ResponseCode == (int)HttpStatusCode.UnprocessableEntity &&
        ExtractErrors(exception).Any(error => error.IndexOf(ReferenceTakenFragment, StringComparison.OrdinalIgnoreCase) >= 0);

    /// <summary>True when Maxio answered "no such record".</summary>
    public static bool IsNotFound(ApiException exception) =>
        exception.ResponseCode == (int)HttpStatusCode.NotFound;

    /// <summary>Maps an SDK exception onto the matching application-level billing exception.</summary>
    public static SubscriptionBillingException Translate(ApiException exception, string operation)
    {
        var errors = ExtractErrors(exception);
        var detail = errors.Count > 0 ? string.Join(" ", errors) : exception.Message;

        return exception.ResponseCode switch
        {
            (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden =>
                new SubscriptionBillingNotConfiguredException(
                    $"Maxio rejected the configured API credentials while trying to {operation}."),

            (int)HttpStatusCode.UnprocessableEntity or (int)HttpStatusCode.BadRequest =>
                new SubscriptionBillingRejectedException(
                    $"Maxio rejected the request to {operation}: {detail}", errors, exception),

            (int)HttpStatusCode.TooManyRequests or >= 500 =>
                new SubscriptionBillingUnavailableException(
                    $"Maxio is currently unavailable (HTTP {exception.ResponseCode}) while trying to {operation}.",
                    exception),

            _ => new SubscriptionBillingException(
                $"Maxio returned an unexpected response (HTTP {exception.ResponseCode}) while trying to {operation}: {detail}",
                errors,
                exception),
        };
    }

    /// <summary>
    /// Reads the error messages out of a Maxio response body. Maxio uses several shapes across endpoints
    /// (<c>{"errors":["..."]}</c>, <c>{"errors":{"field":["..."]}}</c> and <c>{"error":"..."}</c>), and the
    /// SDK only surfaces some of them as typed properties, so the raw body is the reliable source.
    /// </summary>
    public static IReadOnlyList<string> ExtractErrors(ApiException exception)
    {
        if (exception is ErrorListResponseException { Errors: { Count: > 0 } typedErrors })
        {
            return typedErrors.Where(e => !string.IsNullOrWhiteSpace(e)).ToArray();
        }

        var body = exception.HttpContext?.Response?.Body;

        if (string.IsNullOrWhiteSpace(body))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var messages = new List<string>();
            Collect(document.RootElement, messages);
            return messages;
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static void Collect(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals("error") || property.NameEquals("errors"))
                    {
                        CollectValues(property.Value, messages);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, messages);
                }

                break;
        }
    }

    private static void CollectValues(JsonElement element, List<string> messages)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                var text = element.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text!);
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectValues(item, messages);
                }

                break;

            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    var before = messages.Count;
                    CollectValues(property.Value, messages);

                    // Field-keyed shapes read as "email: is invalid" rather than a bare "is invalid".
                    for (var i = before; i < messages.Count; i++)
                    {
                        messages[i] = $"{property.Name}: {messages[i]}";
                    }
                }

                break;
        }
    }
}
