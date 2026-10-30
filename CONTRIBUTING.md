# Contributing

Thanks for helping improve this sample. It's a small .NET 10 console app with six use cases that call Claude Haiku 5.5 on Microsoft Foundry, using the Anthropic C# SDK and Entra ID. Use case 5 also calls Claude Opus 5.5 as a planner. Contributions that make it clearer, more correct, or more useful to other .NET developers are welcome.

## Ways to contribute

- **Report a bug.** A use case fails, or the README doesn't match what the code does.
- **Fix the docs.** Typos, unclear steps, or a missing troubleshooting case.
- **Improve the sample.** For example, a streaming version of a use case, or a clearer way to aggregate the subagent results in case 5.
- **Share results.** If you compared low and high effort (case 2) or ran bulk classification (case 4) on your own data, an issue describing what you saw helps others.

For anything larger than a small fix, open an issue first so we can agree on the approach before you write code.

## Set up

1. Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and the Azure CLI.
2. Clone the repo and build it:

   ```bash
   git clone https://github.com/taswar/Claude-Haiku-5.5-CSharp-Demo.git
   cd Claude-Haiku-5.5-CSharp-Demo
   dotnet build
   ```

3. To run it, deploy Claude Haiku 5.5 on a Microsoft Foundry resource, set `AZURE_AI_FOUNDRY_RESOURCE` with user secrets, and sign in with `az login`, as described in the [README](README.md#configuration). Your account needs the **Cognitive Services User** role on the resource. Case 5 also needs an Opus deployment, or a Haiku deployment as a fallback.

## Guidelines

- **Never commit secrets.** No keys, resource names, tenant IDs or tokens in code, config files or screenshots. Settings belong in user secrets.
- **Keep Entra ID.** The sample uses `DefaultAzureCredential` with no API key. Keep it that way.
- **Treat refusals as responses.** A refusal comes back as `StopReason.Refusal`, not as an exception. Keep that check in any new use case, and keep the `MaxTokens` truncation warning.
- **Keep cost low by default.** Haiku is the cheap, fast model here. Use low or medium effort and small `MaxTokens` unless the use case is about showing more reasoning.
- **Keep the rate limiting.** Case 4 runs in small batches with a delay, and case 5 pauses between subtasks. Keep the limits, or explain why you changed them.
- **Keep it a sample.** The code should stay short and readable in one `Program.cs`. Prefer clarity over abstraction, and add a dependency only when it clearly helps.
- **Match the existing style.** Target `net10.0`, keep nullable reference types on, and follow the naming and layout already in `Program.cs`.
- **Keep the README in sync.** If you add a use case, change settings or change output, update the README in the same pull request, including the use case table.
- **One change per pull request.** Small, focused pull requests are easier to review and merge.

## Pull request checklist

- [ ] `dotnet build` succeeds with no new warnings.
- [ ] Any use case you changed runs against your own Foundry deployment, or you explain why you couldn't run it.
- [ ] No secrets, resource names or personal data are in the diff.
- [ ] The README reflects any change in use cases, settings or output.
- [ ] The pull request description says what changed and why.

The build workflow runs on every pull request and has to pass before a merge.

## Reporting a bug

Open an issue and include:

- Which use case you ran (`dotnet run -- 1` to `6`, or `all`) and what you expected.
- The full error message, including the "Caused by" lines, with any resource names removed.
- Your .NET SDK version (`dotnet --version`) and operating system.
- Your deployment names, and for case 5, whether Opus is on a different resource.

## Security issues

Please don't report security problems in a public issue. Use **Security → Report a vulnerability** on the repo to send a private report.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE) that covers this project.
