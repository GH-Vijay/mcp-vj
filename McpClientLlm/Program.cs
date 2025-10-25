using System.Text.Json;
using Azure;
using ModelContextProtocol.Client;
using Azure.AI.OpenAI;
using OpenAI.Chat;

var clientTransport = new StdioClientTransport(new()
    {
        Name = "Calculator Server",
        Command = "dotnet",
        Arguments = ["run", "--project", "../McpCalculatorServer/McpCalculatorServer.csproj"],
    });
Console.WriteLine("Setting up stdio transport");
await using var mcpClient = await McpClient.CreateAsync(clientTransport);
var tools = await GetMcpTools();

var requestOptions = new ChatCompletionOptions();
List<ChatMessage> messages = new List<ChatMessage>()
{
    new SystemChatMessage("You are a helpful assistant that knows about AI."),
    new UserChatMessage("add 4 and 4"),
};
requestOptions.Tools.Add(tools[0]);

var endpoint = new Uri("https://oai-vs-sub-eastus2.openai.azure.com");
var deploymentName = "gpt-5-mini";
var apiKey = Environment.GetEnvironmentVariable("AZURE_API_KEY");
AzureOpenAIClient azureClient = new(
    endpoint,
    new AzureKeyCredential(apiKey));
ChatClient chatClient = azureClient.GetChatClient(deploymentName);
var response = chatClient.CompleteChat(messages, requestOptions);
var content = response.Value;
Console.WriteLine($"Assistant response: {content}");

for (int i = 0; i < response.Value.ToolCalls.Count; i++)
{
    var call = response.Value.ToolCalls[i];
    Console.WriteLine($"Tool call #{i}: '{call.FunctionName}' with arguments {call.FunctionArguments}");

    var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(call.FunctionArguments);
    var result = await mcpClient.CallToolAsync(
        call.FunctionName,
        dict!,
        cancellationToken: CancellationToken.None
    );

    var respText = (ModelContextProtocol.Protocol.TextContentBlock?)result.Content.FirstOrDefault(c => c.Type == "text");
    if (respText != null)
    {
        Console.WriteLine($"Result of Tool Call '{call.FunctionName}': " + respText.Text);
    }

}

ChatTool ConvertFromMcpToOpenAiChatTool(string name, string description, JsonElement propertiesElement)
{
    // Build a JSON Schema object { "type":"object", "properties": { ... } }
    var schema = new
    {
        type = "object",
        properties = propertiesElement
    };

    // Serialize to JSON string (or to BinaryData if the SDK expects BinaryData)
    var parametersJson = JsonSerializer.Serialize(schema, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    });

    var tool = ChatTool.CreateFunctionTool(name, description, BinaryData.FromString(parametersJson));

    return tool;
}

async Task<List<OpenAI.Chat.ChatTool>> GetMcpTools()
{
    var mcpTools = await mcpClient.ListToolsAsync();
    var list = new List<OpenAI.Chat.ChatTool>();

    foreach (var t in mcpTools)
    {
        t.JsonSchema.TryGetProperty("properties", out JsonElement props);
        var chatTool = ConvertFromMcpToOpenAiChatTool(t.Name, t.Description, props);
        list.Add(chatTool);
    }

    return list;
}
