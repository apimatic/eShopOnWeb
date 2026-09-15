namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public enum PaymentStatus { AwaitingPayment, Authorized, Captured, PartiallyRefunded, Refunded, Voided }
public enum FulfillmentStatus { AwaitingPayment, AwaitingFulfillment, Fulfilled, Cancelled }
