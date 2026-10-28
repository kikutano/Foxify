using System.Collections.Concurrent;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

//
// ============================================================================
// CONFIGURATION
// ============================================================================
//

const string ApiKey = "tinyprobe-test-api-key";
const string BearerToken = "tinyprobe-test-token";

//
// ============================================================================
// IN-MEMORY DATA
// ============================================================================
//

var users = new ConcurrentDictionary<int, User>(
    new Dictionary<int, User>
    {
        [1] = new(
            1,
            "Gaetano",
            "Lenoci",
            "gaetano@example.com",
            true,
            new Address(
                "Via Roma 1",
                "Cerignola",
                "Italy"
            )
        ),

        [2] = new(
            2,
            "Mario",
            "Rossi",
            "mario@example.com",
            true,
            new Address(
                "Via Milano 25",
                "Milano",
                "Italy"
            )
        ),

        [3] = new(
            3,
            "John",
            "Doe",
            "john@example.com",
            false,
            new Address(
                "1 Main Street",
                "London",
                "United Kingdom"
            )
        )
    });

var nextUserId = 4;

//
// ============================================================================
// PUBLIC ENDPOINTS
// ============================================================================
//

// -----------------------------------------------------------------------------
// GET /
// Basic health endpoint
// -----------------------------------------------------------------------------

app.MapGet("/", () =>
{
    return Results.Ok(new
    {
        name = "tinyprobe-test-server",
        version = "1.0.0",
        status = "ok",
        timestamp = DateTimeOffset.UtcNow
    });
});

// -----------------------------------------------------------------------------
// GET /api/public
// Public endpoint without authentication
// -----------------------------------------------------------------------------

app.MapGet("/api/public", () =>
{
    return Results.Ok(new
    {
        message = "This endpoint is public.",
        authenticated = false
    });
});

// -----------------------------------------------------------------------------
// POST /api/login
// Fake login
// -----------------------------------------------------------------------------

app.MapPost("/api/login", (LoginRequest request) =>
{
    if (request.Username != "admin" || request.Password != "password")
    {
        return Results.Json(
            new
            {
                error = "invalid_credentials",
                message = "Invalid username or password."
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        access_token = BearerToken,
        token_type = "Bearer",
        expires_in = 3600,
        refresh_token = "tinyprobe-refresh-token",
        user = new
        {
            id = 999,
            username = request.Username,
            roles = new[]
            {
                "admin",
                "user"
            }
        }
    });
});

//
// ============================================================================
// AUTHENTICATION
// ============================================================================
//

// Simple Bearer authentication helper.
//
// Expected:
//
// Authorization: Bearer tinyprobe-test-token
//
bool IsBearerAuthenticated(HttpRequest request)
{
    if (!request.Headers.TryGetValue("Authorization", out var value))
        return false;

    return value.ToString() == $"Bearer {BearerToken}";
}

// Simple API key helper.
//
// Expected:
//
// X-API-Key: tinyprobe-test-api-key
//
bool IsApiKeyAuthenticated(HttpRequest request)
{
    return request.Headers.TryGetValue("X-API-Key", out var value)
           && value.ToString() == ApiKey;
}

//
// ============================================================================
// AUTHENTICATION TEST ENDPOINTS
// ============================================================================
//

// -----------------------------------------------------------------------------
// GET /api/auth/bearer
// Tests Bearer authentication
// -----------------------------------------------------------------------------

app.MapGet("/api/auth/bearer", (HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Json(
            new
            {
                error = "unauthorized",
                message = "Missing or invalid Bearer token."
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        authenticated = true,
        authentication = "bearer",
        message = "Bearer authentication successful."
    });
});

// -----------------------------------------------------------------------------
// GET /api/auth/apikey
// Tests API key authentication
// -----------------------------------------------------------------------------

app.MapGet("/api/auth/apikey", (HttpRequest request) =>
{
    if (!IsApiKeyAuthenticated(request))
    {
        return Results.Json(
            new
            {
                error = "unauthorized",
                message = "Missing or invalid API key."
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        authenticated = true,
        authentication = "api-key",
        message = "API key authentication successful."
    });
});

// -----------------------------------------------------------------------------
// GET /api/auth/both
// Requires BOTH Bearer + API key
// -----------------------------------------------------------------------------

app.MapGet("/api/auth/both", (HttpRequest request) =>
{
    var bearer = IsBearerAuthenticated(request);
    var apiKey = IsApiKeyAuthenticated(request);

    if (!bearer || !apiKey)
    {
        return Results.Json(
            new
            {
                error = "unauthorized",
                bearer_valid = bearer,
                api_key_valid = apiKey
            },
            statusCode: StatusCodes.Status401Unauthorized);
    }

    return Results.Ok(new
    {
        authenticated = true,
        authentication = new[]
        {
            "bearer",
            "api-key"
        }
    });
});

//
// ============================================================================
// USERS - CRUD
// ============================================================================

// -----------------------------------------------------------------------------
// GET /api/users
//
// Protected
//
// Query parameters:
//
// ?active=true
// ?search=mario
// ?page=1&pageSize=10
//
// -----------------------------------------------------------------------------

app.MapGet("/api/users", (HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Unauthorized();
    }

    var activeQuery = request.Query["active"].ToString();
    var search = request.Query["search"].ToString();

    var page = int.TryParse(
        request.Query["page"],
        out var parsedPage)
        ? Math.Max(parsedPage, 1)
        : 1;

    var pageSize = int.TryParse(
        request.Query["pageSize"],
        out var parsedPageSize)
        ? Math.Clamp(parsedPageSize, 1, 100)
        : 10;

    IEnumerable<User> result = users.Values;

    if (bool.TryParse(activeQuery, out var active))
    {
        result = result.Where(x => x.Active == active);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        result = result.Where(x =>
            x.FirstName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            x.LastName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            x.Email.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    var total = result.Count();

    var data = result
        .OrderBy(x => x.Id)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .ToList();

    return Results.Ok(new
    {
        data,
        pagination = new
        {
            page,
            page_size = pageSize,
            total,
            total_pages = (int)Math.Ceiling(total / (double)pageSize)
        }
    });
});

// -----------------------------------------------------------------------------
// GET /api/users/{id}
// -----------------------------------------------------------------------------

app.MapGet("/api/users/{id:int}", (int id, HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Unauthorized();
    }

    if (!users.TryGetValue(id, out var user))
    {
        return Results.NotFound(new
        {
            error = "user_not_found",
            message = $"User {id} was not found."
        });
    }

    return Results.Ok(user);
});

// -----------------------------------------------------------------------------
// PUT /api/users/{id}/avatar
//
// A multipart/form-data body with a "file" part (docs/example/openapi). Nothing
// is stored: 204 when the user exists and a non-empty file was sent.
// -----------------------------------------------------------------------------

app.MapPut("/api/users/{id:int}/avatar", async (int id, HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Unauthorized();
    }

    if (!users.ContainsKey(id))
    {
        return Results.NotFound(new { error = "user_not_found", message = $"User {id} was not found." });
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "multipart_expected", message = "Send the avatar as multipart/form-data." });
    }

    var form = await request.ReadFormAsync();
    if (form.Files.GetFile("file") is not { Length: > 0 })
    {
        return Results.BadRequest(new { error = "file_missing", message = "The 'file' part is missing or empty." });
    }

    return Results.NoContent();
});

// -----------------------------------------------------------------------------
// POST /api/users
// -----------------------------------------------------------------------------

app.MapPost("/api/users", (
    CreateUserRequest request,
    HttpRequest httpRequest) =>
{
    if (!IsBearerAuthenticated(httpRequest))
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new
        {
            error = "validation_error",
            field = "email",
            message = "Email is required."
        });
    }

    var id = Interlocked.Increment(ref nextUserId);

    var user = new User(
        id,
        request.FirstName,
        request.LastName,
        request.Email,
        true,
        request.Address
    );

    users[id] = user;

    return Results.Created(
        $"/api/users/{id}",
        user);
});

// -----------------------------------------------------------------------------
// PUT /api/users/{id}
// Full replacement
// -----------------------------------------------------------------------------

app.MapPut("/api/users/{id:int}", (
    int id,
    UpdateUserRequest request,
    HttpRequest httpRequest) =>
{
    if (!IsBearerAuthenticated(httpRequest))
    {
        return Results.Unauthorized();
    }

    if (!users.ContainsKey(id))
    {
        return Results.NotFound(new
        {
            error = "user_not_found"
        });
    }

    var user = new User(
        id,
        request.FirstName,
        request.LastName,
        request.Email,
        request.Active,
        request.Address
    );

    users[id] = user;

    return Results.Ok(user);
});

// -----------------------------------------------------------------------------
// PATCH /api/users/{id}
// Partial update
// -----------------------------------------------------------------------------

app.MapPatch("/api/users/{id:int}", (
    int id,
    PatchUserRequest request,
    HttpRequest httpRequest) =>
{
    if (!IsBearerAuthenticated(httpRequest))
    {
        return Results.Unauthorized();
    }

    if (!users.TryGetValue(id, out var existing))
    {
        return Results.NotFound(new
        {
            error = "user_not_found"
        });
    }

    var updated = existing with
    {
        FirstName = request.FirstName ?? existing.FirstName,
        LastName = request.LastName ?? existing.LastName,
        Email = request.Email ?? existing.Email,
        Active = request.Active ?? existing.Active,
        Address = request.Address ?? existing.Address
    };

    users[id] = updated;

    return Results.Ok(updated);
});

// -----------------------------------------------------------------------------
// DELETE /api/users/{id}
// -----------------------------------------------------------------------------

app.MapDelete("/api/users/{id:int}", (
    int id,
    HttpRequest httpRequest) =>
{
    if (!IsBearerAuthenticated(httpRequest))
    {
        return Results.Unauthorized();
    }

    if (!users.TryRemove(id, out _))
    {
        return Results.NotFound(new
        {
            error = "user_not_found"
        });
    }

    return Results.NoContent();
});

//
// ============================================================================
// COMPLEX NESTED RESPONSE
// ============================================================================
//

// -----------------------------------------------------------------------------
// GET /api/catalog
//
// Deeply nested response with arrays and branching.
// -----------------------------------------------------------------------------

app.MapGet("/api/catalog", (HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        catalog = new
        {
            id = "catalog-001",
            name = "tinyprobe Test Catalog",

            metadata = new
            {
                created_by = new
                {
                    id = 123,
                    username = "admin",
                    permissions = new[]
                    {
                        "read",
                        "write",
                        "delete"
                    }
                },

                tags = new[]
                {
                    "test",
                    "mock",
                    "api"
                }
            },
            categories = new[]
            {
                new
                {
                    id = 1,
                    name = "Hardware",
                    products = new[]
                    {
                        new
                        {
                            id = 101,
                            name = "Keyboard",
                            price = 99.99,
                            specifications = new
                            {
                                features = new[] { "mechanical", "rgb" },
                                platforms = new[] { "windows", "linux", "macos" }
                            }
                        }
                    }
                },
                new
                {
                    id = 2,
                    name = "Software",
                    products = new[]
                    {
                        new
                        {
                            id = 201,
                            name = "tinyprobe Pro",
                            price = 49.0,
                            specifications = new
                            {
                                features = new[] { "cli", "native-aot" },
                                platforms = new[] { "windows", "linux", "macos" }
                            }
                        }
                    }
                }
            }
        }
    });
});

//
// ============================================================================
// LARGE USER LIST
// ============================================================================

// -----------------------------------------------------------------------------
// GET /api/users/list
//
// Intentionally nested response with many users.
// -----------------------------------------------------------------------------

app.MapGet("/api/users/list", (HttpRequest request) =>
{
    if (!IsBearerAuthenticated(request))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        success = true,

        result = new
        {
            generated_at = DateTimeOffset.UtcNow,

            summary = new
            {
                count = users.Count,
                active = users.Values.Count(x => x.Active),
                inactive = users.Values.Count(x => !x.Active)
            },

            users = users.Values.Select(user => new
            {
                id = user.Id,
                first_name = user.FirstName,
                last_name = user.LastName,
                email = user.Email,

                profile = new
                {
                    active = user.Active,

                    location = new
                    {
                        user.Address.Street,
                        user.Address.City,
                        user.Address.Country
                    }
                }
            })
        }
    });
});

//
// ============================================================================
// ECHO / HEADERS
// ============================================================================

// -----------------------------------------------------------------------------
// POST /api/echo
//
// Returns headers + body + query parameters.
// Very useful for testing tinyprobe.
// -----------------------------------------------------------------------------

app.MapPost("/api/echo", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);

    var body = await reader.ReadToEndAsync();

    var headers = request.Headers
        .ToDictionary(
            x => x.Key,
            x => x.Value.ToString());

    var query = request.Query
        .ToDictionary(
            x => x.Key,
            x => x.Value.ToString());

    return Results.Ok(new
    {
        method = request.Method,
        path = request.Path.ToString(),
        headers,
        query,
        body
    });
});

// -----------------------------------------------------------------------------
// POST /api/form
//
// An application/x-www-form-urlencoded body: the raw body and the decoded
// fields, in the order they were sent.
// -----------------------------------------------------------------------------

app.MapPost("/api/form", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    var raw = await reader.ReadToEndAsync();

    var fields = raw.Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(pair => pair.Split('=', 2))
        .Select(pair => new
        {
            name = Uri.UnescapeDataString(pair[0].Replace('+', ' ')),
            value = pair.Length > 1 ? Uri.UnescapeDataString(pair[1].Replace('+', ' ')) : ""
        })
        .ToList();

    return Results.Ok(new { content_type = request.ContentType, raw, fields });
});

// -----------------------------------------------------------------------------
// POST /api/files
//
// A multipart/form-data body: the text fields and the files, in the order
// they were sent. Files are described (name, size, SHA-256), not returned.
// -----------------------------------------------------------------------------

app.MapPost("/api/files", async (HttpRequest request) =>
{
    var boundary = Microsoft.Net.Http.Headers.MediaTypeHeaderValue.Parse(request.ContentType ?? "").Boundary.ToString().Trim('"');
    if (string.IsNullOrEmpty(boundary))
        return Results.BadRequest(new { error = "Expected a multipart/form-data body" });

    var fields = new List<object>();
    var files = new List<object>();
    var reader = new Microsoft.AspNetCore.WebUtilities.MultipartReader(boundary, request.Body);
    while (await reader.ReadNextSectionAsync() is { } section)
    {
        var disposition = Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.Parse(section.ContentDisposition);
        var name = disposition.Name.ToString().Trim('"');
        using var buffer = new MemoryStream();
        await section.Body.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        if (disposition.FileName.HasValue)
        {
            files.Add(new
            {
                name,
                filename = disposition.FileName.ToString().Trim('"'),
                content_type = section.ContentType,
                size = bytes.Length,
                sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))
            });
        }
        else
        {
            fields.Add(new { name, value = System.Text.Encoding.UTF8.GetString(bytes), content_type = section.ContentType });
        }
    }

    return Results.Ok(new { content_type = request.ContentType, fields, files });
});

//
// ============================================================================
// STATUS CODE TESTING
// ============================================================================

// -----------------------------------------------------------------------------
// GET /api/status/{code}
//
// Example:
//
// GET /api/status/200
// GET /api/status/400
// GET /api/status/401
// GET /api/status/404
// GET /api/status/500
// -----------------------------------------------------------------------------

app.MapGet("/api/status/{code:int}", (int code) =>
{
    if (code is < 100 or > 599)
    {
        return Results.BadRequest(new
        {
            error = "invalid_status_code"
        });
    }

    return Results.Json(
        new
        {
            status = code,
            message = $"This is a simulated HTTP {code} response."
        },
        statusCode: code);
});

//
// ============================================================================
// DELAY / TIMEOUT TESTING
// ============================================================================

// -----------------------------------------------------------------------------
// GET /api/delay/{milliseconds}
//
// Example:
//
// GET /api/delay/500
// GET /api/delay/5000
// -----------------------------------------------------------------------------

app.MapGet("/api/delay/{milliseconds:int}", async (int milliseconds) =>
{
    milliseconds = Math.Clamp(milliseconds, 0, 30000);

    await Task.Delay(milliseconds);

    return Results.Ok(new
    {
        delayed = true,
        milliseconds
    });
});

//
// ============================================================================
// CUSTOM HEADERS
// ============================================================================

// -----------------------------------------------------------------------------
// GET /api/headers
//
// Returns custom response headers.
// -----------------------------------------------------------------------------

app.MapGet("/api/headers", (HttpResponse response) =>
{
    response.Headers["X-Tinyprobe-Test"] = "true";
    response.Headers["X-Test-Request-Id"] = Guid.NewGuid().ToString();
    response.Headers["X-Custom-Header"] = "hello-world";

    return Results.Ok(new
    {
        message = "Response contains custom headers."
    });
});

//
// ============================================================================
// COOKIE SESSION
// ============================================================================
//
// A browser-style session: the login sets an HttpOnly "sid" cookie and an
// "XSRF-TOKEN" cookie; writes must send the token back in X-XSRF-TOKEN.
//

const string SessionCookie = "sid";
const string CsrfCookie = "XSRF-TOKEN";

var sessions = new ConcurrentDictionary<string, CookieSession>();

IResult StartSession(HttpResponse response, string username)
{
    var session = new CookieSession(username, Guid.NewGuid().ToString("N"));
    var id = Guid.NewGuid().ToString("N");
    sessions[id] = session;
    response.Cookies.Append(SessionCookie, id, new CookieOptions { Path = "/api", HttpOnly = true, SameSite = SameSiteMode.Lax });
    response.Cookies.Append(CsrfCookie, session.CsrfToken, new CookieOptions { Path = "/api", SameSite = SameSiteMode.Lax });
    return Results.Ok(new { username });
}

CookieSession? CurrentSession(HttpRequest request) =>
    request.Cookies.TryGetValue(SessionCookie, out var id) && id is not null && sessions.TryGetValue(id, out var session)
        ? session
        : null;

IResult NotLoggedIn() =>
    Results.Json(new { error = "not_logged_in", message = "Log in first: POST /api/session." }, statusCode: StatusCodes.Status401Unauthorized);

// -----------------------------------------------------------------------------
// POST /api/session
// Logs in (admin / password) and sets the session cookies.
// -----------------------------------------------------------------------------

app.MapPost("/api/session", (LoginRequest request, HttpResponse response) =>
    request.Username == "admin" && request.Password == "password"
        ? StartSession(response, request.Username)
        : Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized));

// -----------------------------------------------------------------------------
// POST /api/session/form
// An HTML-form login: sets the cookies and redirects (303) to /api/session/me.
// -----------------------------------------------------------------------------

app.MapPost("/api/session/form", async (HttpRequest request, HttpResponse response) =>
{
    var form = await request.ReadFormAsync();
    if (form["username"] != "admin" || form["password"] != "password")
        return Results.Json(new { error = "invalid_credentials" }, statusCode: StatusCodes.Status401Unauthorized);

    StartSession(response, "admin");
    response.Headers.Location = "/api/session/me";
    return Results.StatusCode(StatusCodes.Status303SeeOther);
});

// -----------------------------------------------------------------------------
// GET /api/session/me
// The logged-in user; 401 without a valid session cookie.
// -----------------------------------------------------------------------------

app.MapGet("/api/session/me", (HttpRequest request) =>
    CurrentSession(request) is { } session
        ? Results.Ok(new { username = session.Username })
        : NotLoggedIn());

// -----------------------------------------------------------------------------
// POST /api/session/notes
// A write: needs the session cookie and X-XSRF-TOKEN equal to its CSRF token.
// -----------------------------------------------------------------------------

app.MapPost("/api/session/notes", (NoteRequest note, HttpRequest request) =>
{
    if (CurrentSession(request) is not { } session)
        return NotLoggedIn();
    if (request.Headers["X-XSRF-TOKEN"] != session.CsrfToken)
        return Results.Json(new { error = "csrf", message = "X-XSRF-TOKEN must be the value of the XSRF-TOKEN cookie." }, statusCode: StatusCodes.Status403Forbidden);

    return Results.Json(new { text = note.Text, author = session.Username }, statusCode: StatusCodes.Status201Created);
});

// -----------------------------------------------------------------------------
// DELETE /api/session
// Logs out: ends the session and removes both cookies.
// -----------------------------------------------------------------------------

app.MapDelete("/api/session", (HttpRequest request, HttpResponse response) =>
{
    if (request.Cookies.TryGetValue(SessionCookie, out var id) && id is not null)
        sessions.TryRemove(id, out _);
    response.Cookies.Delete(SessionCookie, new CookieOptions { Path = "/api" });
    response.Cookies.Delete(CsrfCookie, new CookieOptions { Path = "/api" });
    return Results.NoContent();
});

//
// ============================================================================
// RUN
// ============================================================================

app.Run();


//
// ============================================================================
// MODELS
// ============================================================================

record LoginRequest(
    string Username,
    string Password);

record NoteRequest(string Text);

record CookieSession(string Username, string CsrfToken);

record CreateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    Address Address);

record UpdateUserRequest(
    string FirstName,
    string LastName,
    string Email,
    bool Active,
    Address Address);

record PatchUserRequest(
    string? FirstName = null,
    string? LastName = null,
    string? Email = null,
    bool? Active = null,
    Address? Address = null);

public sealed record Address(
    string Street,
    string City,
    string Country);

record User(
    int Id,
    string FirstName,
    string LastName,
    string Email,
    bool Active,
    Address Address);