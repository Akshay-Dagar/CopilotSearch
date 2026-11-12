# CopilotSearch (POC)

Agentic enterprise search over (mock) Microsoft 365 data. See [docs/design.md](docs/design.md).

- **Api**: `POST /search` (SSE or `?stream=false`), `GET /health`, `GET /config`
- **Mcp**: the same 10 tools as an MCP server (stdio and Streamable HTTP), no LLM needed
- **Connectors**: mock Graph-shaped connectors over Contoso fixtures (`Connectors:Mode = Mock`)

## Run

Open the UI at http://localhost:5111 after `dotnet run --project src/CopilotSearch.Api --urls http://localhost:5111`.
The default `Demo` provider needs no credentials: it runs a keyword `search_all` and summarizes the hits. Pick
AzureOpenAI or Claude in the UI's Model dropdown once configured (it is disabled until then).

```powershell
dotnet test

# API: set LLM credentials first (user-secrets or env vars), see Llm section in appsettings.json
dotnet user-secrets --project src/CopilotSearch.Api set "Llm:AzureOpenAI:Endpoint" "https://<resource>.openai.azure.com/"
dotnet user-secrets --project src/CopilotSearch.Api set "Llm:AzureOpenAI:ApiKey" "<key>"
dotnet run --project src/CopilotSearch.Api

# Claude on Azure Foundry: Llm:Claude:ResourceName / ApiKey, then Llm:Provider=Claude or {"provider":"Claude"} per request
# Keyless: set AuthMode to DefaultAzureCredential

# MCP server
dotnet run --project src/CopilotSearch.Mcp -- --stdio
dotnet run --project src/CopilotSearch.Mcp -- --http --urls http://localhost:5173   # http://localhost:5173/mcp
```

Example:

```powershell
curl.exe -N -X POST http://localhost:5111/search -H "Content-Type: application/json" -d "{\"query\":\"what did Priya Sharma send about the Q3 budget?\"}"
```

## Notes
- Keep `Microsoft.Extensions.*` (non-AI) packages on 10.0.x; 11.0.0 breaks the MCP SDK on net9.
- The final `answer` has citations rewritten to `[n]`; streamed `answer_delta` text still contains raw ids.
