# 0015 — A `matches` operator

- **Status:** Implemented
- **Roadmap:** M4-15
- **Repositories:** tinyprobe, tinyprobe-vscode (completion only, from the schema)
- **PRs:** #33

## Problem

Some values cannot be checked by equality: generated ids (`ORD-20261029-0042`), tokens, dates, emails, a `Location` header with an id in it, a session cookie. Today the closest check is `contains` on a fixed part of the text, or `exists: true`, which says nothing about the shape.

Postman users write these checks with regular expressions (`pm.expect(id).to.match(/^ORD-\d+$/)`), and so do Insomnia and Bruno users (`matches`). The migration guide tells agents to replace them with `contains`, or drop them and report it (`docs/llm-guide.md`, section 12.3). A regex check is the most common thing they lose in a migration.

## Proposal

One more operator, written like the others:

```yaml
steps:
  - call: create_order
    expect:
      status: 201
      headers:
        Location: { matches: '^/api/orders/[0-9]+$' }
      cookies:
        sid: { matches: '^[0-9a-f]{32}$' }
      body:
        $.id: { matches: '^ORD-[0-9]{8}-[0-9]{4}$' }
        $.email: { matches: '(?i)@example\.com$' }      # (?i): ignore case
        $.createdAt: { matches: '^\d{4}-\d{2}-\d{2}T', exists: true }
```

```text
  ✗ Create an order   POST   201   12.4ms  /api/orders
      $.id matches "^ORD-[0-9]{8}-[0-9]{4}$", actual "ORD-42"
```

Rules:

- **`matches` searches the value**, like `contains` and like JavaScript's `.match()`: `'@example\.com$'` passes for `ada@example.com`. Anchor with `^` and `$` to match the whole value.
- **The value must be text.** A string is matched as it is. Numbers and booleans are matched as their JSON text (`42`, `true`), so `$.id: { matches: '^[0-9]+$' }` works on a number. `null`, objects and arrays do not match: the check fails and shows the actual value. A missing value fails, like every operator except `exists: false`.
- **The syntax is .NET's** (`System.Text.RegularExpressions`), which for usual patterns is the same as JavaScript's: character classes, `\d \w \s`, anchors, groups, alternation `a|b`, quantifiers `{2,4}`, lookahead. Flags are written inline at the start: `(?i)` ignores case, `(?m)` makes `^ $` match at each line, `(?s)` makes `.` match newlines. JavaScript's `/.../i` is written `(?i)...`. One difference to know: `\d` also matches digits of other scripts (Arabic-Indic, ...); `[0-9]` matches only 0 to 9.
- **Write the pattern in single quotes.** In single-quoted YAML a backslash is a plain character, so `'\d+'` is the pattern `\d+`. In double quotes YAML reads the backslashes first (`"\\d+"`); unquoted, a `#` after a space or a `: ` ends the value. The docs and the guide always use single quotes.
- **Placeholders are replaced before the pattern is used**, as in every value: `{ matches: '^${orderPrefix}-[0-9]+$' }`. The variable's text is inserted as it is, not escaped: a `.` in it matches any character.
- **It works wherever the other operators do:** `expect` and `until`, on `body`, `headers` and `cookies`, combined with other operators in one mapping (all must hold), and inside `any` (`{ any: [ { matches: '^A' }, null ] }`). Not on `duration`, and not on `status`.
- **Each check is limited to 1 second.** A pattern that takes longer on a value (catastrophic backtracking, such as `(a+)+$` on a long text) fails the step with this reason instead of hanging the run, as an undefined variable does, and the run goes on to the report:

```text
  ✗ Read the note   GET   200   1.02s  /api/notes/1
      $.description matches "^(a+)+$": the regular expression took more than 1s on this value
```

## Validation

A pattern without placeholders is compiled when the file is loaded, so a broken one is reported before any request, with .NET's reason:

```text
workflows/orders.yaml:9:21: invalid regular expression '^ORD-[0-9': Unterminated [] set
workflows/orders.yaml:12:21: matches needs a regular expression as text, e.g. { matches: '^ORD-[0-9]+$' }
```

The second error is for a list or a mapping as the value. A pattern with placeholders is compiled at run time; if it is invalid then, the step fails with the same reason (`$.id matches "^ORD[": invalid regular expression '^ORD[': Unterminated [] set`).

## Protocol

No new keys. The condition is text in `inspect --json` (`checks[].condition`) and in the `assertions` of `step_completed`: `matches "^ORD-[0-9]+$"`, with the pattern as a JSON string, like the other operators show their value.

Strings in `condition` and `actual` are now written as people read them: `"^(a+)+$"`, not `"^(a\u002B)\u002B$"` (the default JSON escaping of `+ < > & '`). The same JSON value, more readable, for every operator.

`tinyprobe schema` gets `matches` in `$defs/condition`, with its description.

## Extension

Nothing to change: completion and hover of keys come from `tinyprobe schema`, so `matches` is offered after a release. No new `MINIMUM_VERSION`.

## Docs

- `docs/workflow-format.md`: `matches` in the operator table, the rules above (search, single quotes, inline flags, text only, 1 s), and an example.
- `docs/llm-guide.md`: the operator in the checks section; in the Postman, Insomnia and Bruno mappings, `.to.match(/re/flags)` and `matches` become `{ matches: '(?flags)re' }` instead of `contains` or a dropped check. Common mistakes: double quotes with backslashes, JavaScript's `/.../` delimiters kept in the pattern, and expecting a whole-value match without `^ $`.
- `README.md`: `matches` in the `expect.body` row of the step table.
- `docs/protocol.md`: `matches` among the conditions of the schema.
- `docs/example`: a `matches` on a body value (`users.yaml`, the email), a header (`http.yaml`, `X-Test-Request-Id`) and a cookie (`session.yaml`, `sid`), so CI exercises the three.

## Out of scope

- A negated form (`not_matches`). `any` and the other operators cover most cases; it can be added later as an operator of its own if people ask.
- Saving a part of a value with a capture group (`save: { orderNumber: { from: $.id, regex: '([0-9]+)$' } }`). It belongs with checks on bodies that are not JSON (M7-4), where a regex is the only way to read a value.
- `matches` on `status` or `duration`.

## Acceptance criteria

- [x] `matches` in `expect` and `until`, on body, headers and cookies, alone, combined with other operators and inside `any`.
- [x] Search semantics; numbers and booleans as text; `null`, objects, arrays and missing values fail with the actual value shown.
- [x] Invalid patterns without placeholders are load errors with location and .NET's reason; a list or mapping as the value is an error; patterns with placeholders are compiled at run time.
- [x] A 1 s timeout per evaluation, reported as a failed check.
- [x] Unit tests for the loader (valid and every error), the evaluator (each value kind, flags, timeout) and the report text; `docs/example` exercises the three subjects.
- [x] Native AOT: the size of the binary measured before and after (runtime-built `Regex` pulls in the parser and the interpreter); the budget of `scripts/perf.sh` still holds.
- [x] Docs above, the guide's migration tables updated.
- [x] `scripts/dev.sh check` passes.

## Open questions

1. **Search or whole value.** Option A, search (`'@example\.com$'` passes for `ada@example.com`), as proposed: the same as JavaScript, Postman, Insomnia and Bruno, so migrated patterns keep their meaning. Option B, the pattern must match the whole value: safer for a reader who forgets `^ $`, but every migrated pattern would change meaning. *Recommendation: search (A).*
2. **Numbers and booleans.** Option A, matched as their JSON text, as proposed: `$.id: { matches: '^[0-9]+$' }` works whether the API returns `42` or `"42"`. Option B, strings only (any other type fails): stricter, like Postman's `.to.match`, which fails on a number. *Recommendation: as text (A); `eq` and the typed checks are there for the type.*
3. **Placeholders in the pattern.** Option A, the variable's text is inserted as it is, as proposed: one rule for placeholders everywhere, and a variable can hold a pattern. Option B, inserted escaped (`Regex.Escape`): a value with `.` or `+` is matched literally, but a variable cannot hold a pattern, and the rule differs from every other value. *Recommendation: as it is (A), with a line in the docs.*
4. **The timeout.** 1 second per evaluation, fixed, as proposed. A setting (`settings.regex_timeout`) can come later if a real pattern needs more. *Recommendation: fixed 1 s.*
