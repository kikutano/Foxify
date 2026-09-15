# Agent AI Guidelines and Rules

## Core Principles

1. **Always Follow Instructions**: Strictly adhere to the user's explicit instructions without making assumptions
2. **Consistency**: Maintain consistent code style, formatting, and approach throughout all tasks
3. **Quality First**: Prioritize clean, maintainable, and well-documented code
4. **Error Prevention**: Avoid mistakes by following established patterns and best practices

## Task Execution Rules

1. **Understand Before Acting**:
   - Fully comprehend the requirements before starting any implementation
   - Break complex tasks into smaller, manageable steps
   - Ask clarifying questions when requirements are ambiguous

2. **File Operations**:
   - Use `write_to_file` for new files or completely rewriting existing files
   - Use `replace_in_file` for targeted edits to existing files
   - Never modify files without understanding the context and impact

3. **Code Standards**:
   - Follow existing code patterns in the repository
   - Maintain consistent indentation and formatting
   - Use meaningful variable and function names
   - Include appropriate comments for complex logic

4. **Testing and Validation**:
   - Verify file creation/updates were successful before proceeding
   - Test implementations when possible
   - Ensure all required dependencies are met

## Technical Guidelines

1. **Working Directory**:
   - Always operate within the current working directory: c:\Users\Tano\source\repos\kikutano\Foxify
   - Do not attempt to change directories or access files outside this scope

2. **Tool Usage**:
   - Use available tools in the correct sequence:
     - First, read existing files to understand context
     - Then, make targeted changes using replace_in_file when appropriate
     - Finally, create new files with write_to_file when needed
   - When creating new projects, organize all files within dedicated project directories

3. **Command Execution**:
   - Use `execute_command` only when necessary for system operations
   - Provide clear explanations of what each command does
   - Avoid interactive commands that require user input

4. **Error Handling**:
   - If a tool fails, analyze the error and adjust approach accordingly
   - Do not proceed with incomplete steps
   - Use `ask_followup_question` when additional information is needed

## Communication Rules

1. **Clarity**:
   - Communicate clearly and concisely
   - Avoid ambiguity in instructions and explanations
   - Provide sufficient context for understanding

2. **Progress Tracking**:
   - Use task_progress to track implementation steps
   - Update progress after each significant action
   - Complete checklists to show completion status

3. **Completion**:
   - Always use `attempt_completion` when task is finished
   - Do not end with questions or requests for further assistance
   - Provide final summary of what was accomplished

## Specific Considerations

1. **Project Structure**:
   - Respect existing project structure and conventions
   - Follow established naming patterns
   - Maintain appropriate file organization

2. **Security and Safety**:
   - Never execute commands that could harm the system
   - Avoid creating files with potentially dangerous content
   - Be cautious when using external tools or APIs

3. **Documentation**:
   - Maintain clear documentation of all changes made
   - Ensure code is well-commented
   - Keep implementation details consistent with project standards

## Reference Materials

1. **Repository Context**:
   - Use `list_files` and `read_file` to explore the codebase when needed
   - Reference existing files to understand patterns and conventions
   - Leverage `search_files` for finding specific implementations or patterns

2. **Best Practices**:
   - Follow established coding standards for the project's language/framework
   - Maintain consistency with other parts of the codebase
   - Apply appropriate design patterns where suitable

This document serves as a comprehensive guide for proper task execution and code generation within this environment.