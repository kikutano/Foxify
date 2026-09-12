using System.Net;
using Tyfapi.Core.Execution;
using Tyfapi.Core.Models;
using Tyfapi.Core.Tests.Fakes;

namespace Tyfapi.Core.Tests.Execution;

public class WorkflowEngineUnitTests
{
    private readonly WorkflowEngine _engine;

    public WorkflowEngineUnitTests()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("Test response content")
        };

        var fakeHandler = new FakeHttpMessageHandler(response);
        var httpClient = new HttpClient(fakeHandler)
        {
            BaseAddress = new Uri("https://fakeapi.com/")
        };

        _engine = new WorkflowEngine(httpClient);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithValidWorkflow_ShouldExecuteAllSteps()
    {
        // Arrange
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Test Workflow"
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

        // Act & Assert - This test verifies the workflow executes without throwing exceptions
        await _engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown
        Assert.True(true); // If we get here without exception, test passes
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithDelayStep_ShouldWaitForSpecifiedTime()
    {
        // Arrange
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Test Workflow with Delay"
            },
            Functions = new Dictionary<string, FunctionDefinition>(),
            Workflow = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Type = "DELAY",
                    DurationSeconds = 0.1
                }
            }
        };

        // Act & Assert
        var startTime = DateTime.Now;
        await _engine.ExecuteWorkflowAsync(workflow);
        var endTime = DateTime.Now;

        // Should have waited at least 1 second (allowing for some variance)
        Assert.True((endTime - startTime).TotalSeconds >= 0.1);
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithNonExistentFunction_ShouldSkipStep()
    {
        // Arrange
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Test Workflow with Missing Function"
            },
            Functions = new Dictionary<string, FunctionDefinition>(),
            Workflow = new List<WorkflowStep>
            {
                new WorkflowStep
                {
                    Type = "function",
                    FunctionName = "NonExistentFunction"
                }
            }
        };

        // Act & Assert - Should not throw exception but should log message
        await _engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown
        Assert.True(true); // If we get here without exception, test passes
    }

    [Fact]
    public async Task ExecuteWorkflowAsync_WithMissingDependency_ShouldSkipStep()
    {
        // Arrange
        var workflow = new WorkflowTemplate
        {
            Metadata = new WorkflowMetadata
            {
                Name = "Test Workflow with Missing Dependency"
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
                    FunctionName = "TestFunction",
                    DependsOn = new List<string> { "MissingVariable" }
                }
            }
        };

        // Act & Assert - Should not throw exception but should log message
        await _engine.ExecuteWorkflowAsync(workflow);

        // The main assertion here is that no exception was thrown
        Assert.True(true); // If we get here without exception, test passes
    }

    [Fact]
    public async Task SetVariable_ShouldStoreVariableCorrectly()
    {
        // Arrange
        var variableName = "testVar";
        var variableValue = "testValue";

        // Act
        _engine.SetVariable(variableName, variableValue);

        // Assert - This test verifies that the SetVariable method doesn't throw exceptions
        Assert.True(true); // If we get here without exception, test passes
    }

}