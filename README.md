# Claude Haiku 5.5 C# Demo

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A .NET 10 console app that demonstrates common Claude model patterns on Microsoft Foundry, using the Anthropic C# SDK and Entra ID (Azure Identity) authentication. Each use case is a small, self-contained example.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Azure subscription with a Microsoft Foundry resource
- At least one Claude model deployed on that resource (for example `claude-haiku-5-5`)
- Azure CLI, signed in with `az login` (used by `DefaultAzureCredential` locally)
- Your signed-in identity has the **Cognitive Services User** role on the Foundry resource

## Configuration

Settings are read from user secrets first, then environment variables.

```bash
dotnet user-secrets init
dotnet user-secrets set "AZURE_AI_FOUNDRY_RESOURCE" "<your-foundry-resource-name>"
dotnet user-secrets set "AZURE_AI_FOUNDRY_DEPLOYMENT" "claude-haiku-5-5"

# Optional: Opus deployment for use case 5
dotnet user-secrets set "AZURE_AI_FOUNDRY_DEPLOYMENT_OPUS" "claude-opus-5-5"
dotnet user-secrets set "AZURE_AI_FOUNDRY_RESOURCE_OPUS" "<opus-foundry-resource-name>"
```

| Setting | Required | Default | Purpose |
|---|---|---|---|
| `AZURE_AI_FOUNDRY_RESOURCE` | Yes | none | Foundry resource name (not the full URL) |
| `AZURE_AI_FOUNDRY_DEPLOYMENT` | No | `claude-haiku-5-5` | Model deployment used by most use cases |
| `AZURE_AI_FOUNDRY_RESOURCE_OPUS` | No | Same as `AZURE_AI_FOUNDRY_RESOURCE` | Resource that hosts the Opus deployment (use case 5) |
| `AZURE_AI_FOUNDRY_DEPLOYMENT_OPUS` | No | `claude-opus-5-5` | Opus deployment name (use case 5) |

> **Note:** Environment variables override user secrets. If a value you set is ignored, check for a stale variable with `echo $VARIABLE_NAME`.

## Usage

```bash
dotnet run -- [1|2|3|4|5|6|all]
```

Running with no argument runs case 1. Running `all` runs every case in order.

## Use cases

| # | Use case | What it shows | Model | Key settings |
|---|---|---|---|---|
| 1 | Basic | Single-prompt ticket classification | Haiku | `MaxTokens` |
| 2 | Effort comparison | Same task at low vs. high effort | Haiku | `OutputConfig.Effort` |
| 3 | Refusal handling | Detecting `StopReason.Refusal` as a normal response, not an exception | Haiku | `StopReason` |
| 4 | Bulk classification | Classifying many tickets in parallel batches with cancellation | Haiku | `Chunk`, `Task` |
| 5 | Subagent pattern | Opus plans the work, Haiku executes subtasks | Opus + Haiku | Second client, `AZURE_AI_FOUNDRY_*_OPUS` |
| 6 | Context compression | Summarizing a long conversation to reduce tokens | Haiku | Message history |

## Important: Use case 5 may use a different endpoint

Use case 5 is the only case that calls a second model, and that model is often **not in the same Foundry resource** as Haiku. Before you run it, check the following:

1. **Find where Opus is deployed.** Run:
   ```bash
   az cognitiveservices account deployment list --name <resource-name> --resource-group <resource-group> -o table
   ```
   Confirm a deployment with the exact name you configured exists and shows `Succeeded`.
2. **Set the resource separately.** If Opus is in a different Foundry resource, set `AZURE_AI_FOUNDRY_RESOURCE_OPUS` to that resource's name. Give only the name, not the full URL.
3. **Check the region.** The resource and its deployment must be in a region where the model is available. Cross-region calls are supported, but they can add latency and cost.
4. **Check access.** Your identity needs the **Cognitive Services User** role on the Opus resource too.
5. **Check the deployment name.** `DeploymentNotFound` means the name in `AZURE_AI_FOUNDRY_DEPLOYMENT_OPUS` doesn't match a deployment on that resource.
6. **Fallback.** If Opus isn't available, set `AZURE_AI_FOUNDRY_DEPLOYMENT_OPUS` to a Haiku deployment name. Case 5 will still run, but the planning step won't use Opus.

Cases 1–4 and 6 are not affected by these settings.

## Troubleshooting

| Error | Likely cause |
|---|---|
| `Name or service not known` | Wrong `AZURE_AI_FOUNDRY_RESOURCE` value, or DNS/VPN blocking the host |
| `401 Unauthorized` | Identity is missing the Cognitive Services User role, or Azure CLI is signed in to the wrong tenant |
| `DeploymentNotFound` | Deployment name doesn't exist on the resource |
| `I/O exception` | Network error. The inner exception lines show the real cause |

## Project structure

```
Program.cs                           Entry point, configuration, and all use cases
Claude-Haiku-5.5-CSharp-Demo.csproj  .NET 10 project and package references
LICENSE                              MIT license
README.md                            This file
```

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.
