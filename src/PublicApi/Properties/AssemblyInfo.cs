using System.Runtime.CompilerServices;

// Exposes internal helpers (e.g. BillingUserFactory, SubscriptionMappings) to the test assembly.
[assembly: InternalsVisibleTo("SubscriptionTests")]
