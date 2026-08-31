using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.InvoiceEndpoints;

/// <summary>
/// Reports a bill's current state, whatever the provider reports about how it reached that state, and —
/// once it has been put to the shopper — how it can be paid (a top-level pay link). Scoped to the
/// caller's own bills unless the caller is an operator.
/// </summary>
public class GetInvoiceEndpoint : IEndpoint<IResult, GetInvoiceRequest, IInvoicingService>
{
    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/invoices/{invoiceId}",
            [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (string invoiceId, ClaimsPrincipal user, IInvoicingService service, CancellationToken ct) =>
            {
                var requesterId = CallerIdentity.GetUserName(user);
                if (string.IsNullOrEmpty(requesterId))
                    return Results.Unauthorized();

                var request = new GetInvoiceRequest(invoiceId, requesterId, CallerIdentity.IsOperator(user));
                return await HandleAsync(request, service, ct);
            })
            .Produces<GetInvoiceResponse>()
            .WithTags("InvoiceEndpoints");
    }

    public Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoicingService service) =>
        HandleAsync(request, service, CancellationToken.None);

    public async Task<IResult> HandleAsync(GetInvoiceRequest request, IInvoicingService service, CancellationToken ct)
    {
        return await InvoicingProblem.GuardAsync(async () =>
        {
            var details = await service.GetInvoiceAsync(request.InvoiceId, request.RequesterId, request.IsOperator, ct);

            var response = new GetInvoiceResponse(request.CorrelationId())
            {
                InvoiceId = details.InvoiceId,
                OrderId = details.OrderId,
                Status = details.Status,
                ProviderStatus = details.ProviderStatus,
                Currency = details.Currency,
                Amount = details.Amount,
                DueDate = details.DueDate,
                CustomerName = details.CustomerName,
                CustomerEmail = details.CustomerEmail,
                Issued = details.Issued,
                PaymentLink = details.PaymentLink,
                History = InvoiceViewMapper.ToView(details.History),
            };
            return Results.Ok(response);
        });
    }
}

public class GetInvoiceRequest : BaseRequest
{
    public string InvoiceId { get; }
    public string RequesterId { get; }
    public bool IsOperator { get; }

    public GetInvoiceRequest(string invoiceId, string requesterId, bool isOperator)
    {
        InvoiceId = invoiceId;
        RequesterId = requesterId;
        IsOperator = isOperator;
    }
}

public class GetInvoiceResponse : BaseResponse
{
    public GetInvoiceResponse(Guid correlationId) : base(correlationId) { }

    public GetInvoiceResponse() { }

    public string InvoiceId { get; set; } = string.Empty;
    public int OrderId { get; set; }

    /// <summary>eShop's own lifecycle stage: Draft, Issued or Withdrawn.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The raw status string the provider reports.</summary>
    public string? ProviderStatus { get; set; }

    public string Currency { get; set; } = string.Empty;
    public string Amount { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerEmail { get; set; }
    public bool Issued { get; set; }

    /// <summary>How the shopper can pay the bill, once it has been put to them. Null otherwise.</summary>
    public string? PaymentLink { get; set; }

    /// <summary>The provider's record of how the bill reached its current state.</summary>
    public List<InvoiceHistoryView> History { get; set; } = new();
}
