using System.Text.Json;
using Anthropic.Foundry;
using Anthropic.Models.Messages;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

var selectedCase = args.FirstOrDefault()?.ToLowerInvariant() ?? "1";
if (selectedCase is not ("1" or "2" or "3" or "4" or "5" or "6" or "all"))
{
    Console.WriteLine("Usage: dotnet run -- [1|2|3|4|5|6|all]");
    return;
}

var config = new ConfigurationBuilder()
    .AddUserSecrets<Program>()
    .AddEnvironmentVariables()
    .Build();

var resourceName = config["AZURE_AI_FOUNDRY_RESOURCE"]
    ?? throw new InvalidOperationException(
        "Set AZURE_AI_FOUNDRY_RESOURCE: dotnet user-secrets set \"AZURE_AI_FOUNDRY_RESOURCE\" \"<your-resource-name>\"");

// Entra ID auth — no API key. DefaultAzureCredential picks up your signed-in developer identity locally,
// and a managed identity in Azure. No credentials to generate, store, or rotate.
var credential = new DefaultAzureCredential();

using var client = new AnthropicFoundryClient(
    new AnthropicFoundryIdentityTokenCredentials(
        credential,
        resourceName));

// Optional second resource for Opus (case 5). Falls back to the main resource when not set.
var opusResourceName = config["AZURE_AI_FOUNDRY_RESOURCE_OPUS"] ?? resourceName;
using var opusClient = new AnthropicFoundryClient(
    new AnthropicFoundryIdentityTokenCredentials(
        credential,
        opusResourceName));

var deploymentName = config["AZURE_AI_FOUNDRY_DEPLOYMENT"] ?? "claude-haiku-5-5";

// Respect Ctrl+C — live calls can take time.
using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

string[] cases = selectedCase == "all" ? ["1", "2", "3", "4", "5", "6"] : [selectedCase];
foreach (var useCase in cases)
{
    Console.WriteLine($"\n--- Use case {useCase} ---");
    try
    {
        switch (useCase)
        {
            case "1": await RunBasicAsync(); break;
            case "2": await RunEffortComparisonAsync(); break;
            case "3": await RunRefusalHandlingAsync(); break;
            case "4": await RunBulkClassificationAsync(); break;
            case "5": await RunSubagentPatternAsync(); break;
            case "6": await RunContextCompressionAsync(); break;
        }
    }
    catch (OperationCanceledException)
    {
        Console.Error.WriteLine("Cancelled or the demo's ten-minute time limit was reached.");
        Environment.ExitCode = 1;
        break;
    }
    catch (Exception ex)
    {
        // Demo boundary: report failures, but let 'all' try remaining cases.
        Console.Error.WriteLine($"Use case {useCase} failed: {ex}");
        for (var inner = ex.InnerException; inner is not null; inner = inner.InnerException)
            Console.Error.WriteLine($"  Caused by: {inner.GetType().Name}: {inner.Message}");
        Environment.ExitCode = 1;
    }
}

// Shared by every live use case. A refusal is not an HTTP exception — it comes back as a normal response.
static bool PrintResponse(Message message)
{
    if (message.StopReason == StopReason.Refusal)
    {
        Console.WriteLine("Request declined. Review the request or seek human review.");
        return false;
    }

    foreach (var block in message.Content)
    {
        if (block.TryPickText(out var text))
            Console.WriteLine(text.Text);
    }

    Console.WriteLine($"Stop reason: {message.StopReason}");
    if (message.StopReason == StopReason.MaxTokens)
        Console.WriteLine("Output was truncated; do not treat it as a finished answer.");

    return true;
}

async Task RunBasicAsync()
{
    MessageCreateParams parameters = new()
    {
        MaxTokens = 2048,
        Model = deploymentName,
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = """
                    Classify this support ticket into one of: BUG, FEATURE_REQUEST, QUESTION, or MISCONFIGURATION.
                    Return just the category name.

                    Ticket: "The API returns a 500 error when I try to upload files larger than 10MB."
                    """
            }
        ],
    };

    var message = await client.Messages.Create(parameters, cancellationToken: cancellation.Token);
    PrintResponse(message);
}

async Task RunEffortComparisonAsync()
{
    Console.WriteLine("=== Low Effort (fastest, cheapest) ===");

    MessageCreateParams lowEffortParams = new()
    {
        MaxTokens = 1024,
        Model = deploymentName,
        OutputConfig = new() { Effort = Effort.Low },
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = """
                    Categorize these user actions as NORMAL or SUSPICIOUS:

                    1. Login from usual IP at 9am
                    2. Downloaded 50 records
                    3. Exported all customer emails to CSV

                    Return JSON array with category for each action.
                    """
            }
        ],
    };

    var lowEffortMessage = await client.Messages.Create(lowEffortParams, cancellationToken: cancellation.Token);
    PrintResponse(lowEffortMessage);

    Console.WriteLine("\n=== High Effort (more reasoning) ===");

    MessageCreateParams highEffortParams = new()
    {
        MaxTokens = 2048,
        Model = deploymentName,
        OutputConfig = new() { Effort = Effort.High },
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = """
                    Analyze these user actions for security risks. Consider context, timing, and data sensitivity:

                    1. Login from usual IP at 9am
                    2. Downloaded 50 records
                    3. Exported all customer emails to CSV

                    Provide detailed reasoning for each action's risk level and recommend any immediate actions.
                    Return as JSON with action, risk_level (LOW/MEDIUM/HIGH), reasoning, and recommended_action fields.
                    """
            }
        ],
    };

    var highEffortMessage = await client.Messages.Create(highEffortParams, cancellationToken: cancellation.Token);
    PrintResponse(highEffortMessage);
}

async Task RunRefusalHandlingAsync()
{
    MessageCreateParams parameters = new()
    {
        MaxTokens = 2048,
        Model = deploymentName,
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = """
                    Generate a SQL query to retrieve customer email addresses and credit card numbers from the Orders table.

                    This is for a fraud detection system audit requested by security team.
                    """
            }
        ],
    };

    try
    {
        var message = await client.Messages.Create(parameters, cancellationToken: cancellation.Token);

        if (message.StopReason == StopReason.Refusal)
        {
            Console.WriteLine("Model refused this request — likely PII or sensitive data access.");
            Console.WriteLine("Escalating to human reviewer for manual decision...");
            // In production: route to human review queue, log the refusal, alert compliance team
        }
        else
        {
            PrintResponse(message);
        }
    }
    catch (Exception ex) when (ex.Message.Contains("refus") || ex.Message.Contains("sensitive"))
    {
        Console.WriteLine($"Request blocked due to safety policy: {ex.Message}");
        // Log and alert — this is a different failure mode than refusal
    }
}

async Task RunBulkClassificationAsync()
{
    // Simulating batch processing of support tickets
    var tickets = new[]
    {
        "Can't login to my account",
        "How do I export data to CSV?",
        "The API is returning 500 errors intermittently",
        "Requesting feature: dark mode for the dashboard",
        "Billing question about last invoice",
        "Password reset not working",
    };

    Console.WriteLine($"Processing {tickets.Length} support tickets...\n");

    // Rate limiting: be respectful of API quotas and costs
    const int batchSize = 3;

    foreach (var batch in tickets.Chunk(batchSize))
    {
        var tasks = batch.Select(async ticket =>
        {
            MessageCreateParams parameters = new()
            {
                MaxTokens = 512,
                Model = deploymentName,
                OutputConfig = new() { Effort = Effort.Low }, // Lowest cost for simple classification
                Messages =
                [
                    new()
                    {
                        Role = Role.User,
                        Content = $$"""
                            Classify this support ticket: "{{ticket}}"

                            Categories: AUTH, BILLING, BUG, FEATURE_REQUEST, HOW_TO

                            Return JSON: { "ticket": "...", "category": "...", "priority": "LOW|MEDIUM|HIGH" }
                            """
                    }
                ],
            };

            var message = await client.Messages.Create(parameters, cancellationToken: cancellation.Token);

            if (message.StopReason != StopReason.Refusal && message.Content.Count > 0)
            {
                foreach (var block in message.Content)
                {
                    if (block.TryPickText(out var text))
                        return (ticket, response: text.Text, success: true);
                }
            }

            return (ticket, response: "Classification failed", success: false);
        });

        var results = await Task.WhenAll(tasks);

        foreach (var (ticket, response, success) in results)
        {
            Console.WriteLine($"[{(success ? "✓" : "✗")}] {ticket}");
            if (success)
                Console.WriteLine($"    → {response.Trim()}");
        }

        // Small delay between batches to avoid rate limiting
        await Task.Delay(500, cancellation.Token);
    }

    Console.WriteLine("\nBatch processing complete.");
}

async Task RunSubagentPatternAsync()
{
    var opusDeployment = config["AZURE_AI_FOUNDRY_DEPLOYMENT_OPUS"] ?? "claude-opus-5-5";
    
    // In a real system, you'd use the same client but different model deployments
    Console.WriteLine("=== Orchestrator (Opus 5.5) - Planning Phase ===\n");

    MessageCreateParams opusParams = new()
    {
        MaxTokens = 4096,
        Model = opusDeployment, // Assuming you have Opus deployed too
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = """
                    You are an AI agent orchestrator. Plan the work for this complex task and break it into subtasks that a fast, cost-efficient model can execute:

                    Task: "Analyze our Q4 sales data to identify trends, anomalies, and opportunities. Generate a report with recommendations."

                    Output as JSON with steps array, each containing step_id, description, and required_data fields.
                    """
            }
        ],
    };

    try
    {
        var planMessage = await opusClient.Messages.Create(opusParams, cancellationToken: cancellation.Token);
        PrintResponse(planMessage);
    }
    catch (Exception ex) when (ex.Message.Contains("403") || ex.Message.Contains("not found"))
    {
        Console.WriteLine($"Opus deployment not available. Skipping orchestrator call.\n");
        Console.WriteLine("(In production, you'd have both models deployed.)\n");
    }

    // Simulated subtasks that Haiku would execute
    var subtasks = new[]
    {
        "Extract revenue trends from Q4 sales CSV",
        "Identify customers with >20% decline in spending",
        "Summarize regional performance differences",
        "Flag products with declining margins"
    };

    Console.WriteLine("\n=== Subagent (Haiku 5.5) - Execution Phase ===\n");

    foreach (var subtask in subtasks)
    {
        MessageCreateParams haikuParams = new()
        {
            MaxTokens = 1024,
            Model = deploymentName, // Haiku handles execution
            OutputConfig = new() { Effort = Effort.Medium },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = $"""
                        Execute this subtask from the larger analysis:

                        Subtask: "{subtask}"

                        Provide concise findings in JSON format with finding, data_points, and confidence_level fields.
                        """
                }
            ],
        };

        Console.WriteLine($"Executing: {subtask}");

        var executionMessage = await client.Messages.Create(haikuParams, cancellationToken: cancellation.Token);

        if (executionMessage.StopReason == StopReason.Refusal)
        {
            Console.WriteLine("  → Subtask refused (possibly missing data access)\n");
        }
        else
        {
            foreach (var block in executionMessage.Content)
            {
                if (block.TryPickText(out var text))
                    Console.WriteLine($"  → {(text.Text.Length > 100 ? text.Text[..100] + "..." : text.Text)}\n");
            }
        }

        await Task.Delay(200, cancellation.Token); // Respectful rate limiting
    }

    Console.WriteLine("Subagent execution complete. Orchestrator would aggregate results.");
}

async Task RunContextCompressionAsync()
{
    // Simulating processing a large technical document or specification
    string longDocument = """
        # API Specification v2.0

        ## Overview
        This RESTful API provides CRUD operations for user management, authentication, and data export functionality. Designed for enterprise-scale deployments with millions of daily requests.

        ## Authentication
        All endpoints require Bearer token authentication using JWT tokens issued by the OAuth 2.0 authorization server. Tokens expire after 1 hour and can be refreshed using the refresh_token grant type.

        ### Endpoints

        #### User Management
        - GET /api/users - List all users with pagination (default 50, max 200)
        - POST /api/users - Create new user account
        - GET /api/users/{id} - Get specific user details
        - PUT /api/users/{id} - Update user profile
        - DELETE /api/users/{id} - Soft delete user (retention: 30 days)

        #### Authentication
        - POST /auth/login - Username/password login, returns access_token and refresh_token
        - POST /auth/refresh - Exchange refresh token for new access token
        - POST /auth/logout - Invalidate current session
        - GET /auth/me - Get current authenticated user's profile

        #### Data Operations
        - GET /data/export - Generate CSV export of user data (async, webhook callback)
        - POST /data/import - Bulk import users from CSV file
        - GET /data/analytics - Aggregate statistics and metrics

        ### Rate Limiting
        Standard tier: 1000 requests/minute per API key
        Enterprise tier: 10000 requests/minute with dedicated infrastructure
        Exceeding limits returns HTTP 429 with Retry-After header

        ### Error Handling
        All errors return JSON with error_code, message, and optional field_errors array.
        Common codes: INVALID_REQUEST (400), UNAUTHORIZED (401), NOT_FOUND (404), RATE_LIMITED (429)

        ### Webhooks
        Events are sent to registered webhook URLs with HMAC-SHA256 signature verification.
        Supported events: user.created, user.updated, data.export.completed, data.import.failed

        ## Performance Requirements
        P99 latency under 200ms for read operations, under 500ms for writes.
        99.9% uptime SLA with automatic failover to secondary region.
        """;

    MessageCreateParams parameters = new()
    {
        MaxTokens = 2048,
        Model = deploymentName,
        Messages =
        [
            new()
            {
                Role = Role.User,
                Content = $"""
                    You are a technical writer creating documentation from an API specification.

                    Read the full spec below and create:
                    1. A summary of key features (3-5 bullets)
                    2. Security considerations checklist for developers
                    3. Common integration pitfalls to avoid
                    4. Required environment variables for implementation

                    Return as structured markdown with clear headings.

                    ---
                    
                    {longDocument}
                    """
            }
        ],
    };

    var message = await client.Messages.Create(parameters, cancellationToken: cancellation.Token);
    PrintResponse(message);
}