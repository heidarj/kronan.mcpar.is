# Krónan Shopping (Dev)

This is a separate development plugin. `plugin.json` identifies the package;
`mcp.json` connects a local plugin host, such as Codex on this Mac, directly to
`http://127.0.0.1:5076/mcp`. It requires the running development server and its
loopback authentication mode. The shopping tools still use the server's configured
Krónan account and can change checkout, named saved lists, and the separate free-form shopping note.

## Tunnel connection for ChatGPT

`tunnel.yaml` is a tunnel-client profile, generated from the native client's
supported configuration format. It uses tunnel
`tunnel_6ac29e002a988191bcd88c3aa02d92cd` and references the runtime key through
`CONTROL_PLANE_API_KEY`; it contains no key. It is not read by the plugin host.

With the runtime key already in your terminal environment, run from this folder:

```sh
tunnel-client run --config ./tunnel.yaml
```

Use OpenAI's standard `tunnel-client`, rather than the Cloudflare runtime variant.
While using the temporary client downloaded in this setup, the equivalent command
is `/private/tmp/kronan-openai-tunnel/tunnel-client run --config ./tunnel.yaml`.
Stop the previous tunnel client first to avoid competing clients and a port conflict.
Keep the client and dev server running while using ChatGPT.

In ChatGPT's Create MCP plugin dialog, choose Tunnel, select this tunnel, and use
No authentication for this development server.

The portable `mcp.json` schema has no `tunnel_id` field. Its loopback URL works
only for a host running on this Mac; uploading this ZIP does not turn that URL
into a ChatGPT tunnel connection. The documented cloud packaging flow requires
registering the connection in ChatGPT first and obtaining its `plugin_asdk_app...`
ID. Once registration succeeds, replace this package's local `mcp.json` wiring
with a root `.app.json` mapping:

```json
{
  "apps": {
    "kronan-dev": {"id": "YOUR_REGISTERED_PLUGIN_ASDK_APP_ID"}
  }
}
```

Set `extensions.com.openai.apps` in `plugin.json` to `"./.app.json"`, and remove
`mcp.json` from the cloud package to avoid also declaring the loopback connection.
The tunnel ID is not a replacement for that registered app ID.

This package cannot resolve the current `plugins_access_denied` workspace error.
Cloud installation and tunnel tool calls remain unverified until that is resolved.

## Package for local testing

From the repository root:

```sh
python3 scripts/package-plugin-dev.py
```

The ready-made ZIP in `plugins/dist/kronan-shopping-dev.zip` has the plugin manifest
at the ZIP root. It is for local development, not public submission.

References: [Plugin packaging](https://developers.openai.com/plugins/build/plugins),
[Secure MCP Tunnel](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels).
