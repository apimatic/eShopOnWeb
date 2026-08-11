using System.Collections.Generic;

namespace Microsoft.eShopWeb.Infrastructure.PayPal.Contracts;

// Shapes shared across the PayPal APIs. Only the fields this integration reads or sends are modelled; PayPal
// may return more, which System.Text.Json ignores. Property names map to snake_case via the shared policy.

internal sealed class MoneyDto
{
    public string? CurrencyCode { get; set; }
    public string? Value { get; set; }
}

internal sealed class LinkDto
{
    public string? Href { get; set; }
    public string? Rel { get; set; }
    public string? Method { get; set; }
}

// The PayPal error model: { name, message, debug_id, details:[{ issue, description, field, ... }] }.
internal sealed class ErrorDto
{
    public string? Name { get; set; }
    public string? Message { get; set; }
    public string? DebugId { get; set; }
    public List<ErrorDetailDto>? Details { get; set; }
}

internal sealed class ErrorDetailDto
{
    public string? Field { get; set; }
    public string? Value { get; set; }
    public string? Location { get; set; }
    public string? Issue { get; set; }
    public string? Description { get; set; }
}
