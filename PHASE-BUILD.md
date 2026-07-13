You are implementing Maxio inside the eShopOnWeb repository through the usecases described in plan.md.

INTEGRATION MANDATE (NON-DEBATABLE)
1. Research and confirm the correct, current way to interact with Maxio for
   every capability this integration requires before writing code against it.
2. If you cannot confirm how a required capability works, or find that Maxio
   does not support something the integration needs, STOP and report the gap
   rather than working around it with invented or assumed behavior.

CODE QUALITY & STABILITY GUARANTEE
  - Write production-grade, highly secure, cleanly typed C#.
  - Ensure absolutely nothing breaks in the existing eShopOnWeb codebase.
    Maxio failures must never roll back or block eShopOnWeb's order lifecycle
    beyond the existing paths plan.md describes.
  - No placeholders, no "TODO"s, no incomplete implementations.

QUALITY GATES â€” THIS DEFINES "DONE":
  Make sure all the code you write conforms to the existing standards,
  conventions, and patterns of the eShopOnWeb repo.

HARD RULES
  - Implement plan.md thoroughly.
  - Do not hardcode Maxio credentials (client id/secret) or the environment.
    Use .NET user-secrets, and target the Maxio SANDBOX environment for all
    development and testing.
  - Sandbox credentials are provided to you in the environment variables
    MAXIO_API_KEY, MAXIO_SITE_SUBDOMAIN, MAXIO_ENVIRONMENT, and
    MAXIO_DEFAULT_PRODUCT_FAMILY. Load them into .NET user-secrets yourself.
    Never write their values into any file inside the repository.
  - Do not implement/build anything additional beyond what the plan describes.
  - Work until the integration is fully complete. Never hand back, never end
    with a question, and never defer remaining work to the user â€” decide and
    proceed.
  - After implementation is complete, verify and test yourself thoroughly to
    see if it's done completely and correctly, and that nothing breaks and
    everything works.
  - After self-testing, give me a step-by-step plan to verify it myself AT THE
    END OF THE WHOLE INTEGRATION, meaning COMPLETION and all WORKING FLOWS.

