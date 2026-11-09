using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using StaxFattMerchantApi.Core.ErrorResponse;
using StaxFattMerchantApi.Core.Exceptions;

namespace Microsoft.eShopWeb.PublicApi.Payments;

/// <summary>A request the app cannot carry out: becomes a 4xx answer with a reason.</summary>
public class ApiProblem : Exception
{
    public int Status { get; }

    public ApiProblem(int status, string message, Exception? inner = null) : base(message, inner)
    {
        Status = status;
    }

    public static ApiProblem BadRequest(string message) => new(400, message);
    public static ApiProblem NotFound(string message) => new(404, message);
    public static ApiProblem Conflict(string message) => new(409, message);
    public static ApiProblem Unprocessable(string message) => new(422, message);

    /// <summary>Ids come from the URL: one that is not a valid id simply names nothing.</summary>
    public static Guid ParseId(string value, string what) =>
        Guid.TryParse(value, out var id) ? id : throw NotFound("No such " + what + ".");

    public IResult ToResult() => Results.Json(new
    {
        statusCode = Status,
        error = Message,
        message = Message
    }, statusCode: Status);

    /// <summary>Translates anything thrown while talking to Stax into a 4xx problem.</summary>
    public static ApiProblem FromException(Exception ex)
    {
        switch (ex)
        {
            case ApiProblem p:
                return p;
            case ApiException<RawError> api:
                {
                    var text = DescribeBody(api.Error.ReadAsString());
                    var code = (int)api.StatusCode;
                    var status = code switch
                    {
                        404 => 404,
                        409 => 409,
                        400 or 422 => 422,
                        _ => 424
                    };
                    return new ApiProblem(status, "Stax: " + text, ex);
                }
            case AuthSchemeException:
                return new ApiProblem(422, "Stax is not configured: Stax:ApiKey is missing.", ex);
            case SdkException sdk:
                return new ApiProblem(424, "Stax could not be reached: " + sdk.Message, ex);
            case HttpRequestException http:
                return new ApiProblem(424, "Stax could not be reached: " + http.Message, ex);
            default:
                return new ApiProblem(500, ex.Message, ex);
        }
    }

    public static bool IsStaxFailure(Exception ex) =>
        ex is ApiProblem or ApiException<RawError> or SdkException or HttpRequestException;

    /// <summary>Flattens a Stax error body (validation maps, "error"/"message" fields) into one sentence.</summary>
    public static string DescribeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "request rejected";
        try
        {
            using var doc = JsonDocument.Parse(body);
            var parts = new StringBuilder();
            Flatten(doc.RootElement, parts);
            var s = parts.ToString().Trim();
            return s.Length == 0 ? "request rejected" : s;
        }
        catch (JsonException)
        {
            return body.Length > 300 ? body[..300] : body;
        }
    }

    private static void Flatten(JsonElement e, StringBuilder sb)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                var text = e.GetString();
                if (string.IsNullOrEmpty(text) || sb.ToString().Contains(text)) break;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(text);
                break;
            case JsonValueKind.Array:
                foreach (var i in e.EnumerateArray()) Flatten(i, sb);
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject()) Flatten(p.Value, sb);
                break;
        }
    }
}

public static class ApiGuard
{
    /// <summary>Runs a handler, turning Stax and validation failures into 4xx answers.</summary>
    public static async Task<IResult> Run(Func<Task<IResult>> handler)
    {
        try
        {
            return await handler();
        }
        catch (Exception ex) when (ApiProblem.IsStaxFailure(ex))
        {
            return ApiProblem.FromException(ex).ToResult();
        }
    }
}
