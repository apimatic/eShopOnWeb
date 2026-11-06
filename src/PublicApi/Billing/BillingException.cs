using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlazorShared.Models;
using MaxioAdvancedBilling.Core.ErrorResponse;
using MaxioAdvancedBilling.Core.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.Billing;

/// <summary>
/// The app can't carry out a request; carries the HTTP status and the reason for the caller.
/// </summary>
public class BillingException : Exception
{
    public int StatusCode { get; }

    public BillingException(int statusCode, string message, Exception? inner = null) : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public static BillingException BadRequest(string message) => new(StatusCodes.Status400BadRequest, message);
    public static BillingException NotFound(string message) => new(StatusCodes.Status404NotFound, message);
    public static BillingException Conflict(string message) => new(StatusCodes.Status409Conflict, message);
    public static BillingException Unprocessable(string message) => new(StatusCodes.Status422UnprocessableEntity, message);

    public static IResult ToResult(int statusCode, string message) =>
        Results.Content(new ErrorDetails { StatusCode = statusCode, Message = message }.ToString(), "application/json", Encoding.UTF8, statusCode);

    /// <summary>Runs a Maxio-backed action and turns failures into 4xx (or 5xx for outages) results with a reason.</summary>
    public static async Task<IResult> RunAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (BillingException ex)
        {
            return ToResult(ex.StatusCode, ex.Message);
        }
        catch (ApiException ex)
        {
            var message = ExtractMessage(ex);
            var status = (int)ex.StatusCode;
            if (status == 404 || status == 409) return ToResult(status, message);
            if (status == 400 || status == 422) return ToResult(status, message);
            if (status >= 400 && status < 500 && status != 401 && status != 403 && status != 429) return ToResult(422, message);
            return ToResult(StatusCodes.Status502BadGateway, "The billing system could not process the request: " + message);
        }
        catch (SdkTimeoutException)
        {
            return ToResult(StatusCodes.Status504GatewayTimeout, "The billing system timed out.");
        }
        catch (SdkException ex)
        {
            return ToResult(StatusCodes.Status502BadGateway, "The billing system is unavailable: " + ex.Message);
        }
    }

    private static string ExtractMessage(ApiException ex)
    {
        var error = ex.GetType().GetProperty("Error")?.GetValue(ex);
        if (error is ApiError apiError && apiError.TryGetRawError(out var raw))
        {
            var text = Flatten(raw.ReadAsString());
            if (!string.IsNullOrWhiteSpace(text)) return text;
        }
        if (error != null)
        {
            // Typed error bodies (e.g. {"errors":[...]}) expose themselves through TryGet* methods.
            foreach (var method in error.GetType().GetMethods())
            {
                if (!method.Name.StartsWith("TryGet") || method.Name == "TryGetRawError" || method.GetParameters().Length != 1) continue;
                var args = new object?[] { null };
                if (method.Invoke(error, args) is true && args[0] != null)
                {
                    var text = Flatten(JsonSerializer.Serialize(args[0], args[0]!.GetType()));
                    if (!string.IsNullOrWhiteSpace(text)) return text;
                }
            }
        }
        return string.IsNullOrWhiteSpace(ex.Message) ? ex.StatusCode.ToString() : ex.Message;
    }

    /// <summary>Flattens a Maxio JSON error body into plain text.</summary>
    private static string Flatten(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new List<string>();
            Collect(doc.RootElement, parts);
            var distinct = new List<string>();
            foreach (var p in parts)
                if (!distinct.Contains(p)) distinct.Add(p);
            return string.Join(" ", distinct);
        }
        catch (JsonException)
        {
            return body.Length > 500 ? body.Substring(0, 500) : body;
        }
    }

    private static void Collect(JsonElement e, List<string> parts)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                parts.Add(e.GetString()!);
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Collect(i, parts);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == "additionalProperties" || p.Name == "AdditionalProperties") continue;
                    Collect(p.Value, parts);
                }
                break;
        }
    }
}
