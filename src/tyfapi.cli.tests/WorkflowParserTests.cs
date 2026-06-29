using TyfApi.Cli.Parsers;

namespace TyfApi.Cli.Tests;

public class WorkflowParserTests
{
    private readonly WorkflowParser _parser = new();

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
}