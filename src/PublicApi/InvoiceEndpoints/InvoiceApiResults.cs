using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Invoicing;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Translates an <see cref="OperationResult{T}"/> from the invoicing services into an HTTP result
/// with a precise status code: not-found (404), invalid (400), state conflict (409) or a provider
/// failure (502). Callers supply how a success is rendered.
/// </summary>
public static class InvoiceApiResults
{
    public static IResult ToHttp<T>(OperationResult<T> result, Func<T, IResult> onSuccess) => result.Status switch
    {
        OperationStatus.Ok => onSuccess(result.Value!),
        OperationStatus.NotFound => Results.NotFound(new ErrorResponse(result.Error ?? "Not found.")),
        OperationStatus.Invalid => Results.BadRequest(new ErrorResponse(result.Error ?? "The request was not valid.")),
        OperationStatus.Conflict => Results.Conflict(new ErrorResponse(result.Error ?? "The requested action is not allowed for this bill.")),
        OperationStatus.ProviderError => Results.Json(new ErrorResponse(result.Error ?? "The invoicing provider could not be reached."), statusCode: StatusCodes.Status502BadGateway),
        _ => Results.Json(new ErrorResponse("Unexpected result."), statusCode: StatusCodes.Status500InternalServerError)
    };

    public static InvoiceResponse ToResponse(InvoiceDetailView view) => new()
    {
        InvoiceId = view.InvoiceId,
        OrderId = view.OrderId,
        State = view.State,
        ProviderStatus = view.ProviderStatus,
        Amount = view.Amount,
        Currency = view.Currency,
        DueDate = view.DueDate,
        CustomerName = view.CustomerName,
        CustomerEmail = view.CustomerEmail,
        PaymentLink = view.PaymentLink,
        History = view.History.Select(h => new InvoiceHistoryResponse(h.Event, h.Date)).ToList()
    };
}

public record ErrorResponse(string Error);
