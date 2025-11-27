using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

public class AgentTask 
{
    public string Description { get; set; }
    public AgentThread Thread { get; set; }
    public Agent Agent { get; set; }
    public KernelArguments KernelArguments { get; set; }

    public async Task<string> InvokeAsync(string input = default)
    {
        var messages = new List<Microsoft.SemanticKernel.ChatMessageContent>
        {
            new (AuthorRole.User, this.Description),
        };

        if (input != default(dynamic))
        {
            messages.Add(new (AuthorRole.User, input));
        }

        var builder = new StringBuilder();

        var options = new AgentInvokeOptions() 
        {
            KernelArguments = this.KernelArguments
        };

        await foreach (var stream in this.Agent.InvokeAsync(messages, this.Thread, options))
        {
            builder.Append(stream.Message);
        }

        return builder.ToString();
    }

    public async Task<T> InvokeAsync<T>(string input) 
    {
        var response = await this.InvokeAsync(input);
        return JsonSerializer.Deserialize<T>(response);
    }

    public async Task<T> InvokeAsync<T>(dynamic input = default) 
    {
        var serializedInput = input != default(dynamic) ? JsonSerializer.Serialize(input) : default(string);
        var response = await this.InvokeAsync(serializedInput);
        return JsonSerializer.Deserialize<T>(response);
    }
}