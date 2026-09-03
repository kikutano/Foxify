# Cline AI Agent Guidelines and Rules

## Core Principles

1. **Project-Specific Compliance**: Strictly adhere to tyfapi project conventions and patterns
2. **YAML Schema Adherence**: Follow the exact YAML structure defined for workflow definitions
3. **CLI Engine Integration**: Ensure all generated code integrates properly with the .NET Native AOT CLI engine
4. **AI-Friendly Design**: Create content that's optimized for LLM generation and understanding

## Coding Standards

### YAML Workflow Definitions
1. **Metadata Structure**: Always include complete metadata section with name, description, environment, and api_version
2. **Function Definitions**: 
   - Use HTTP_REQUEST type for all API calls
   - Follow exact variable substitution syntax: `${variable_name}`
   - Include proper headers with Content-Type and Authorization where needed
   - Define extract sections for response data handling
3. **Workflow Steps**:
   - Use explicit function calls with function_name parameter
   - Include depends_on arrays for proper dependency management
   - Add DELAY steps when simulating human-like timing
4. **Settings Section**: Always include timeout and max_retries values

### C# Code Conventions
1. **Native AOT Compliance**: 
   - Avoid reflection usage
   - Use Source Generators where appropriate
   - Follow strict AOT-compliant practices
2. **Error Handling**:
   - Implement proper exception handling
   - Include meaningful error messages
3. **Performance Considerations**:
   - Leverage HttpClient with SocketsHttpHandler
   - Minimize memory allocations
   - Ensure thread safety where required

## Workflow Execution Guidelines

### Variable Management
1. **Global Variables**: Reference using `${GLOBAL_TOKEN}` format
2. **Local Variables**: Use `${variable_name}` syntax for function outputs
3. **Variable Scope**: Understand the difference between global and function-scoped variables
4. **Dependency Tracking**: Always define depends_on relationships properly

### Function Design
1. **HTTP Request Structure**:
   - Define baseurl or endpoint clearly
   - Include proper method (GET, POST, PUT, etc.)
   - Use JSON body formatting with proper escaping
2. **Response Handling**:
   - Extract data using JSONPath expressions
   - Define clear variable names for extracted data
   - Handle optional response fields appropriately

### Testing and Validation
1. **Schema Validation**: Ensure all workflow files follow the defined schema
2. **Execution Simulation**: Create workflows that can be properly executed by the CLI engine
3. **Variable Flow**: Verify that dependencies are properly managed between steps

## Integration Considerations

### VS Code Extension Compatibility
1. **File Structure**: Follow existing project directory conventions
2. **Naming Patterns**: Use consistent naming for workflow files and variables
3. **Documentation**: Include appropriate comments for better IDE integration

### AI Generation Optimization
1. **Minimal Nesting**: Keep YAML structure flat for easier LLM parsing
2. **Clear Semantics**: Use descriptive names that are easy to understand
3. **Consistent Patterns**: Follow established patterns in existing workflow examples
4. **Example Templates**: Reference existing templates like simple_workflow_template.yaml and login_flow_example.yaml

## Best Practices

1. **Consistency**: Match the formatting and structure of existing workflow files
2. **Readability**: Write YAML that's easy for both humans and AI to understand
3. **Maintainability**: Create workflows that are easy to modify and extend
4. **Performance**: Design workflows that work efficiently with the Native AOT engine
5. **Error Prevention**: Anticipate potential issues and include proper safeguards

## Reference Materials

1. **Existing Examples**:
   - simple_workflow_template.yaml
   - login_flow_example.yaml
   - test_variables.yaml
   - test_with_variables.yaml

2. **Core Documentation**:
   - README.md for project architecture overview
   - agent_ai.md for general AI agent guidelines
   - src/Tyfapi.Core/Execution/WorkflowEngine.cs for implementation details

This document serves as a comprehensive guide for proper task execution and code generation within the tyfapi project environment.