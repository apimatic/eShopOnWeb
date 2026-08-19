using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using FirecrawlApi.Core.Extensions;
using FirecrawlApi.Core.Models;

namespace FirecrawlApi.Core;

internal static class ParameterFlattener
{
    public static IEnumerable<KeyValuePair<string, string>> Flatten(Param param)
    {
        var (key, value, format) = param;
        return Flatten(key, value, format)
            .Select(kv => new KeyValuePair<string, string>(kv.Key, kv.Value));
    }

    public static IEnumerable<string> Flatten(object? value)
    {
        if (value is null) return [];

        var normalized = value.Normalize();
        return normalized switch
        {
            string str => [str],
            bool b => [b.ToString().ToLowerInvariant()],
            IEnumerable<object?> list => FlattenList(list, value),
            IDictionary<string, object?> => [JsonSerializer.Serialize(value)],
            _ => [normalized?.ToString() ?? string.Empty]
        };
    }

    private static IEnumerable<(string Key, string Value)> Flatten(
        string key,
        object? value,
        SerializationFormat format)
    {
        // TODO: Shield handle null or empty or default value here;
        if (value is null) return [];

        var normalized = value.Normalize();
        return normalized switch
        {
            IDictionary<string, object?> dict => FlattenDict(key, dict, format),
            string str => [(key, str)],
            IEnumerable<object?> list => FlattenList(key, list, format),
            bool boolValue => [(key, boolValue.ToString().ToLowerInvariant())],
            _ => [(key, Convert.ToString(normalized, CultureInfo.InvariantCulture) ?? string.Empty)]
        };
    }

    // ------------------------------------------------------
    // Expression helpers (pure functions)
    // ------------------------------------------------------

    private static IEnumerable<(string, string)> FlattenDict(
        string key,
        IDictionary<string, object?> dict,
        SerializationFormat format)
        =>
            dict.SelectMany(kv =>
                Flatten($"{key}[{kv.Key}]", kv.Value, format)
            );

    private static IEnumerable<(string, string)> FlattenList(
        string key,
        IEnumerable<object?> list,
        SerializationFormat format)
        =>
            format switch
            {
                SerializationFormat.Plain => list.SelectMany(item => Flatten(key, item, format)),
                SerializationFormat.Indexed => Indexed(key, list),
                SerializationFormat.UnIndexed => UnIndexed(key, list),
                SerializationFormat.Csv => Yield(key, list, ","),
                SerializationFormat.Tsv => Yield(key, list, "\t"),
                SerializationFormat.Psv => Yield(key, list, "|"),
                _ => list.SelectMany(item => Flatten(key, item, format))
            };

    private static IEnumerable<string> FlattenList(IEnumerable<object?> list, object? original)
    {
        var items = list.ToList();
        return items.All(IsScalar)
            ? items.Select(v => v?.ToString() ?? string.Empty)
            : [JsonSerializer.Serialize(original)];
    }

    // ------------------------------------------------------
    // Specific list-format handlers (each is a pure expression)
    // ------------------------------------------------------
    private static bool IsScalar(object? v) => v is null or string or bool or long or decimal or double;


    private static IEnumerable<(string, string)> Indexed(string key, IEnumerable<object?> list)
        =>
            list.SelectMany((item, index) =>
                Flatten($"{key}[{index}]", item, SerializationFormat.Plain)
            );

    private static IEnumerable<(string, string)> UnIndexed(string key, IEnumerable<object?> list)
        =>
            list.SelectMany(item =>
                Flatten($"{key}[]", item, SerializationFormat.Plain)
            );

    private static IEnumerable<(string, string)> Yield(string key, IEnumerable<object?> list, string sep) =>
        [(key, string.Join(sep, list.Select(v => v?.ToString() ?? "")))];
}