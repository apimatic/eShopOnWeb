using System.Runtime.CompilerServices;

// The Maxio billing integration keeps its HTTP client, wire models and orchestration internal:
// the only supported surface is ISubscriptionBillingService plus the DI extension. The unit test
// assembly is granted access so those internals can be tested directly rather than forcing them
// public just to be observable.
[assembly: InternalsVisibleTo("UnitTests")]
