#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.SemanticKernel, 1.66.0"
#r "nuget: Microsoft.SemanticKernel.Agents.Core, 1.66.0"
#r "nuget: Microsoft.SemanticKernel.Agents.Abstractions, 1.66.0"
#r "nuget: Microsoft.SemanticKernel.Connectors.OpenAI, 1.66.0"
#r "nuget: Octokit, 13.0.1"
#r "nuget: DotNetEnv, 3.0.0"

#load "helpers/SemanticKernel/AgentTask.cs"

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Agents;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using Octokit;
using DotNetEnv;

// Load environment variables
Env.Load();

var openaiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
var githubOwner = Environment.GetEnvironmentVariable("GITHUB_OWNER");
var githubRepo = Environment.GetEnvironmentVariable("GITHUB_REPO");
var prNumber = int.Parse(Environment.GetEnvironmentVariable("PR_NUMBER") ?? "0");

// GitHub Plugin (same as in the notebook)
public class GitHubPlugin
{
    private readonly GitHubClient _client;
    private readonly string _owner;
    private readonly string _repo;

    public GitHubPlugin(string token, string owner, string repo)
    {
        _client = new GitHubClient(new ProductHeaderValue("PR-Description-Agent"));
        _client.Credentials = new Credentials(token);
        _owner = owner;
        _repo = repo;
    }

    [KernelFunction("get_pr_details")]
    [Description("Fetches pull request details including title, branch name, and description")]
    public async Task<string> GetPRDetailsAsync([Description("The pull request number")] int prNumber)
    {
        var pr = await _client.PullRequest.Get(_owner, _repo, prNumber);
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            number = pr.Number,
            title = pr.Title,
            head_branch = pr.Head.Ref,
            base_branch = pr.Base.Ref,
            author = pr.User.Login
        });
    }

    [KernelFunction("get_pr_files")]
    [Description("Fetches the list of files changed in a pull request")]
    public async Task<string> GetPRFilesAsync([Description("The pull request number")] int prNumber)
    {
        var files = await _client.PullRequest.Files(_owner, _repo, prNumber);
        var fileList = files.Select(f => new { filename = f.FileName, changes = f.Changes }).ToList();
        return System.Text.Json.JsonSerializer.Serialize(fileList);
    }

    [KernelFunction("get_pr_diff")]
    [Description("Fetches the complete diff for a pull request")]
    public async Task<string> GetPRDiffAsync([Description("The pull request number")] int prNumber)
    {
        var files = await _client.PullRequest.Files(_owner, _repo, prNumber);
        var diff = string.Join("\n\n", files.Select(f => $"File: {f.FileName}\n{f.Patch}"));
        return diff.Length > 10000 ? diff.Substring(0, 10000) + "\n[truncated]" : diff;
    }
}

// Initialize Semantic Kernel
var builder = Kernel.CreateBuilder();
builder.AddOpenAIChatCompletion("gpt-4o", openaiApiKey);
var kernel = builder.Build();

// Add GitHub plugin
var githubPlugin = new GitHubPlugin(githubToken, githubOwner, githubRepo);
kernel.ImportPluginFromObject(githubPlugin, "GitHub");

// Create agent
var agent = new ChatCompletionAgent
{
    Name = "PRDescriptionPopulator",
    Instructions = """
    Fill out the PR description with:
    1. What is the change?
    2. Details on code changes
    3. Code areas to focus on (file paths only)
    4. Related PRs (search using Jira ID if found)
    5. Jira Ticket Info (extract from branch, link to https://ungerboeck.atlassian.net/browse/[ID])
    
    Use GitHub functions to fetch data. Output in clean markdown.
    """,
    Kernel = kernel
};

// Create thread
var thread = new ChatHistoryAgentThread();

// Create task
var prDescriptionTask = new AgentTask
{
    Description = $"Generate PR description for PR #{prNumber}",
    Agent = agent,
    Thread = thread,
    KernelArguments = new KernelArguments(new OpenAIPromptExecutionSettings 
    { 
        FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(),
        MaxTokens = 2000 
    })
};

// Execute
var result = await prDescriptionTask.InvokeAsync();

// Save output
File.WriteAllText("pr-description-output.md", result);
Console.WriteLine("✅ PR description generated and saved!");
