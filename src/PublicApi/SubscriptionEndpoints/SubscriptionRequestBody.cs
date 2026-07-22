using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

/// <summary>
/// Reads a subscription request body tolerantly: unknown and extra fields are ignored, property
/// names are matched regardless of case and separators, and a value written as a string or a number
/// is accepted either way.
/// </summary>
/// <remarks>
/// The framework's default binding answers a body it cannot deserialize with an empty 400, which
/// tells the caller nothing. Reading the body here means every rejection comes from the subscription
/// module itself and carries an explanation.
/// </remarks>
public sealed class SubscriptionRequestBody
{
    private static readonly JsonDocumentOptions ParseOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>How far into nested envelopes a property is looked for.</summary>
    private const int MaxSearchDepth = 4;

    private readonly JsonElement _root;
    private readonly bool _hasContent;
    private readonly IReadOnlyDictionary<string, string> _queryValues;

    private SubscriptionRequestBody(JsonElement root, bool hasContent,
        IReadOnlyDictionary<string, string> queryValues)
    {
        _root = root;
        _hasContent = hasContent;
        _queryValues = queryValues;
    }

    /// <summary>An absent or unreadable body, which behaves as an object with no properties.</summary>
    public static SubscriptionRequestBody Empty { get; } =
        new(default, hasContent: false, new Dictionary<string, string>());

    public static async Task<SubscriptionRequestBody> ReadAsync(HttpRequest request,
        CancellationToken cancellationToken)
    {
        var queryValues = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in request.Query)
        {
            var value = entry.Value.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                queryValues[Normalize(entry.Key)] = value;
            }
        }

        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);

        var payload = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(payload))
        {
            return new SubscriptionRequestBody(default, hasContent: false, queryValues);
        }

        try
        {
            using var document = JsonDocument.Parse(payload, ParseOptions);

            return document.RootElement.ValueKind == JsonValueKind.Object
                ? new SubscriptionRequestBody(document.RootElement.Clone(), hasContent: true, queryValues)
                : new SubscriptionRequestBody(default, hasContent: false, queryValues);
        }
        catch (JsonException)
        {
            return new SubscriptionRequestBody(default, hasContent: false, queryValues);
        }
    }

    public string? GetString(params string[] names)
    {
        if (!TryGetProperty(names, out var value))
        {
            return TryGetQueryValue(names);
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => null
        };
    }

    public decimal? GetDecimal(params string[] names)
    {
        if (!TryGetProperty(names, out var value))
        {
            var fromQuery = TryGetQueryValue(names);

            return decimal.TryParse(fromQuery, NumberStyles.Number, CultureInfo.InvariantCulture, out var queryNumber)
                ? queryNumber
                : null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    public bool? GetBoolean(params string[] names)
    {
        if (!TryGetProperty(names, out var value))
        {
            return bool.TryParse(TryGetQueryValue(names), out var fromQuery) ? fromQuery : null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        };
    }

    private string? TryGetQueryValue(string[] names)
    {
        foreach (var name in names)
        {
            if (_queryValues.TryGetValue(Normalize(name), out var value))
            {
                return value;
            }
        }

        return null;
    }

    /// <summary>
    /// Looks a property up breadth first, so a value written at the top level wins over the same
    /// name nested inside an envelope such as <c>{ "migration": { "product_handle": … } }</c>.
    /// </summary>
    private bool TryGetProperty(string[] names, out JsonElement value)
    {
        value = default;

        if (!_hasContent)
        {
            return false;
        }

        var level = new List<JsonElement> { _root };

        for (var depth = 0; depth < MaxSearchDepth && level.Count > 0; depth++)
        {
            foreach (var name in names)
            {
                var wanted = Normalize(name);

                foreach (var element in level)
                {
                    foreach (var property in element.EnumerateObject())
                    {
                        if (Normalize(property.Name) == wanted && property.Value.ValueKind != JsonValueKind.Null)
                        {
                            value = property.Value;
                            return true;
                        }
                    }
                }
            }

            var next = new List<JsonElement>();
            foreach (var element in level)
            {
                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind == JsonValueKind.Object)
                    {
                        next.Add(property.Value);
                    }
                }
            }

            level = next;
        }

        return false;
    }

    /// <summary>Matches <c>plan_handle</c>, <c>planHandle</c> and <c>Plan-Handle</c> as one name.</summary>
    private static string Normalize(string name)
    {
        var builder = new StringBuilder(name.Length);

        foreach (var character in name)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }
}
