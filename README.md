# kronan.mcpar.is

An HTTP-hosted .NET MCP server that integrates with the [Kronan](https://www.kronan.is/) API and exposes MCP tools over **Streamable HTTP** using the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Overview

This server implements the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) as a deployable ASP.NET Core web service. It can be used with any MCP-compatible AI assistant or tool to query the Kronan store API.

This repository also serves as a reference point for custom, production-oriented MCP server work. If you need consulting help with MCP adoption, HTTP-hosted MCP services, or a tailored MCP server for your own APIs and internal systems, commercial/custom implementation work is available.

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `SearchProducts` | Search for products by keyword |
| `GetProduct` | Get detailed information about a product by SKU |
| `ListCategories` | List all product categories |

## Configuration

The server reads its configuration from environment variables or `appsettings.json`.

| Setting | Environment variable | Config key | Default |
|---------|---------------------|------------|---------|
| Kronan API base URL | `Kronan__BaseUrl` | `Kronan:BaseUrl` | `https://api.kronan.is/api/v1/` |
| Kronan access token | `KRONAN_API_KEY` or `Kronan__ApiKey` | `Kronan:ApiKey` | _(none)_ |
| MCP endpoint shared secret | `MCP_SERVER_API_KEY` or `McpServer__ApiKey` | `McpServer:ApiKey` | _(required outside Development)_ |

> **Do not commit your tokens or endpoint secrets.** Use environment variables or a secrets manager.

## Running Locally

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)

### Start the server

```bash
cd src/Kronan.mcpar.is

# Set the Kronan access token and a secret for your MCP endpoint
export KRONAN_API_KEY=your_kronan_access_token
export MCP_SERVER_API_KEY=your_mcp_server_secret

dotnet run
```

The MCP endpoint will be available at: `http://localhost:5076/mcp`  
A health check is available at: `http://localhost:5076/health`

Requests to `/mcp` must include:

```http
X-Api-Key: your_mcp_server_secret
```

## Running with Docker

```bash
# Build
docker build -t kronan-mcp .

# Run
docker run -p 8080:8080 \
  -e KRONAN_API_KEY=your_kronan_access_token \
  -e MCP_SERVER_API_KEY=your_mcp_server_secret \
  kronan-mcp
```

MCP endpoint: `http://localhost:8080/mcp`

## Connecting an MCP Client

Configure your MCP client (e.g., Claude Desktop, VS Code Copilot) to use the Streamable HTTP transport pointing to:

```
http://<host>:<port>/mcp
```

Example Claude Desktop config (`claude_desktop_config.json`):

```json
{
  "mcpServers": {
    "kronan": {
      "url": "http://localhost:5076/mcp",
      "headers": {
        "X-Api-Key": "your_mcp_server_secret"
      },
      "transport": "streamable-http"
    }
  }
}
```

## Project Structure

```
kronan.mcpar.is/
├─ src/
│  └─ Kronan.mcpar.is/
│     ├─ KronanApi/          # Typed HttpClient for Kronan API
│     ├─ Models/             # API response models
│     ├─ Options/            # Configuration model
│     ├─ Tools/              # MCP tool implementations
│     ├─ Program.cs          # App entry point and DI setup
│     └─ appsettings.json    # Default configuration
├─ Dockerfile
└─ README.md
```

## Development

```bash
# Build
dotnet build src/Kronan.mcpar.is/Kronan.mcpar.is.csproj

# Run with hot reload
dotnet watch --project src/Kronan.mcpar.is/Kronan.mcpar.is.csproj
```

## Licensing

This project is available under the GNU GPL v3.

Commercial licensing is also available for organizations that want to use this software under different terms, or that want consulting and custom MCP server creation services.

For commercial licensing or consulting inquiries, contact:

[GitHub Profile](https://github.com/heidarj)
