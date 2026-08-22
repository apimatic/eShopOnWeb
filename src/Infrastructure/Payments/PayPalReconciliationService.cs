using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Specifications;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

public class PayPalReconciliationService : IPaymentReconciliationService
{
    private readonly IRepository<Order> _orders;
    private readonly PayPalGateway _gateway;
    private readonly PayPalSettings _settings;

    public PayPalReconciliationService(
        IRepository<Order> orders,
        PayPalGateway gateway,
        IOptions<PayPalSettings> settings)
    {
        _orders = orders;
        _gateway = gateway;
        _settings = settings.Value;
    }

    public async Task<ReconciliationReport> GetReportAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        string fromRaw,
        string toRaw,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_settings.ClientId) || string.IsNullOrWhiteSpace(_settings.ClientSecret))
        {
            throw new ApplicationCore.Exceptions.ApiException(
                "PayPal is not configured. Set PayPal:ClientId and PayPal:ClientSecret.", 503);
        }

        var transactions = await _gateway.SearchAllTransactionsAsync(fromRaw, toRaw, cancellationToken);
        var orders = await _orders.ListAsync(new OrdersWithPaymentSpecification(), cancellationToken);
        var ordersById = orders.ToDictionary(o => o.Id);

        var matched = new List<ReconciliationMatch>();
        var paypalOnly = new List<PayPalTransactionRow>();
        var matchedOrderIds = new HashSet<int>();

        foreach (var detail in transactions)
        {
            var info = detail.TransactionInfo;
            var row = ToRow(info);
            var orderId = TryParseOrderId(info?.InvoiceId) ?? TryParseOrderId(info?.CustomField);

            if (orderId is int id && ordersById.ContainsKey(id))
            {
                matchedOrderIds.Add(id);
                matched.Add(new ReconciliationMatch { OrderId = id, PayPal = row });
            }
            else
            {
                paypalOnly.Add(row);
            }
        }

        var eshopOnly = orders
            .Where(o => HasPayment(o) && !matchedOrderIds.Contains(o.Id) && InRange(o.OrderDate, from, to))
            .Select(o => new EshopPaymentRow
            {
                OrderId = o.Id,
                BuyerId = o.BuyerId,
                Status = o.FulfillmentStatus.ToString(),
                PayPalOrderId = o.PayPalOrderId,
                AuthorizationId = o.PayPalAuthorizationId,
                CaptureId = o.PayPalCaptureId,
                Total = o.Total(),
                OrderDate = o.OrderDate
            })
            .ToList();

        return new ReconciliationReport
        {
            From = from,
            To = to,
            Matched = matched,
            PayPalOnly = paypalOnly,
            EshopOnly = eshopOnly
        };
    }

    private static bool HasPayment(Order order) =>
        !string.IsNullOrEmpty(order.PayPalOrderId) ||
        !string.IsNullOrEmpty(order.PayPalAuthorizationId) ||
        !string.IsNullOrEmpty(order.PayPalCaptureId);

    private static bool InRange(DateTimeOffset value, DateTimeOffset from, DateTimeOffset to) =>
        value >= from && value <= to;

    private static int? TryParseOrderId(string? value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return id;
        }

        return null;
    }

    private static PayPalTransactionRow ToRow(PayPalServerSdk.Models.TransactionInformation? info)
    {
        return new PayPalTransactionRow
        {
            TransactionId = info?.TransactionId,
            PaypalReferenceId = info?.PaypalReferenceId,
            InvoiceId = info?.InvoiceId,
            CustomField = info?.CustomField,
            TransactionStatus = info?.TransactionStatus,
            Amount = info?.TransactionAmount?.Value,
            Currency = info?.TransactionAmount?.CurrencyCode,
            FeeAmount = info?.FeeAmount?.Value,
            InitiationDate = PayPalMoneyFormatter.ParseTime(info?.TransactionInitiationDate)
        };
    }
}
