# kronan.mcpar.is

An HTTP-hosted .NET MCP server that integrates with the [Kronan](https://www.kronan.is/) API and exposes MCP tools over **Streamable HTTP** using the official [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

## Overview

This server implements the [Model Context Protocol (MCP)](https://modelcontextprotocol.io/) as a deployable ASP.NET Core web service. It can be used with any MCP-compatible AI assistant or tool to query the Kronan store API.

## Available MCP Tools

| Tool | Description |
|------|-------------|
| `SearchProducts` | Search for products by keyword, with optional category filter |
| `GetProduct` | Get detailed information about a product by ID |
| `ListCategories` | List all product categories |
| `ListStores` | List all Kronan store locations |
| `GetOffers` | Retrieve current offers and campaign deals |

## Configuration

The server reads its configuration from environment variables or `appsettings.json`.

| Setting | Environment variable | Config key | Default |
|---------|---------------------|------------|---------|
| Kronan API base URL | `Kronan__BaseUrl` | `Kronan:BaseUrl` | `https://api.kronan.is/` |
| Kronan API key | `KRONAN_API_KEY` or `Kronan__ApiKey` | `Kronan:ApiKey` | _(none)_ |

> **Do not commit your API key.** Use environment variables or a secrets manager.

## Running Locally

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9)

### Start the server

```bash
cd src/Kronan.mcpar.is

# Set your API key (optional if the API is open)
export KRONAN_API_KEY=your_api_key_here

dotnet run
```

The MCP endpoint will be available at: `http://localhost:5000/mcp`  
A health check is available at: `http://localhost:5000/health`

## Running with Docker

```bash
# Build
docker build -t kronan-mcp .

# Run
docker run -p 8080:8080 \
  -e KRONAN_API_KEY=your_api_key_here \
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
      "url": "http://localhost:5000/mcp",
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
