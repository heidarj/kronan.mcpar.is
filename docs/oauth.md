# OAuth setup and Entra compatibility

Entra still lacks dynamic client registration (DCR). That does not rule it out: ChatGPT supports predefined clients, and Microsoft documents ChatGPT's **User-Defined OAuth Client** registration with Entra for its Power BI MCP servers. That is evidence for this approach, not an end-to-end verification of this custom service. See [Microsoft's setup](https://learn.microsoft.com/en-us/power-bi/developer/mcp/remote-mcp-server-external-clients) and [OpenAI authentication requirements](https://developers.openai.com/plugins/build/auth).

A public discovery check on October 1, 2026 found that Entra's [common v2 discovery document](https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration) omits `code_challenge_methods_supported` as well as `registration_endpoint`. OpenAI's documented requirement to advertise S256 means manual registration alone does not establish compatibility. Check your tenant-specific discovery and the actual ChatGPT flow before choosing Entra. The `common` endpoint was used only for this public check; the service must use a tenant-specific issuer.

Use your existing tenant as the first candidate. No Auth0 account is required by the server. If discovery or token interoperability fails, investigate that failure before selecting another managed provider. Do not build an ad hoc OAuth issuer or fabricate missing provider capabilities in a metadata proxy.

## Entra registrations

1. Create a single-tenant **resource/API registration** for Krónan Shopping. Set `api.requestedAccessTokenVersion` to 2. Expose the API with the service's HTTPS resource identifier, matching `Authentication:Resource` (including `/mcp`). Add delegated scopes `catalog:read`, `shopping:read`, `shopping:write`.
2. Create a separate **ChatGPT OAuth client registration**, grant those delegated API permissions, and obtain consent as required by tenant policy. Use ChatGPT's predefined/User-Defined OAuth Client route rather than DCR. Copy the exact callback URL shown by ChatGPT; do not guess it or use a wildcard. Choose its platform/client type based on the actual ChatGPT flow; Microsoft's documented Power BI example uses a native/public client. Do not create a client secret unless the selected flow requires a confidential client.
3. Configure the server Authority to `https://login.microsoftonline.com/<tenant-id>/v2.0`, Audience to the **resource/API app's client GUID**, SubjectClaim to `oid`, and allowed subjects to your two users' tenant object IDs. Audience, resource identifier, and OAuth client ID are different values.
4. Configure all three server scope values to their fully qualified exposed-API forms, e.g. `https://your-host/mcp/shopping:write`. Entra's `scp` token claim may contain the short scope name; the server accepts that spelling only after issuer/audience/membership validation.
5. Ensure your girlfriend has an account/guest identity in the selected tenant and has consent/access. Tenant-wide membership alone does not grant access: this service checks its explicit allowed-subject list.

[Microsoft's guidance for securing a custom MCP server](https://learn.microsoft.com/en-us/entra/agent-id/secure-mcp-server-with-entra-id) describes v2 tokens, resource identifiers, scope names, and the GUID audience.

## Interoperability gate — complete before household rollout

Run the read-only discovery check against the deployed protected-resource URL:

```sh
python3 scripts/oauth-preflight.py https://your-host/.well-known/oauth-protected-resource/mcp
```

It checks provider endpoints, advertised S256 PKCE, and code response support. It reports whether a registration endpoint exists; its absence is expected for a manually registered Entra client. A discovery pass alone does not establish compatibility.

In ChatGPT create the private connection using `/mcp`, OAuth, and the predefined client. Verify:

- Provider discovery is accepted, including advertised S256 PKCE. Missing advertised metadata is a compatibility failure to resolve, even if Entra accepts PKCE requests.
- Authorization includes the correct resource and delegated scopes. Entra receives an acceptable request and issues the expected v2 issuer/audience/scp/oid claims.
- The exact callback, code exchange, and supported client authentication work.
- Access tokens can expire and refresh without repeatedly prompting the user; request/consent `offline_access` where required by the provider/client flow.
- Both allowed users can connect; an unrelated valid tenant account cannot call tools.
- Catalogue/read scopes cannot authorize mutations. No upstream credential reaches ChatGPT, HTML, or logs.

OpenAI requires the authorization server to support the MCP resource contract. Do not assume a successful manual token test proves ChatGPT will send compatible resource/scope parameters. Record observed behavior without saving tokens.

## Revoke access

Remove a user from `AllowedSubjects`, then deploy using the stop/drain/start procedure. Revoke their provider refresh sessions/consents as appropriate. A previously issued token remains cryptographically valid until expiry, but the service's updated membership policy rejects it. Keep the tenant-specific issuer; do not use `common` or `organizations` as a household authority.

## Workspace and public distribution

First verify the private hosted connection, then package the plugin and publish it to your Business workspace using your current administrator controls. Workspace availability does not replace server authorization. Test installation and card behavior on both phones; host surfaces can differ.

Public publishing is separate. OpenAI's current [plugin guidelines](https://developers.openai.com/plugins/plugin-guidelines) prohibit collecting credentials and restrict unofficial third-party integrations. Do not add a public “paste your Krónan API key” screen and assume Directory eligibility. Obtain Krónan support for delegated authorization and clarify eligibility first. This repository can remain an open-source self-host template where each operator keeps its credential in its own managed secret.
