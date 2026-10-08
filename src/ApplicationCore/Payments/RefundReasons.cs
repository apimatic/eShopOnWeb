using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>The refund reasons an operator may give; stored and sent in their canonical form.</summary>
public static class RefundReasons
{
    public const string Fraud = "FRAUD";
    public const string CustomerRequest = "CUSTOMER REQUEST";
    public const string Return = "RETURN";
    public const string Duplicate = "DUPLICATE";
    public const string Other = "OTHER";

    public static IReadOnlyList<string> All { get; } = new[] { Fraud, CustomerRequest, Return, Duplicate, Other };

    /// <summary>Returns the canonical reason for <paramref name="reason"/>, or null when none was given.</summary>
    public static string? Normalize(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return null;

        var candidate = reason.Trim().Replace('_', ' ');
        var match = All.FirstOrDefault(r => string.Equals(r, candidate, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new PaymentValidationException(
            $"Unknown refund reason '{reason}'. Use one of: {string.Join(", ", All)}.");
    }
}
