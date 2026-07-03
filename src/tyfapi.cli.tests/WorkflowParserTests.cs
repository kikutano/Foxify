using tyfapi.cli.Tyfapi;

namespace TyfApi.Cli.Tests;

public class WorkflowParserTests
{
    private readonly TyfapiParser _parser = new();

    [Fact]
    public void Parse_WithValidYaml_ReturnsPopulatedTemplate()
    {
        // Arrange
        string yaml = @"
            metadata:
              name: ""Example User Journey - Full Checkout Loop""
              description: ""Tests the full end-to-end user workflow from listing products to final purchase confirmation.""
              environment: ""dev_credentials.json""
              api_version: ""v1""

            functions:
              GetUserDetails:
                type: HTTP_REQUEST
                method: GET
                endpoint: ""/api/v1/user/{user_id}""
                headers:
                  Content-Type: application/json
                  Authorization: $${GLOBAL_TOKEN}
                body: """"
                extract:
                  current_profile_data: $.userProfileData

              CreateProductRecord:
                type: HTTP_REQUEST
                method: POST
                endpoint: ""/api/v1/product/create""
                headers:
                  Content-Type: application/json
                  X-Source-ID: $${user_id}
                body: '{""input_param"": ""Initial test value"", ""linked_by"": ""$${user_id}""}'
                extract:
                  product_record_id: $.data.newProductId

              UpdateFinalStatus:
                type: HTTP_REQUEST
                method: PUT
                endpoint: ""/api/v1/status/$${resource_id}""
                headers:
                  Authorization: $${GLOBAL_TOKEN}
                  Content-Type: application/json
                body: '{""status"": ""success"", ""notes"": ""Finished testing cycle.""}'
                extract:
                  final_status_code: $.data.httpStatusCode

            workflow:
              - type: function
                function_name: GetUserDetails
              - name: FlowDelay1
                type: DELAY
                duration_seconds: 2.0
              - type: function
                function_name: CreateProductRecord
                depends_on: [GetUserDetails]
              - name: FlowDelay2
                type: DELAY
                duration_seconds: 1.0
              - type: function
                function_name: UpdateFinalStatus
                depends_on: [CreateProductRecord]

            settings:
              timeout: 20
              max_retries: 3";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Example User Journey - Full Checkout Loop", result.Metadata.Name);
        Assert.Equal("v1", result.Metadata.ApiVersion);
        Assert.Equal("HTTP_REQUEST", result.Functions["GetUserDetails"].Type);
        Assert.Equal("GET", result.Functions["GetUserDetails"].Method);
    }

    [Fact]
    public void Parse_EmptyContent_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _parser.Parse(""));
        Assert.Throws<ArgumentException>(() => _parser.Parse("   "));
    }

    [Fact]
    public void Parse_WithValidYaml_CompletesAllMetadataAssertions()
    {
        // Arrange
        string yaml = @"
            metadata:
              name: ""Example User Journey - Full Checkout Loop""
              description: ""Tests the full end-to-end user workflow from listing products to final purchase confirmation.""
              environment: ""dev_credentials.json""
              api_version: ""v1""

            functions:
              GetUserDetails:
                type: HTTP_REQUEST
                method: GET
                endpoint: ""/api/v1/user/{user_id}""
                headers:
                  Content-Type: application/json
                  Authorization: $${GLOBAL_TOKEN}
                body: """"
                extract:
                  current_profile_data: $.userProfileData

              CreateProductRecord:
                type: HTTP_REQUEST
                method: POST
                endpoint: ""/api/v1/product/create""
                headers:
                  Content-Type: application/json
                  X-Source-ID: $${user_id}
                body: '{""input_param"": ""Initial test value"", ""linked_by"": ""$${user_id}""}'
                extract:
                  product_record_id: $.data.newProductId

              UpdateFinalStatus:
                type: HTTP_REQUEST
                method: PUT
                endpoint: ""/api/v1/status/$${resource_id}""
                headers:
                  Authorization: $${GLOBAL_TOKEN}
                  Content-Type: application/json
                body: '{""status"": ""success"", ""notes"": ""Finished testing cycle.""}'
                extract:
                  final_status_code: $.data.httpStatusCode

            workflow:
              - type: function
                function_name: GetUserDetails
              - name: FlowDelay1
                type: DELAY
                duration_seconds: 2.0
              - type: function
                function_name: CreateProductRecord
                depends_on: [GetUserDetails]
              - name: FlowDelay2
                type: DELAY
                duration_seconds: 1.0
              - type: function
                function_name: UpdateFinalStatus
                depends_on: [CreateProductRecord]

            settings:
              timeout: 20
              max_retries: 3";

        // Act
        var result = _parser.Parse(yaml);

        // Assert - Additional comprehensive assertions
        Assert.NotNull(result);
        Assert.Equal("Example User Journey - Full Checkout Loop", result.Metadata.Name);
        Assert.Equal("Tests the full end-to-end user workflow from listing products to final purchase confirmation.", result.Metadata.Description);
        Assert.Equal("dev_credentials.json", result.Metadata.Environment);
        Assert.Equal("v1", result.Metadata.ApiVersion);

        // Assert functions
        Assert.NotNull(result.Functions);
        Assert.True(result.Functions.ContainsKey("GetUserDetails"));
        Assert.True(result.Functions.ContainsKey("CreateProductRecord"));
        Assert.True(result.Functions.ContainsKey("UpdateFinalStatus"));

        // Assert specific function details
        var getUserDetails = result.Functions["GetUserDetails"];
        Assert.Equal("HTTP_REQUEST", getUserDetails.Type);
        Assert.Equal("GET", getUserDetails.Method);
        Assert.Equal("/api/v1/user/{user_id}", getUserDetails.Endpoint);
        Assert.True(getUserDetails.Headers.ContainsKey("Content-Type"));
        Assert.True(getUserDetails.Headers.ContainsKey("Authorization"));
        Assert.Equal("application/json", getUserDetails.Headers["Content-Type"]);
        Assert.Equal("$${GLOBAL_TOKEN}", getUserDetails.Headers["Authorization"]);
        Assert.Equal("", getUserDetails.Body);
        Assert.True(getUserDetails.Extract.ContainsKey("current_profile_data"));
        Assert.Equal("$.userProfileData", getUserDetails.Extract["current_profile_data"]);

        // Assert workflow steps
        Assert.NotNull(result.Workflow);
        Assert.Equal(5, result.Workflow.Count);

        // Assert first step
        var firstStep = result.Workflow[0];
        Assert.Equal("function", firstStep.Type);
        Assert.Equal("GetUserDetails", firstStep.FunctionName);
        Assert.Null(firstStep.Name);
        Assert.Null(firstStep.DependsOn);
        Assert.Null(firstStep.DurationSeconds);

        // Assert delay step
        var delayStep = result.Workflow[1];
        Assert.Equal("DELAY", delayStep.Type);
        Assert.Equal("FlowDelay1", delayStep.Name);
        Assert.Equal(2.0, delayStep.DurationSeconds);
        Assert.Null(delayStep.FunctionName);
        Assert.Null(delayStep.DependsOn);

        // Assert settings
        Assert.NotNull(result.Settings);
        Assert.Equal(20, result.Settings.Timeout);
        Assert.Equal(3, result.Settings.MaxRetries);
    }

    [Fact]
    public void Parse_WithMinimalYaml_ReturnsPopulatedTemplate()
    {
        // Arrange
        string yaml = @"
            metadata:
              name: ""Minimal Test""
              api_version: ""v1""

            functions:
              SimpleFunction:
                type: HTTP_REQUEST
                method: GET
                endpoint: ""/api/v1/simple""

            workflow:
              - type: function
                function_name: SimpleFunction";

        // Act
        var result = _parser.Parse(yaml);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Minimal Test", result.Metadata.Name);
        Assert.Equal("v1", result.Metadata.ApiVersion);
        Assert.Single(result.Functions);
        Assert.True(result.Functions.ContainsKey("SimpleFunction"));
        Assert.Equal(1, result.Workflow.Count);

        var step = result.Workflow[0];
        Assert.Equal("function", step.Type);
        Assert.Equal("SimpleFunction", step.FunctionName);
    }

}
