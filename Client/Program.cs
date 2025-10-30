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

var chatClient = new ChatClientBuilder(
    new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
        .GetChatClient(deployment)
        .AsIChatClient()
)
    .UseFunctionInvocation()
    .Build();

var transport = new HttpClientTransport(
    new HttpClientTransportOptions { Endpoint = new Uri("http://localhost:5000") }
);

var mcpClient = await McpClient.CreateAsync(transport);

var tools = await mcpClient.ListToolsAsync();

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
