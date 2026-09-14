using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Subscriptions;
using Microsoft.eShopWeb.Infrastructure.Maxio.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSubscriptionService : ISubscriptionService
{
    private readonly System.IServiceProvider _serviceProvider;
    private readonly MaxioOptions _options;

    public MaxioSubscriptionService(System.IServiceProvider serviceProvider, IOptions<MaxioOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
    }

    private IMaxioClient Client
    {
        get
        {
            EnsureConfigured();
            return _serviceProvider.GetRequiredService<IMaxioClient>();
        }
    }

    public async Task<IReadOnlyList<SubscriptionPlan>> ListPlansAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        int familyId = await ResolveProductFamilyIdAsync(cancellationToken);

        IReadOnlyList<MaxioProductEnvelope> products = await Client.ListProductsForFamilyAsync(familyId, cancellationToken);

        return products
            .Select(envelope => envelope.Product)
            .Where(product => product.ArchivedAt is null)
            .OrderBy(product => product.Name, StringComparer.OrdinalIgnoreCase)
            .Select(product => new SubscriptionPlan
            {
                Id = product.Id,
                Handle = product.Handle ?? string.Empty,
                Name = product.Name,
                Description = product.Description,
                PriceInCents = product.PriceInCents,
                Interval = product.Interval,
                IntervalUnit = product.IntervalUnit,
                RequiresPaymentMethod = product.RequireCreditCard,
                Taxable = product.Taxable
            })
            .ToList();
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscriptionSubscriber subscriber, string planHandle, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(subscriber.Reference))
        {
            throw new ArgumentException("A subscriber reference is required.", nameof(subscriber));
        }

        if (string.IsNullOrWhiteSpace(planHandle))
        {
            throw new ArgumentException("A plan handle is required.", nameof(planHandle));
        }

        await EnsureCustomerAsync(subscriber, cancellationToken);

        string subscriptionReference = BuildSubscriptionReference(subscriber.Reference, planHandle);

        MaxioSubscriptionEnvelope? existing = await Client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
        if (existing is not null)
        {
            return new SubscribeResult(ToEnrollment(existing.Subscription), alreadySubscribed: true);
        }

        try
        {
            var request = new MaxioCreateSubscription
            {
                ProductHandle = planHandle,
                CustomerReference = subscriber.Reference,
                Reference = subscriptionReference
            };

            MaxioSubscriptionEnvelope created = await Client.CreateSubscriptionAsync(request, cancellationToken);
            return new SubscribeResult(ToEnrollment(created.Subscription), alreadySubscribed: false);
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity)
        {
            if (ex.Errors.Any(error => error.Contains("must be unique", StringComparison.OrdinalIgnoreCase)))
            {
                existing = await Client.FindSubscriptionByReferenceAsync(subscriptionReference, cancellationToken);
                if (existing is not null)
                {
                    return new SubscribeResult(ToEnrollment(existing.Subscription), alreadySubscribed: true);
                }
            }

            if (ex.Errors.Any(error => error.Contains("could not find product", StringComparison.OrdinalIgnoreCase)
                                       || (error.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                                           && (error.Contains("product", StringComparison.OrdinalIgnoreCase)
                                               || error.Contains("api handle", StringComparison.OrdinalIgnoreCase)))))
            {
                throw new SubscriptionPlanNotFoundException(planHandle);
            }

            throw new SubscriptionEnrollmentException(ex.Message);
        }
    }

    public async Task<IReadOnlyList<SubscriptionEnrollment>> ListSubscriptionsAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        if (string.IsNullOrWhiteSpace(subscriber.Reference))
        {
            return Array.Empty<SubscriptionEnrollment>();
        }

        MaxioCustomerEnvelope? customer = await Client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (customer is null)
        {
            return Array.Empty<SubscriptionEnrollment>();
        }

        IReadOnlyList<MaxioSubscriptionEnvelope> subscriptions = await Client.ListSubscriptionsForCustomerAsync(customer.Customer.Id, cancellationToken);

        return subscriptions
            .Select(envelope => ToEnrollment(envelope.Subscription))
            .OrderByDescending(subscription => subscription.CreatedAt)
            .ToList();
    }

    private async Task<int> ResolveProductFamilyIdAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<MaxioProductFamilyEnvelope> families = await Client.ListProductFamiliesAsync(cancellationToken);
        MaxioProductFamilyEnvelope? family = families.FirstOrDefault(familyEnvelope =>
            string.Equals(familyEnvelope.ProductFamily.Handle, _options.ProductFamilyHandle, StringComparison.OrdinalIgnoreCase));

        if (family is null)
        {
            throw new MaxioNotConfiguredException();
        }

        return family.ProductFamily.Id;
    }

    private async Task<MaxioCustomer> EnsureCustomerAsync(SubscriptionSubscriber subscriber, CancellationToken cancellationToken)
    {
        MaxioCustomerEnvelope? existing = await Client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
        if (existing is not null)
        {
            return existing.Customer;
        }

        (string firstName, string lastName) = ResolveNames(subscriber);

        var create = new MaxioCreateCustomer
        {
            FirstName = firstName,
            LastName = lastName,
            Email = subscriber.Email,
            Organization = "eShopOnWeb",
            Reference = subscriber.Reference
        };

        try
        {
            MaxioCustomerEnvelope created = await Client.CreateCustomerAsync(create, cancellationToken);
            return created.Customer;
        }
        catch (MaxioApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.UnprocessableEntity
                                           && ex.Errors.Any(error =>
                                               error.Contains("reference", StringComparison.OrdinalIgnoreCase) &&
                                               (error.Contains("taken", StringComparison.OrdinalIgnoreCase) ||
                                                error.Contains("unique", StringComparison.OrdinalIgnoreCase))))
        {
            MaxioCustomerEnvelope? raced = await Client.FindCustomerByReferenceAsync(subscriber.Reference, cancellationToken);
            if (raced is not null)
            {
                return raced.Customer;
            }

            throw;
        }
    }

    private static SubscriptionEnrollment ToEnrollment(MaxioSubscription subscription)
    {
        return new SubscriptionEnrollment
        {
            Id = subscription.Id,
            State = subscription.State ?? string.Empty,
            Reference = subscription.Reference,
            Currency = subscription.Currency,
            BalanceInCents = subscription.BalanceInCents,
            PriceInCents = subscription.ProductPriceInCents,
            ProductHandle = subscription.Product?.Handle,
            ProductName = subscription.Product?.Name,
            PaymentCollectionMethod = subscription.PaymentCollectionMethod,
            CurrentPeriodStartedAt = subscription.CurrentPeriodStartedAt,
            CurrentPeriodEndsAt = subscription.CurrentPeriodEndsAt,
            NextAssessmentAt = subscription.NextAssessmentAt,
            CreatedAt = subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt,
            ActivatedAt = subscription.ActivatedAt,
            CanceledAt = subscription.CanceledAt,
            CancelAtEndOfPeriod = subscription.CancelAtEndOfPeriod
        };
    }

    private static string BuildSubscriptionReference(string customerReference, string planHandle)
    {
        return $"{customerReference}-{planHandle}";
    }

    private static (string FirstName, string LastName) ResolveNames(SubscriptionSubscriber subscriber)
    {
        if (!string.IsNullOrWhiteSpace(subscriber.FirstName) && !string.IsNullOrWhiteSpace(subscriber.LastName))
        {
            return (subscriber.FirstName, subscriber.LastName);
        }

        string email = subscriber.Email;
        int atIndex = email.IndexOf('@');
        string localPart = atIndex >= 0 ? email[..atIndex] : email;
        string? domain = atIndex >= 0 ? email[(atIndex + 1)..] : null;

        if (string.IsNullOrWhiteSpace(localPart))
        {
            localPart = "eShopOnWeb";
        }

        int dotIndex = localPart.IndexOf('.');
        if (dotIndex > 0 && dotIndex < localPart.Length - 1)
        {
            return (Capitalize(localPart[..dotIndex]), Capitalize(localPart[(dotIndex + 1)..]));
        }

        string lastName = string.IsNullOrWhiteSpace(domain) ? "Shopper" : domain.Split('.')[0];
        return (Capitalize(localPart), Capitalize(lastName));
    }

    private static string Capitalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return char.ToUpperInvariant(value[0]) + value[1..];
    }

    private void EnsureConfigured()
    {
        if (!_options.IsConfigured)
        {
            throw new MaxioNotConfiguredException();
        }
    }
}
