using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Enum;

namespace MaxioAdvancedBilling.Models.Enums;

/// <summary>
/// The state of a subscription.
/// * <b>Live States</b>
///     * <c>active</c> - A normal, active subscription. It is not in a trial and is paid and up to date.
///     * <c>assessing</c> - An internal (transient) state that indicates a subscription is in the middle of periodic assessment. Do not base any access decisions in your app on this state, as it may not always be exposed.
///     * <c>pending</c> - An internal (transient) state that indicates a subscription is in the creation process. Do not base any access decisions in your app on this state, as it may not always be exposed.
///     * <c>trialing</c> - A subscription in trialing state has a valid trial subscription. This type of subscription may transition to active once payment is received when the trial has ended. Otherwise, it may go to a Problem or End of Life state.
///     * <c>paused</c> - An internal state that indicates that your account with Advanced Billing is in arrears.
/// * <b>Problem States</b>
///     * <c>past_due</c> - Indicates that the most recent payment has failed, and payment is past due for this subscription. If you have enabled our automated dunning, this subscription will be in the dunning process (additional status and callbacks from the dunning process will be available in the future). If you are handling dunning and payment updates yourself, you will want to use this state to initiate a payment update from your customers.
///     * <c>soft_failure</c> - Indicates that normal assessment/processing of the subscription has failed for a reason that cannot be fixed by the Customer. For example, a Soft Fail may result from a timeout at the gateway or incorrect credentials on your part. The subscriptions should be retried automatically. An interface is being built for you to review problems resulting from these events to take manual action when needed.
///     * <c>unpaid</c> - Indicates an unpaid subscription. A subscription is marked unpaid if the retry period expires and you have configured your <see href="https://maxio.zendesk.com/hc/en-us/articles/24287076583565-Dunning-Overview">Dunning</see> settings to have a Final Action of <c>mark the subscription unpaid</c>.
/// * <b>End of Life States</b>
///     * <c>canceled</c> - Indicates a canceled subscription. This may happen at your request (via the API or the web interface) or due to the expiration of the <see href="https://maxio.zendesk.com/hc/en-us/articles/24287076583565-Dunning-Overview">Dunning</see> process without payment. See the <see href="https://maxio.zendesk.com/hc/en-us/articles/24252109503629-Reactivating-and-Resuming">Reactivation</see> documentation for info on how to restart a canceled subscription.
///     While a subscription is canceled, its period will not advance, it will not accrue any new charges, and Advanced Billing will not attempt to collect the overdue balance.
///     * <c>expired</c> - Indicates a subscription that has expired due to running its normal life cycle. Some products may be configured to have an expiration period. An expired subscription then is one that stayed active until it fulfilled its full period.
///     * <c>failed_to_create</c> - Indicates that signup has failed. (You may see this state in a signup_failure webhook.)
///     * <c>on_hold</c> - Indicates that a subscription’s billing has been temporarily stopped. While it is expected that the subscription will resume and return to active status, this is still treated as an “End of Life” state because the customer is not paying for services during this time.
///     * <c>suspended</c> - Indicates that a prepaid subscription has used up all their prepayment balance. If a prepayment is applied, it will return to an active state.
///     * <c>trial_ended</c> - A subscription in a trial_ended state is a subscription that completed a no-obligation trial and did not have a card on file at the expiration of the trial period. See <see href="https://maxio.zendesk.com/hc/en-us/articles/24261076617869-Product-Editing">Product Pricing – No Obligation Trials</see> for more details.
/// <para>
/// See <see href="https://maxio.zendesk.com/hc/en-us/articles/24252119027853-Subscription-States">Subscription States</see> for more info about subscription states and state transitions.
/// </para>
/// </summary>
[JsonConverter(typeof(StringEnumConverter<SubscriptionState>))]
public sealed record SubscriptionState : StringEnum<SubscriptionState>
{
    private SubscriptionState(string value) : base(value)
    {
    }

    public static readonly SubscriptionState Pending = new("pending");

    public static readonly SubscriptionState FailedToCreate = new("failed_to_create");

    public static readonly SubscriptionState Trialing = new("trialing");

    public static readonly SubscriptionState Assessing = new("assessing");

    public static readonly SubscriptionState Active = new("active");

    public static readonly SubscriptionState SoftFailure = new("soft_failure");

    public static readonly SubscriptionState PastDue = new("past_due");

    public static readonly SubscriptionState Suspended = new("suspended");

    public static readonly SubscriptionState Canceled = new("canceled");

    public static readonly SubscriptionState Expired = new("expired");

    public static readonly SubscriptionState Paused = new("paused");

    public static readonly SubscriptionState Unpaid = new("unpaid");

    public static readonly SubscriptionState TrialEnded = new("trial_ended");

    public static readonly SubscriptionState OnHold = new("on_hold");

    public static readonly SubscriptionState AwaitingSignup = new("awaiting_signup");

    public static SubscriptionState FromValue(string value) => FromValueCore(value);
}
