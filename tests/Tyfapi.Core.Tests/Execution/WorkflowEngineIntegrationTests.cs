using System.Net;
using Tyfapi.Core.Execution;
using Tyfapi.Core.Models;
using Tyfapi.Core.Parsing;
using Tyfapi.Core.Tests.Fakes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Tyfapi.Core.Tests.Execution;

public class WorkflowEngineIntegrationTests
{
    private const string WorkFlowYamlSource = @"
        # Final Demo Workflow: Complete Variable Extraction Example
        # -----------------------------------------
        # Metadata provides global info for tracking, reporting, and AI context.
        metadata:
          name: ""Final Demo Workflow"" # Human-readable name of the flow/test.
          description: ""Complete demonstration of variable extraction from API responses and usage in subsequent requests.""
          api_version: ""v1""                           # Explicit versioning for schema tracking.

        # -----------------------------------------
        # Functions/Templates Library: Reusable API Blocks
        functions:
          # Function 1: Perform Login (POST Request) - Extracts multiple variables
          LoginUser:
            type: HTTP_REQUEST
            method: POST
            baseurl: ${baseUrlDev}
            endpoint: ""login""
            headers:
              Content-Type: application/json
            body: '{
              ""username"": ""test_user"",
              ""password"": ""test_password""
            }'
            extract:
              token: $.token # Saves the bearer token into a variable accessible by name.
              user_id: $.user.id # Saves the user ID into a variable accessible by name.
              username: $.user.username # Saves the username into a variable accessible by name.
              expires_at: $.expires_at # Saves expiration timestamp

          # Function 2: Get Protected Resource (GET Request using Bearer Token)
          GetProtectedResource:
            type: HTTP_REQUEST
            method: GET
            baseurl: ${baseUrlDev}
            endpoint: ""me""
            headers:
              Content-Type: application/json
              Authorization: ""Bearer ${token}""     # Uses the token from login step.
            body: """"
            extract:
              resource_data: $.data # Saves protected resource data into a variable accessible by name.

          # Function 3: Get User Details using extracted user_id
          GetUserDetails:
            type: HTTP_REQUEST
            method: GET
            baseurl: ${baseUrlDev}
            endpoint: ""user/${user_id}"" # Uses the user_id from login step
            headers:
              Content-Type: application/json
              Authorization: ""Bearer ${token}""     # Uses the token from login step.
            body: """"
            extract:
              user_details: $.data # Saves user details into a variable accessible by name.

        # -----------------------------------------
        # Workflow Definition (The Execution Sequence)
        workflow:
          - type: function
            function_name: LoginUser # Calls the defined ""LoginUser"" block to authenticate.

          # Optional delay between login and subsequent call
          - name: FlowDelay1
            type: DELAY
            duration_seconds: 1.0

          # Execute protected resource call using the token from login
          - type: function
            function_name: GetProtectedResource
            depends_on: [LoginUser] # Ensures 'token' is available for this step's authorization header.

          # Execute user details call using extracted user_id and token
          - type: function
            function_name: GetUserDetails
            depends_on: [LoginUser] # Ensures 'user_id' and 'token' are available for this step's headers and endpoint.

        settings:
          timeout: 10     # Global timeout (seconds) for any single API call.
          max_retries: 3   # Number of times to retry transient failures.";

    [Fact]
    public async Task ExecuteWorkflowAsync_WithFakeHttpMessageHandler_ShouldExecuteWithoutException()
    {
        // Arrange
        var fakeHandler = new FakeHttpLoginMessageHandler();
        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://fakeapi.com/") // Base URL for the fake API
        };

        var envDeserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .Build();

        var environmentVariables = envDeserializer
            .Deserialize<Dictionary<string, object>>(WorkFlowYamlSource) ?? new();

        var parser = new WorkflowParser();
        var workflow = parser.Parse(WorkFlowYamlSource, environmentVariables);

        // Act & Assert - This should execute without throwing exceptions
        var engine = new WorkflowEngine(httpClient);
        await engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown during execution
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteFunctionStepAsync_WithFakeHttpMessageHandler_ShouldExecuteWithoutException()
    {
        // Arrange
        var fakeHandler = new FakeHttpMessageHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("Test response")
            }
        );

        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://fakeapi.com/") // Base URL for the fake API
        };

        var engine = new WorkflowEngine(httpClient);

        // Set up a workflow with a function that would make an HTTP call
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Integration Test Workflow"
            },
            Functions = new Dictionary<string, FunctionDefinition>
            {
                {
                    "TestFunction", new FunctionDefinition
                    {
                        Type = "HTTP_REQUEST",
                        Method = "GET",
                        Endpoint = "/api/test",
                        Headers = new Dictionary<string, string>(),
                        Body = "",
                        Extract = new Dictionary<string, string>()
                    }
                }
            },
            Workflow = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Type = "function",
                    FunctionName = "TestFunction"
                }
            }
        };

        // Act & Assert - This should execute without throwing exceptions
        await engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown during execution
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteFunctionStepAsync_WithValidResponse_ShouldHandleResponse()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Test response content")
        };

        var fakeHandler = new FakeHttpMessageHandler(response);

        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://fakeapi.com/")
        };

        var engine = new WorkflowEngine(httpClient);

        // Set up a workflow with a function that would make an HTTP call
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Response Handling Test"
            },
            Functions = new Dictionary<string, FunctionDefinition>
            {
                {
                    "TestFunction", new FunctionDefinition
                    {
                        Type = "HTTP_REQUEST",
                        Method = "POST",
                        Endpoint = "/api/test",
                        Headers = new Dictionary<string, string>
                        {
                            { "Content-Type", "application/json" }
                        },
                        Body = "{ \"test\": \"data\" }",
                        Extract = new Dictionary<string, string>()
                    }
                }
            },
            Workflow = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Type = "function",
                    FunctionName = "TestFunction"
                }
            }
        };

        // Act & Assert - This should execute without throwing exceptions
        await engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown during execution
        Assert.True(true);
    }

    [Fact]
    public async Task ExecuteDelayStepAsync_WithValidDuration_ShouldWaitCorrectly()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Test response content")
        };

        var fakeHandler = new FakeHttpMessageHandler(response);

        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://fakeapi.com/") // Base URL for the fake API
        };

        var engine = new WorkflowEngine(httpClient);

        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Delay Test"
            },
            Functions = new Dictionary<string, FunctionDefinition>(),
            Workflow = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Type = "DELAY",
                    DurationSeconds = 0.5
                }
            }
        };

        // Act & Assert - This should execute without throwing exceptions and should wait
        var startTime = DateTime.Now;
        await engine.ExecuteWorkflowAsync(workflow);
        var endTime = DateTime.Now;

        // Should have waited at least 0.5 seconds (allowing for some variance)
        Assert.True((endTime - startTime).TotalSeconds >= 0.5);
    }
}
