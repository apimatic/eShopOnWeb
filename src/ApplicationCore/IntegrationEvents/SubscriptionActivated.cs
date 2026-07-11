using System;
using MediatR;

namespace Microsoft.eShopWeb.ApplicationCore.IntegrationEvents;

public record SubscriptionActivated(
    int SubscriptionId,
    int MaxioSubscriptionId,
    string UserId,
    string ProductHandle,
    int ProductId,
    decimal Price,
    string BillingCycle) : INotification;
