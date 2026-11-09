using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;

namespace Microsoft.eShopWeb.PublicApi.Payments;

public sealed record StaxPaymentMethod(
    string Id,
    string CustomerId,
    string Kind,
    string? Brand,
    string Last4,
    string Label,
    string? BinType,
    bool Prepaid,
    string? State,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? DeletedAt)
{
    public bool IsBank => Kind == "bank";

    public static StaxPaymentMethod From(JsonElement e)
    {
        var method = e.Str("method") ?? "card";
        var kind = string.Equals(method, "bank", StringComparison.OrdinalIgnoreCase) ? "bank" : "card";
        var prepaid = false;
        if (e.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object
            && meta.TryGetProperty("prepaid", out var pp) && pp.ValueKind == JsonValueKind.True)
        {
            prepaid = true;
        }
        return new StaxPaymentMethod(
            e.Str("id") ?? string.Empty,
            e.Str("customer_id") ?? string.Empty,
            kind,
            kind == "card" ? e.Str("card_type") : null,
            e.Str("card_last_four") ?? string.Empty,
            e.Str("nickname") ?? string.Empty,
            e.Str("bin_type"),
            prepaid,
            e.Str("address_state"),
            e.Date("created_at"),
            e.Date("deleted_at"));
    }
}

public sealed record StaxChildTransaction(string Id, string Type, bool Success, string? Status, string? Message);

public sealed record StaxTransaction(
    string Id,
    bool Success,
    string? Status,
    decimal Total,
    decimal TotalRefunded,
    DateTimeOffset? SettledAt,
    bool IsVoided,
    string? Message,
    IReadOnlyList<StaxChildTransaction> Children)
{
    public static StaxTransaction From(JsonElement e)
    {
        var children = new List<StaxChildTransaction>();
        if (e.TryGetProperty("child_transactions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in arr.EnumerateArray())
            {
                children.Add(new StaxChildTransaction(
                    c.Str("id") ?? string.Empty,
                    c.Str("type") ?? string.Empty,
                    c.Bool("success"),
                    c.Str("status"),
                    c.Str("message")));
            }
        }
        return new StaxTransaction(
            e.Str("id") ?? string.Empty,
            e.Bool("success"),
            e.Str("status"),
            e.Dec("total"),
            e.Dec("total_refunded"),
            e.Date("settled_at"),
            e.Bool("is_voided"),
            e.Str("message"),
            children);
    }
}

public sealed record StaxMerchantFees(bool SurchargeEnabled, decimal? CreditSurchargePercent, string? Policy);

public static class JsonElementExtensions
{
    public static string? Str(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null,
        };
    }

    public static bool Bool(this JsonElement e, string name)
    {
        if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(name, out var v))
        {
            return false;
        }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => v.TryGetInt32(out var i) && i != 0,
            JsonValueKind.String => v.GetString() is "1" or "true",
            _ => false,
        };
    }

    public static decimal Dec(this JsonElement e, string name)
    {
        var s = e.Str(name);
        return s is not null && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;
    }

    public static DateTimeOffset? Date(this JsonElement e, string name)
    {
        var s = e.Str(name);
        if (string.IsNullOrWhiteSpace(s))
        {
            return null;
        }
        // Stax timestamps are UTC, e.g. "2026-11-09 11:09:32"
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? d : null;
    }
}
