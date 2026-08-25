using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Payment;

public static class PaymentStatusExtensions
{
    public static OrderPaymentStatus ToOrderStatus(this PaymentRecordStatus status) =>
        status switch
        {
            PaymentRecordStatus.AwaitingPayment => OrderPaymentStatus.AwaitingPayment,
            PaymentRecordStatus.Authorized => OrderPaymentStatus.Authorized,
            PaymentRecordStatus.Fulfilled => OrderPaymentStatus.Fulfilled,
            PaymentRecordStatus.Cancelled => OrderPaymentStatus.Cancelled,
            PaymentRecordStatus.Refunded => OrderPaymentStatus.Refunded,
            PaymentRecordStatus.PartiallyRefunded => OrderPaymentStatus.PartiallyRefunded,
            _ => OrderPaymentStatus.AwaitingPayment
        };
}
