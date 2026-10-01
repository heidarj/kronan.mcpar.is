# Remaining live acceptance checks

Automated tests use synthetic credentials, JWTs, and upstream responses. They establish local behavior; they do not establish real Krónan/Entra/ChatGPT/Azure compatibility.

- Confirm both household members see the same native Krónan note and obtain the appropriate user/customer-group token privately.
- Verify the upstream schema against actual reads, free-text batches, SKU batches, quantity changes, and removal. A GET may auto-create the note. Never send an empty change-line body.
- Test Entra discovery, S256 PKCE metadata, manual client registration, exact callback, resource/scopes, token audience, expiry, and refresh. Connect both allowed users and reject an unrelated tenant identity.
- Verify upstream 429/Retry-After behavior and quota attribution for the customer-group token. Other apps sharing the quota must be considered.
- Test a controlled single-process restart, the 200-second wait, old operation rejection, and the real Azure stop/drain/start procedure. Inspect actual replica termination before replacement.
- Test list/product cards in ChatGPT, including on both phones: text/quantity/add, explicit removal confirmation, read-only mode, exact image CDN CSP origins, and refresh after an uncertain write.
- Review workspace installation/publishing controls, scope consent prompts, and provider session revocation. Confirm secrets are absent from logs and package contents.

## Alexa follow-up

The `/alexa` adapter is not implemented in this release. Add it after the hosted MCP acceptance checks pass. Use a custom skill with an invocation name such as “Krónan Shopping”; the expected custom-skill utterance is “Alexa, ask Krónan Shopping to add butter.” Verify invocation naming and language support in the Alexa developer console.

The adapter must validate Amazon's request signature/certificate chain, timestamp, and skill ID before processing. Account linking uses a separate OAuth client and validates its bearer token with the same household/scope policy. Map Alexa's signed request ID to the same mutation coordinator, never mint a new action on a delivery retry, and spend the same upstream budget. A restart must reject replay candidates rather than guess whether they were applied; design and test that request-ID lifecycle before exposing the endpoint. Prefer free-text additions, batch explicit items, and give a short voice response. Implement Amazon's complete verification contract or use its supported SDK rather than a partial signature check.
