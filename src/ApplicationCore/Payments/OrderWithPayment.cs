using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Entities.PaymentAggregate;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>An order paired with its payment (if one has been started), for the my-orders view.</summary>
public record OrderWithPayment(Order Order, Payment? Payment);
