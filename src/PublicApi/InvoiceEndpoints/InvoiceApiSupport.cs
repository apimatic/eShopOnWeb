using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>Reads the caller's identity and role from the validated JWT.</summary>
public static class CallerIdentity
{
    public static string? GetUserName(ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Name) ?? user.Identity?.Name;

    public static bool IsOperator(ClaimsPrincipal user) =>
        user.IsInRole(BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS);
}

/// <summary>
/// Translates the invoicing domain's exceptions into HTTP results at the API boundary — one shared
/// ladder applied at every call site, carrying only caller-safe messages (never a secret or a raw
/// provider body).
/// </summary>
public static class InvoicingProblem
{
    public static async Task<IResult> GuardAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (OrderNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvoiceNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (CatalogItemNotFoundException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvoiceStateException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
        catch (InvoiceProviderException ex)
        {
            var (status, message) = MapProvider(ex);
            return Results.Json(new { error = message }, statusCode: status);
        }
    }

    private static (int Status, string Message) MapProvider(InvoiceProviderException ex) => (int?)ex.StatusCode switch
    {
        // Our credentials or our quota — the caller did nothing wrong and cannot fix it.
        401 or 403 => (StatusCodes.Status502BadGateway, "The invoicing provider is unavailable."),
        429 => (StatusCodes.Status503ServiceUnavailable, "The invoicing provider is temporarily unavailable."),
        // The provider rejected the caller's request — hand back the same status so they can act on it.
        >= 400 and < 500 => ((int)ex.StatusCode!, ex.Message),
        // Transport, timeout, provider 5xx, or an unreadable response — no meaningful caller status.
        _ => (StatusCodes.Status502BadGateway, ex.Message),
    };
}

/// <summary>A single step in the provider's record of how a bill reached its current state.</summary>
public class InvoiceHistoryView
{
    public string? Event { get; set; }
    public DateTimeOffset? Date { get; set; }
}

/// <summary>A shopper-facing summary of one bill (carries its own invoiceId, which operators act on).</summary>
public class InvoiceSummaryView
{
    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool Issued { get; set; }
    public string Amount { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
}

/// <summary>One row of the reconciliation report (carries its own invoiceId).</summary>
public class ReconciliationEntryView
{
    public string? InvoiceId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public bool PresentAtProvider { get; set; }
    public bool PresentInEShop { get; set; }
    public string? Status { get; set; }
    public string? Amount { get; set; }
    public string? Currency { get; set; }
    public DateTimeOffset? CreatedDate { get; set; }
    public string Discrepancy { get; set; } = string.Empty;
}

public static class InvoiceViewMapper
{
    public static InvoiceSummaryView ToView(InvoiceSummary summary) => new()
    {
        InvoiceId = summary.InvoiceId,
        OrderId = summary.OrderId,
        Status = summary.Status,
        Issued = summary.Issued,
        Amount = summary.Amount,
        Currency = summary.Currency,
        DueDate = summary.DueDate,
    };

    public static ReconciliationEntryView ToView(ReconciliationEntry entry) => new()
    {
        InvoiceId = entry.InvoiceId,
        Origin = entry.Origin.ToString(),
        PresentAtProvider = entry.PresentAtProvider,
        PresentInEShop = entry.PresentInEShop,
        Status = entry.Status,
        Amount = entry.Amount,
        Currency = entry.Currency,
        CreatedDate = entry.CreatedDate,
        Discrepancy = entry.Discrepancy.ToString(),
    };

    public static List<InvoiceHistoryView> ToView(IReadOnlyList<InvoiceHistoryItem> history) =>
        history.Select(h => new InvoiceHistoryView { Event = h.Event, Date = h.Date }).ToList();
}
