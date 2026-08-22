using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.PublicApi.OrderEndpoints;
using MinimalApi.Endpoint;

namespace Microsoft.eShopWeb.PublicApi.ReconciliationEndpoints;

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId) { }
    public ReconciliationResponse() { }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public List<ReconciliationMatchResponse> Matched { get; set; } = new();
    public List<PayPalTransactionResponse> PayPalOnly { get; set; } = new();
    public List<ShopOrderResponse> EshopOnly { get; set; } = new();
}

public class ReconciliationMatchResponse
{
    public PayPalTransactionResponse PayPal { get; set; } = new();
    public ShopOrderResponse Order { get; set; } = new();
}

public class PayPalTransactionResponse
{
    public string? TransactionId { get; set; }
    public string? PaypalReferenceId { get; set; }
    public string? PaypalReferenceIdType { get; set; }
    public string? InvoiceId { get; set; }
    public string? CustomField { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public decimal? FeeAmount { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? InitiationDate { get; set; }
    public DateTimeOffset? UpdatedDate { get; set; }

    public static PayPalTransactionResponse From(PayPalTransactionRecord row) => new()
    {
        TransactionId = row.TransactionId,
        PaypalReferenceId = row.PaypalReferenceId,
        PaypalReferenceIdType = row.PaypalReferenceIdType,
        InvoiceId = row.InvoiceId,
        CustomField = row.CustomField,
        Amount = row.Amount,
        Currency = row.Currency,
        FeeAmount = row.FeeAmount,
        Status = row.Status,
        InitiationDate = row.InitiationDate,
        UpdatedDate = row.UpdatedDate
    };
}

public class ReconciliationEndpoint : IEndpoint<IResult, ReconciliationRequest, IReconciliationService>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReconciliationEndpoint(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void AddRoute(IEndpointRouteBuilder app)
    {
        app.MapGet("api/reconciliation",
            [Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)] async
            (DateTimeOffset from, DateTimeOffset to, IReconciliationService reconciliation) =>
            {
                return await HandleAsync(new ReconciliationRequest { From = from, To = to }, reconciliation);
            })
            .Produces<ReconciliationResponse>()
            .WithTags("ReconciliationEndpoints");
    }

    public async Task<IResult> HandleAsync(ReconciliationRequest request, IReconciliationService reconciliation)
    {
        if (request.To < request.From)
        {
            throw new OrderPaymentException("`to` must be on or after `from`.", 400);
        }

        var report = await reconciliation.ReconcileAsync(
            request.From,
            request.To,
            _httpContextAccessor.HttpContext?.RequestAborted ?? default);

        return Results.Ok(new ReconciliationResponse(request.CorrelationId())
        {
            From = report.From,
            To = report.To,
            Matched = report.Matched.Select(m => new ReconciliationMatchResponse
            {
                PayPal = PayPalTransactionResponse.From(m.PayPal),
                Order = ShopOrderResponse.From(m.Order, request.CorrelationId())
            }).ToList(),
            PayPalOnly = report.PayPalOnly.Select(PayPalTransactionResponse.From).ToList(),
            EshopOnly = report.EshopOnly.Select(o => ShopOrderResponse.From(o, request.CorrelationId())).ToList()
        });
    }
}
