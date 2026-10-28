# 0010 — Basic auth and OAuth2 client credentials

- **Status:** Implemented
- **Roadmap:** M4-3
- **Repositories:** tinyprobe
- **PRs:** [#26](https://github.com/kikutano/tinyprobe/pull/26)

## Problem

An `auth` set is a set of headers. That covers bearer tokens and API keys, but two common schemes are awkward.

- **Basic auth.** `Authorization: Basic <base64 of user:password>`. Today the guide tells agents to put the base64 text in `env.local.yaml` as `basicAuth`. The user cannot read or change the password without decoding it, an agent must compute base64 by hand, and a workflow cannot try a wrong password with `with`. Postman, Insomnia, Bruno, OpenAPI (`scheme: basic`) and curl (`-u user:password`) all write the user name and the password.
- **OAuth2 client credentials** (service-to-service APIs, Auth0, Keycloak, Okta, Azure AD). Before calling the API, a client sends its id and secret to a token URL and gets an access token. Since 0.17.0 this can be written as a `form` endpoint, a first step with `save: { token: $.access_token }`, and a bearer set. That works in workflows, but:
  - every workflow repeats the token step;
  - `tinyprobe call` and ▶ Call in the extension cannot call a protected endpoint without a token passed by hand (`--with token=…`), which the user must get first and paste again when it expires;
  - Postman users set this up once, on the collection (the OAuth 2.0 helper), and expect every request to be authenticated.

## Proposal

Two keys inside an auth set, next to its headers: `basic` and `oauth2`.

```yaml
# tinyprobe/api/backend.yaml
base_url: ${baseUrl}

auth:
  admin:
    basic:                                 # Authorization: Basic base64(username:password)
      username: ${username}
      password: ${password}

  service:
    oauth2:                                # client credentials: a token, then Authorization: Bearer <token>
      token_url: ${authUrl}/oauth/token
      client_id: ${clientId}
      client_secret: ${clientSecret}
      scope: [orders:read, orders:write]   # optional; a list is joined with spaces
    X-Tenant: ${tenant}                    # other keys are headers, as today

endpoints:
  admin_stats:
    path: /admin/stats
    auth: admin
    params: { username: admin, password: }

  list_orders:
    path: /orders
    auth: service
```

```yaml
steps:
  - name: Wrong password
    call: admin_stats
    with: { password: wrong }
    expect: { status: 401 }

  - name: Orders, with a token from the token URL
    call: list_orders
    expect: { status: 200 }
```

```text
$ tinyprobe call tinyprobe/api/backend.yaml list_orders -e tinyprobe/env.local.yaml
  ✓ list_orders    GET    200    41.20ms  /orders
```

### `basic`

- `username` and `password`, both required. They may contain placeholders.
- tinyprobe sends `Authorization: Basic <base64 of username:password>`, encoded as UTF-8 (RFC 7617).
- A username with `:` fails the step: `Basic auth: the username cannot contain ':'`.

### `oauth2`

- **Client credentials only** (`grant_type=client_credentials`, RFC 6749 section 4.4).
- `token_url`, `client_id` and `client_secret` are required.
- `scope` is optional: a value, or a list joined with spaces.
- `client_auth` says how the client id and secret are sent:
  - `basic` (the default): an `Authorization: Basic` header on the token request, as RFC 6749 asks servers to support and as Postman does by default;
  - `form`: `client_id` and `client_secret` fields in the form body (Postman's "Send client credentials in body").
- `form` adds other fields to the token request, such as Auth0's `audience` or Azure AD's `resource`: `form: { audience: https://api.example.com }`.
- Every value may contain placeholders.
- The token request is a `POST` with an `application/x-www-form-urlencoded` body. The response must be JSON with `access_token`. The API request then gets `Authorization: Bearer <access_token>`, whatever the response's `token_type` says.

**When the token is requested.**

- On the first request that uses the set, before sending it.
- The token is then reused for the rest of the run, by every endpoint whose set resolves to the same token URL, client, secret, scope and form.
- After its `expires_in`, minus 30 seconds, the next request gets a new token. A response without `expires_in` keeps its token for the whole run.
- `retries` and `until` reuse the token.
- Like the cookie jar, the token belongs to one workflow run. Each workflow of a suite gets its own token; `tinyprobe call` gets one on every call; `tinyprobe_run` and `tinyprobe_call` under MCP follow the same rules.

**When it fails.** The step fails as a request error, with no response from the API, and the message has the token server's answer:

```text
Cannot get an OAuth2 token from https://auth.example.com/oauth/token: status 401, {"error":"invalid_client"}
Cannot get an OAuth2 token from https://auth.example.com/oauth/token: the response has no access_token
```

A network error or a timeout of the token request is retried like the API request (`max_retries`).

To test the authorization server itself (a wrong secret answers `401`), call the token URL as a normal `form` endpoint, as since 0.17.0.

### Both

- An endpoint can use one set with `basic` or `oauth2`, plus any number of header sets (`auth: [service, tenant]`).
- They set `Authorization`, so an `Authorization` header elsewhere is an error: in the same set, in another set of the endpoint, or in the endpoint's own `headers`. One in the file's `headers` is not sent to that endpoint, as `Content-Type` with `form`.
- Placeholders in `basic` and `oauth2` are endpoint arguments, like the ones in headers. `--with password=…` and a step's `with` reach them, and a typo in `with` is caught.
- With `-v`, the log shows `Requesting an OAuth2 token from <url>`, and later `Using the OAuth2 token from <url>` for each reuse.
- The recorded request (ndjson, the extension's response view) shows the `Authorization` header as sent, as it does today for bearer sets. The token request itself is not recorded as a step.

## Validation

```text
api.yaml:5:5: auth 'admin': basic needs username and password
api.yaml:7:7: unknown key 'usrname' in basic (did you mean 'username'?)
api.yaml:11:5: auth 'service': oauth2 needs token_url, client_id and client_secret
api.yaml:13:7: unknown key 'token_uri' in oauth2 (did you mean 'token_url'?)
api.yaml:16:20: client_auth must be basic or form, not 'header'
api.yaml:4:5: auth 'admin' has basic and an Authorization header: use one of them
api.yaml:22:11: endpoint 'list_orders' uses auth 'admin' and 'service', which both set Authorization: use one of them
api.yaml:25:7: endpoint 'list_orders' sets Authorization, but its auth 'service' sets its own: remove the header
```

## Protocol

New keys only, protocol `1`.

- `inspect --json`, each endpoint: `"basic_auth"` and `"oauth2"`, `null` when the endpoint's sets have none. Values are the literal YAML, placeholders included:

  ```json
  "basic_auth": { "username": "${username}", "password": "${password}" },
  "oauth2": {
    "token_url": "${authUrl}/oauth/token", "client_id": "${clientId}", "client_secret": "${clientSecret}",
    "scope": "orders:read orders:write", "client_auth": "basic", "form": {}
  }
  ```

- `headers` stays the effective headers written in the files, so it has no `Authorization` header for these endpoints.
- `arguments` includes the placeholders of `basic` and `oauth2`.
- ndjson `request.headers`: the `Authorization` header as sent.

## Extension

Nothing required. Completion and hover read `basic`, `oauth2` and their keys from `tinyprobe schema`. ▶ Call on an endpoint with `oauth2` now works with no token passed by hand. No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: "Auth sets" with headers, `basic` and `oauth2`, and the rules above.
- `docs/llm-guide.md`:
  - Postman `basic` auth becomes `basic`;
  - Postman `oauth2` with `grant_type: client_credentials` becomes `oauth2`: `accessTokenUrl`, `clientId`, `clientSecret`, `scope`, and `client_authentication` (`header` is `basic`, `body` is `form`);
  - OpenAPI `http`/`basic` and `oauth2` `clientCredentials` (`tokenUrl`, `scopes`), Insomnia, Bruno and curl `-u` map the same way;
  - other grant types are still reported;
  - common mistakes: the base64 of the credentials in an env file, a token step repeated in every workflow, and an `Authorization` header next to `basic` or `oauth2`.
- `README.md`: the API file example.
- `docs/protocol.md`: `basic_auth` and `oauth2`.
- JSON Schema: the `basic` and `oauth2` keys of an auth set, with descriptions.
- `docs/example`:
  - test-server routes: `POST /oauth/token`, client credentials, accepting both client authentications, with `expires_in`; `GET /api/auth/basic` (admin / password); `GET /api/auth/oauth`, which needs a token from `/oauth/token`;
  - steps in `auth.yaml`: basic with the right and a wrong password, and an endpoint with `oauth2`, once per `client_auth`;
  - the example environment gets the test client's id and secret.
- [Decision 0003](../decisions/0003-endpoints-declare-workflows-run.md): a note that an auth set may obtain its credential (see open question 1).

## Out of scope

- Other OAuth2 grant types: authorization code (needs a browser), password (removed in OAuth 2.1), device code. Refresh tokens. The guide keeps telling agents to ask for a token in `env.local.yaml`.
- `private_key_jwt` and mTLS client authentication.
- Digest, NTLM, AWS Signature v4, Hawk. Each would be another key of an auth set, with its own task.
- Basic or OAuth2 on query parameters (`?access_token=`).
- Showing the token request in the report as a step of its own.

## Acceptance criteria

- [x] `basic` and `oauth2` in auth sets as above, with every validation error at its location.
- [x] Unit tests with `FakeServer`:
  - Basic: the header, UTF-8, a username with `:`;
  - OAuth2: both `client_auth`, `scope` as a value and a list, extra `form` fields;
  - the token reused within a run and requested again after `expires_in`;
  - each run gets its own token;
  - token errors (status, no `access_token`, network error with retries);
  - placeholders from `with`, and the recorded `Authorization` header.
- [x] `inspect --json` has `basic_auth`, `oauth2` and their arguments. The schema test passes.
- [x] `docs/example/workflows/auth.yaml` covers Basic and OAuth2 against the test server, in CI and in `scripts/dev.sh e2e`.
- [x] `tinyprobe call` of an endpoint with `oauth2` works with only the env file.
- [x] `scripts/dev.sh check` passes.

## Open questions

1. **OAuth2 in the API file, or only in workflows.** Decision 0003 says an API file "has nothing that runs". An `oauth2` set does run a request, the token request, before the request it describes. It is still declarative: it says how a request is authenticated, holds no value from earlier steps, and its token lives only for one run, like the cookie jar. The alternative is to add nothing: document the token step with `save`, and leave `call` and ▶ Call needing a token by hand. *Recommendation: `oauth2` in auth sets, with a note in decision 0003: "an auth set may obtain its credential (OAuth2 client credentials); the credential lives for one run".*
2. **The syntax.** The proposal puts `basic` and `oauth2` as keys of an auth set, next to its headers: one place for auth, and a set can still add headers (`X-Tenant`). `basic` and `oauth2` are not real header names, so nothing that works today changes. The alternative is a separate section (`auth_schemes:`), with a second name to learn. *Recommendation: keys of an auth set.*
3. **The default `client_auth`.** `basic` follows RFC 6749 and Postman's default. `form` is what some tutorials show, and most servers accept both. *Recommendation: `basic`.*
4. **Token lifetime.** The proposal reuses the token for the whole run until `expires_in`. The alternative is a token for every request: simpler, but slower, and some servers rate-limit token requests. *Recommendation: reuse until `expires_in`.*

## Decisions

Approved on 2026-10-28 with the recommendations:

1. `oauth2` goes in auth sets, and decision 0003 gets a note: an auth set may obtain its credential, which lives for one run.
2. `basic` and `oauth2` are keys of an auth set, next to its headers.
3. `client_auth` defaults to `basic`.
4. A token is reused for the run until `expires_in`, minus 30 seconds.
