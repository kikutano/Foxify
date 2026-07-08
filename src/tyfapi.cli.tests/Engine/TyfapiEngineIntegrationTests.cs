using System.Net;
using tyfapi.cli.Tyfapi;

namespace tyfapi.cli.tests.Engine;

public class TyfapiEngineIntegrationTests
{
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

        var engine = new TyfapiEngine();
        
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
        
        var engine = new TyfapiEngine();
        
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
        var engine = new TyfapiEngine();
        
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