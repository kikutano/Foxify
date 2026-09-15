# Variable Extraction Guide

This guide explains how to extract variables from API responses and use them in subsequent requests within the Foxify workflow engine.

## Overview

The Foxify engine supports extracting values from API responses using JSONPath expressions. This allows you to:
- Extract authentication tokens, IDs, or other data from one request
- Use that extracted data in subsequent requests through variable substitution
- Create dynamic workflows that depend on previous API responses

## How It Works

### 1. Defining Extraction Points

In your workflow definition, add an `extract` section to any HTTP_REQUEST function:

```yaml
LoginUser:
  type: HTTP_REQUEST
  method: POST
  endpoint: "login"
  extract:
    token: $.token
    user_id: $.user.id
    username: $.user.username
```

### 2. Using Extracted Variables

Once variables are extracted, you can use them in subsequent requests:

```yaml
GetProtectedResource:
  type: HTTP_REQUEST
  method: GET
  endpoint: "me"
  headers:
    Authorization: "Bearer ${token}"
```

## Supported JSONPath Expressions

The engine supports basic JSONPath expressions:

### Simple Paths
- `$.token` - Extracts the token field at root level
- `$.user.id` - Extracts the id field from the user object
- `$.items[0].name` - Extracts the name field from the first item in an array

### Example Response Structure
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "user": {
    "id": 123,
    "username": "test_user",
    "email": "test@example.com"
  },
  "expires_at": "2026-07-21T10:00:00Z"
}
```

## Complete Workflow Example

Here's a complete example showing variable extraction in action:

```yaml
# Function to extract multiple variables from login response
LoginUser:
  type: HTTP_REQUEST
  method: POST
  endpoint: "login"
  body: '{
    "username": "test_user",
    "password": "test_password"
  }'
  extract:
    token: $.token
    user_id: $.user.id
    username: $.user.username

# Function that uses the extracted variables
GetProtectedResource:
  type: HTTP_REQUEST
  method: GET
  endpoint: "me"
  headers:
    Authorization: "Bearer ${token}"
    Content-Type: application/json
  depends_on: [LoginUser]
```

## Best Practices

1. **Always Check Dependencies**: Use `depends_on` to ensure variables are available before using them
2. **Use Descriptive Names**: Choose variable names that clearly indicate their purpose
3. **Handle Optional Fields**: Be aware that missing fields will not cause errors but won't be extracted
4. **Test Extraction Logic**: Validate your JSONPath expressions work with actual API responses

## Common Use Cases

### Authentication Flows
```yaml
LoginUser:
  type: HTTP_REQUEST
  method: POST
  endpoint: "login"
  extract:
    token: $.token
    refresh_token: $.refresh_token

GetProtectedResource:
  type: HTTP_REQUEST
  method: GET
  endpoint: "api/resource"
  headers:
    Authorization: "Bearer ${token}"
```

### Multi-Step Workflows
```yaml
CreateUser:
  type: HTTP_REQUEST
  method: POST
  endpoint: "users"
  extract:
    user_id: $.id
    created_at: $.created_at

UpdateUser:
  type: HTTP_REQUEST
  method: PUT
  endpoint: "users/${user_id}"
  body: '{
    "name": "Updated Name"
  }'
```

## Error Handling

If a JSONPath expression doesn't match, the variable will not be extracted but won't cause workflow failure. This allows for graceful handling of optional or conditional data extraction.