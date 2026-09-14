using System.Runtime.CompilerServices;

// The Maxio wire contracts and transport client are internal on purpose: nothing outside this
// assembly should bind to Maxio's payload shapes. The unit tests exercise the billing service
// against a fake transport, so they need to see them.
[assembly: InternalsVisibleTo("UnitTests")]
