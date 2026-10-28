# 0009 — Cookies

- **Status:** Approved
- **Roadmap:** M4-2
- **Repositories:** tinyprobe
- **PRs:** —

## Problem

Many APIs keep the session in a cookie: a login answers with `Set-Cookie: sid=…`, and every later request must send it. Many also use a CSRF cookie that must be copied into a header (`XSRF-TOKEN` → `X-XSRF-TOKEN`: Angular, Laravel, Django, Spring Security).

Today `tinyprobe run` already keeps cookies, by accident. Each workflow gets its own HTTP client, and .NET's default handler stores cookies. It was checked on 0.17.0:

- a cookie set by one step is sent by the next steps, merged with a `Cookie` header written in the API file;
- a cookie set by a redirect is kept.

Nobody knows this, because it is not documented, not tested and not visible:

- The recorded request (ndjson `request.headers`, the extension's response view) does not show the `Cookie` header that was sent, because the handler adds it after tinyprobe records the request. Someone debugging a 401 cannot see whether the session cookie went out.
- A cookie cannot be checked. A `Set-Cookie` header check works only on the text of the header, with the attributes and every cookie joined together.
- A cookie cannot be saved, so the CSRF pattern cannot be written.
- The guide tells agents that cookies are not supported, so Postman `pm.cookies`, curl `-b`/`-c` and OpenAPI `in: cookie` are skipped and listed in `MIGRATION.md`.

## Proposal

```yaml
# tinyprobe/api/shop.yaml
base_url: ${baseUrl}
endpoints:
  login:
    method: POST
    path: /session
    params: { email:, password: }
    body: { email: ${email}, password: ${password} }
  me:
    path: /session/me
  add_note:
    method: POST
    path: /notes
    params: { csrf:, text: }
    headers:
      X-XSRF-TOKEN: ${csrf}
    body: { text: ${text} }
  logout:
    method: DELETE
    path: /session
```

```yaml
# tinyprobe/workflows/session.yaml
import: ../api/shop.yaml
steps:
  - name: Log in
    call: login
    with: { email: ada@example.com, password: ${password} }
    expect:
      cookies:
        sid: { exists: true }
    save:
      csrf: { cookie: XSRF-TOKEN }

  - name: The session cookie is sent
    call: me
    expect:
      body:
        $.email: ada@example.com

  - call: add_note
    with: { csrf: ${csrf}, text: Hello }
    expect: { status: 201 }

  - call: logout
    expect:
      cookies:
        sid: { exists: false }

  - name: Logged out
    call: me
    expect: { status: 401 }
```

### The cookie jar

- **Every workflow run has its own cookie jar, empty at the start.** A `Set-Cookie` of any response is stored, redirects included. Every later request of the same workflow sends the cookies that match its URL: domain, path, `Secure`, expiry. A cookie with `Max-Age=0` or an expiry in the past is removed.
- Each workflow of a suite (`tinyprobe run tinyprobe/workflows`) starts from an empty jar, so workflows do not depend on each other ([decision 0003](../decisions/0003-endpoints-declare-workflows-run.md)). `retries` and `until` repeat a request with the jar as it is at that moment.
- **`tinyprobe call` keeps no cookies between calls** (decision 0003). Within one call, a cookie set by a redirect is sent to the redirect's target.
- A `Cookie` header written in the API file (file `headers`, an auth set, the endpoint's `headers`) is sent as written, followed by the jar's cookies (`Cookie: manual=1; sid=abc`). This is what happens today.
- MCP: `tinyprobe_run` follows the same rule (one jar per workflow), and `tinyprobe_call` keeps nothing.

### Seeing what was sent

The recorded request (`request.headers` in ndjson, the extension's response view, `-v`) includes the `Cookie` header as it was sent, the jar's cookies included. The recorded response already shows its `Set-Cookie` headers.

### `expect.cookies`

```yaml
expect:
  cookies:
    sid: { exists: true }
    theme: dark                       # the value, as text
    cart: { contains: "item-42" }
```

- `cookies` is a mapping of cookie names to conditions, with the same operators as `headers`. Values are text.
- **A name refers to a cookie in the jar after the response, among those that would be sent to the request's URL.** `exists: true` passes after a login that set `sid`. `exists: false` passes after a logout that removed it, and also when the cookie was never set. This matches Postman's `pm.cookies`, which reads the jar for the request's domain.
- Names are case-sensitive, as in HTTP.
- `until` accepts `cookies` too: `until: { cookies: { report_ready: { exists: true } } }`.

### `save` from a cookie

```yaml
save:
  csrf: { cookie: XSRF-TOKEN }        # the value of a cookie in the jar
  orderId: $.id                       # a JSONPath of the body, as today
```

- A `save` value is a JSONPath, as today, or `{ cookie: name }`. The cookie is read like an `expect.cookies` name: from the jar after the response, for the request's URL.
- A cookie that is not in the jar fails the step, like a JSONPath that matches nothing:
  `Cannot save 'csrf': no cookie 'XSRF-TOKEN' after this response`.
- The saved value is the cookie's value, with no attributes.

## Validation

At load time:

```text
session.yaml:9:9: unknown key 'cookie' in expect (did you mean 'cookies'?)
session.yaml:12:13: save 'csrf' must be a JSONPath ($.token) or { cookie: name }
session.yaml:12:15: unknown key 'cooky' in save 'csrf' (did you mean 'cookie'?)
```

The second message replaces today's `save 'csrf' must be a single value`. Cookie conditions are parsed like header conditions, with the same errors (`cannot mix operators … with object fields`).

When the step runs: the `save` error above, reported as a failed step.

## Protocol

New values only, protocol `1`.

- `inspect --json`, in `expect` and `until`: a cookie check has the subject `cookie <name>`, as a header check has `header <name>`. For example: `{ "subject": "cookie sid", "condition": "exists" }`.
- `inspect --json`, `save`: a value read from a cookie is the string `cookie <name>`, so `save` stays an object of strings, for example `"csrf": "cookie XSRF-TOKEN"`.
- ndjson `assertions`: subject `cookie sid`, `actual` is the cookie's value, or `(missing)` as for a missing header.
- ndjson `request.headers`: also contains the `Cookie` header added from the jar. The key keeps its meaning (the headers as sent), and is now complete.

## Extension

Nothing required. The response view shows the request's `Cookie` header once the CLI records it. Completion reads `cookies` and `cookie` from `tinyprobe schema`. No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: a "Cookies" section (the jar, `expect.cookies`, `save` from a cookie), plus the rows in the step and expect tables.
- `docs/llm-guide.md`:
  - remove "cookies are not supported yet";
  - Postman `pm.cookies.has/get` becomes `expect.cookies`, and a cookie copied into an environment variable becomes `save: { x: { cookie: … } }`;
  - a session kept by cookies (HAR, Postman) needs nothing: the jar sends them;
  - curl `-c`/`-b file` needs nothing, and `-b 'name=value'` becomes a `Cookie` header with a placeholder;
  - OpenAPI `in: cookie` becomes a `Cookie: "name=${name}"` header with a param;
  - common mistakes: saving `Set-Cookie` with a header check and pasting it into a `Cookie` header, and writing a session cookie's value into a file.
- `README.md`: the format table.
- `docs/protocol.md`: the `cookie <name>` subject, `save` from a cookie, and the `Cookie` request header.
- JSON Schema: `expect.cookies`, `until.cookies`, the `{ cookie: }` form of `save`, with descriptions.
- `docs/example`:
  - test-server routes for a cookie session: `POST /api/session` sets an `HttpOnly` `sid` and an `XSRF-TOKEN` cookie; `GET /api/session/me` needs `sid`; `POST /api/session/notes` needs `X-XSRF-TOKEN` equal to the cookie; `DELETE /api/session` removes both;
  - a workflow `session.yaml` that covers each of them.

## Out of scope

- Sharing cookies between workflows or between calls (a session file): ruled out by decision 0003.
- Turning the jar off (`settings: { cookies: false }`). A workflow that needs a request without cookies calls it before logging in. We add the setting if someone asks for it.
- Checking cookie attributes (`HttpOnly`, `Secure`, `SameSite`, expiry). They need a mapping per cookie and are more about security scanning than API tests. A `Set-Cookie` header check covers them meanwhile: `Set-Cookie: { contains: HttpOnly }`.
- Saving response headers (`save: { location: { header: Location } }`). The `{ cookie: }` form leaves room for it, and it gets its own task.
- Setting cookies in the jar by hand. A `Cookie` header covers it.

## Acceptance criteria

- [ ] One jar per workflow run, kept across redirects. `call` keeps nothing between calls.
- [ ] The recorded request has the `Cookie` header as sent.
- [ ] `expect.cookies`, `until.cookies` and `save: { x: { cookie: … } }` work as above, and every error above is reported at its location.
- [ ] Unit tests with `FakeServer`: the jar (path, expiry, removal, one workflow does not see another's cookies), checks, save, a missing cookie, the recorded header.
- [ ] `docs/example/workflows/session.yaml` passes against the test server, in CI and in `scripts/dev.sh e2e`.
- [ ] `inspect --json` has the `cookie <name>` subjects and save values. The schema test passes.
- [ ] `scripts/dev.sh check` passes.

## Open questions

1. **What a cookie check reads.** Option A is the jar after the response, for the request's URL, as proposed: `exists: false` works after a logout, and it matches Postman's `pm.cookies`. Option B is only the cookies set by this response's `Set-Cookie`: "set by this response" is explicit, but a logout check has to read the expired `Set-Cookie`, and a step cannot check a cookie set earlier. *Recommendation: the jar (A).*
2. **The `save` syntax.** The proposal is `csrf: { cookie: XSRF-TOKEN }`, a mapping that can later take `{ header: Location }`. The alternative is a string such as `cookie XSRF-TOKEN` next to `$.id`: shorter, but a string whose meaning depends on its first word is easy for agents to get wrong. *Recommendation: the mapping.*
3. **`save` from a cookie in this task.** It is needed only for the CSRF pattern, which is common in browser-facing APIs (Laravel, Django, Spring, Angular). Without it, M4-2 is documentation, the recorded header and checks. *Recommendation: include it.*

## Decisions

Approved on 2026-10-28 with the recommendations:

1. A cookie check reads the jar after the response, for the request's URL.
2. `save` from a cookie is written `{ cookie: name }`.
3. `save` from a cookie is part of this task.
