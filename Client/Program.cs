using Azure; // Added for AzureKeyCredential
using Azure.AI.OpenAI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;

Console.WriteLine("=== MCP Client Demo ===");
Console.WriteLine();

var config = new ConfigurationBuilder().AddUserSecrets<Program>().Build();
string endpoint = config["AzureOpenAI:Endpoint"];
string deployment = config["AzureOpenAI:Deployment"];
string apiKey = config["AzureOpenAI:ApiKey"];

Console.WriteLine("Initializing Azure OpenAI client...");

// Create an IChatClient using Azure OpenAI with API key authentication
IChatClient chatClient;
try
{
    chatClient = new ChatClientBuilder(
        new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
            .GetChatClient(deployment)
            .AsIChatClient()
    )
        .UseFunctionInvocation()
        .Build();

    Console.WriteLine("✓ Azure OpenAI client initialized successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to initialize Azure OpenAI client: {ex.Message}");
    Console.WriteLine(
        "\nPlease update the azureOpenAIEndpoint, deploymentName, and azureOpenAIApiKey in Program.cs"
    );
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

Console.WriteLine();
Console.WriteLine("Starting MCP Server and connecting client...");

// Create the MCP client and connect to the MCP server
// The server runs as a separate process via stdio transport
IMcpClient mcpClient;
try
{
    // Get the path to the MCP Server project
    string projectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Server", "Server.csproj")
    );

    mcpClient = await McpClientFactory.CreateAsync(
        new StdioClientTransport(
            new()
            {
                Command = "dotnet",
                Arguments = ["run", "--project", projectPath],
                Name = "MCP Server",
            }
        )
    );

    Console.WriteLine("✓ Connected to MCP Server successfully");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to connect to MCP Server: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

Console.WriteLine();
Console.WriteLine("Retrieving available tools from MCP Server...");

// List all available tools from the MCP server
IList<McpClientTool> tools;
try
{
    tools = await mcpClient.ListToolsAsync();
    Console.WriteLine($"✓ Found {tools.Count} available tools:");
    Console.WriteLine();

    foreach (McpClientTool tool in tools)
    {
        Console.WriteLine($"  • {tool.Name}");
        Console.WriteLine($"    Description: {tool.Description}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"✗ Failed to retrieve tools: {ex.Message}");
    Console.WriteLine("Press any key to exit...");
    Console.ReadKey();
    return;
}

Console.WriteLine();
Console.WriteLine("=== Interactive Chat Mode ===");
Console.WriteLine("You can now interact with the MCP tools through natural language.");
Console.WriteLine("Type 'exit' or 'quit' to end the session.");
Console.WriteLine();

// Conversational loop that can utilize the tools via prompts
List<ChatMessage> messages = [];

while (true)
{
    Console.Write("You: ");
    string? userInput = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(userInput))
    {
        continue;
    }

    if (
        userInput.Equals("exit", StringComparison.OrdinalIgnoreCase)
        || userInput.Equals("quit", StringComparison.OrdinalIgnoreCase)
    )
    {
        Console.WriteLine("\nGoodbye!");
        break;
    }

    messages.Add(new(ChatRole.User, userInput));

    Console.Write("Assistant: ");

    try
    {
        List<ChatResponseUpdate> updates = [];
        await foreach (
            ChatResponseUpdate update in chatClient.GetStreamingResponseAsync(
                messages,
                new() { Tools = [.. tools] }
            )
        )
        {
            Console.Write(update);
            updates.Add(update);
        }
        Console.WriteLine();
        Console.WriteLine();

        messages.AddMessages(updates);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n✗ Error: {ex.Message}");
        Console.WriteLine();
    }
}