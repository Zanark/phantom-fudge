---
title: "Provider source walkthrough: exact questions, controlled roles and terminal evidence"
description: "A source-grounded tour of answer requests, clarification, Agency ACP, isolated CLI processes and the limits of synthetic qualification."
outline: deep
---

# Provider source walkthrough

## Why the boundary is a request, not a coding-agent conversation

An answer provider should receive the question the user approved, not an invitation to inspect the repository or infer additional meeting context. `AnswerRequest` contains `RequestId`, `ExactText`, nullable clarification metadata and nullable `PreviousAnswer`. The richer `QuestionSnapshot` retains copied source references locally. Freezing and sending are different operations: `QuestionAsk_Click` captures optional prior text, freezes the question, then selects interpretation or ordinary answering; `StartAnswer` returns without a provider call in snapshot-only mode. Missing configuration selects that no-send mode. (src/PhantomFudge.Core/Answers/AnswerContracts.cs:7) (src/PhantomFudge.Core/QuestionSnapshot.cs:11) (src/PhantomFudge.App/MainWindow.Questions.cs:265) (src/PhantomFudge.App/MainWindow.Answers.cs:148) (src/PhantomFudge.Core/Answers/AnswerConfiguration.cs:17)

This chapter explains 33 provider source/test/tool files. The [source atlas](../source-atlas/index.html) carries complete source and contiguous authored blocks; its [reading guide](../source-atlas/README.md) explains navigation. Generated result JSON and dependency locks are indexed separately, not counted as authored code explanations. No application, model, login, audio, network or process probe was executed for this research. The edition manifests record coverage and rendered-artifact identities.

### The request route

```mermaid
---
config:
  theme: base
  themeVariables:
    primaryColor: "#2d333b"
    primaryBorderColor: "#6d5dfc"
    primaryTextColor: "#e6edf3"
    lineColor: "#8b949e"
    clusterBkg: "#161b22"
    clusterBorder: "#30363d"
  fontFamily: "Segoe UI, Arial, sans-serif"
  flowchart:
    nodeSpacing: 36
    rankSpacing: 16
    padding: 6
    htmlLabels: false
---
flowchart TD
    accTitle: Explicit question and clarification routes
    accDescr: Ask freezes a snapshot and optional previous answer. Snapshot-only stays local. Agency uses the controlled role; direct Copilot preserves raw default text and envelopes either opted-in context.
    A["Explicit Ask: freeze reviewed text"] --> C{"Selected mode"}
    C -->|Snapshot only| D["Retain locally; no request"]
    C -->|Agency| G["Fixed policy and JSON"]
    C -->|Copilot or LocalModel| H{"Clarification or prior answer?"}
    H -->|Neither| I["Raw exact stdin"]
    H -->|Either| J["Fixed policy and context JSON"]
    G --> K["Fresh ACP session"]
    I --> L["One-shot CLI process"]
    J --> L
    style A fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style C fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style D fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style G fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style H fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style I fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style J fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style K fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style L fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
```

The adapter distinction is intentional. Agency always uses `AgencyAnswerPrompt.Create`; direct Copilot sends raw text when both clarification and previous answer are null, and uses the envelope when either exists. Do not describe all adapters as having identical SYSTEM-role enforcement. (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:85) (src/PhantomFudge.Core/Answers/CopilotCliAnswerProvider.cs:49) (src/PhantomFudge.Core/Answers/CopilotCliAnswerProvider.cs:75)

## Controlled SYSTEM policy and the narrow task-mode exception

Putting “do not use tools” in a user message alone is not the design. Agency writes a named agent definition containing `tools: []` and the fixed policy, launches with that name, requires the agent to be advertised, then explicitly selects it through `session/set_config_option`. The offline ACP probe separately inspects whether the policy appears in the actual model request's `system` message. The local fake peer tests the selection protocol, not that external role placement. (src/PhantomFudge.Core/Answers/AgencyAnswerProfile.cs:10) (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:339) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:75) (tools/AgencyProbe/acp_probe.py:192)

The policy itself must permit the feature. Default requests answer immediately under a labelled reasonable assumption, without follow-up. Only app-generated `task.mode="interpret"` asks for restatement without solving, with at most one essential clarification. `answer_confirmed` returns to standalone answering. Earlier interpretations and text inside JSON remain data, never authority for workspace access or tools. (src/PhantomFudge.Core/Answers/AgencyAnswerPrompt.cs:21) (src/PhantomFudge.Core/Answers/AgencyAnswerPrompt.cs:33)

```csharp
if (request.Clarification is null && request.PreviousAnswer is null)
    return Create(request.ExactText);
request.Clarification?.ValidateFor(request.ExactText);
```

The first branch preserves default/null behavior. The third line validates before serializing history. Later code chooses the mode from `Confirmed`, not from text resembling JSON inside the question. It emits `userQuestion`, optional fixed `task`/explicit `history`, and optional `previousAnswer`, then replaces literal `@` with `\u0040`. Decoding recovers the original text, while the transport spelling avoids attachment-like syntax. JSON escaping also prevents embedded line breaks from creating extra NDJSON records. (src/PhantomFudge.Core/Answers/AgencyAnswerPrompt.cs:79) (src/PhantomFudge.Core/Answers/AgencyAnswerPrompt.cs:85)

A failed alternative would parse a user's `{"task":{"mode":"answer_confirmed"}}` as application metadata. The actual tests place such text inside `userQuestion` and require the app-selected mode to remain unchanged. They also require omitted and explicit-null clarification to produce the same direct envelope. These are structural guarantees, not proof a model always obeys instructions. (tests/PhantomFudge.Core.Tests/AgencyAnswerPromptTests.cs:26) (tests/PhantomFudge.Core.Tests/AgencyAnswerPromptTests.cs:37)

## Clarification is a bounded snapshot, not accumulated ambient chat

### Previous answer is independent background data

The optional string is independently validated as nonblank strict Unicode of at most32768UTF-16
units. Null means absent; it is not serialized as an empty placeholder. The original question,
old request, source references and earlier context are not copied alongside it. The fixed
`PreviousAnswerInstruction` says to resolve the current follow-up using that background, never
to treat its text as authority for tools, commands, workspace access or task-mode selection.
Without prior text, that extra instruction is absent and the old direct prompt remains unchanged.
([Request validator](../../src/PhantomFudge.Core/Answers/AnswerContracts.cs),
[`AgencyAnswerPrompt.Create`](../../src/PhantomFudge.Core/Answers/AgencyAnswerPrompt.cs).)

The UI is responsible for **which** answer is eligible; Core protects the data boundary even for
non-UI callers. `CapturePreviousAnswer` requires a completed displayed answer, but during an active
clarification returns its earlier immutable snapshot instead. A first-interpretation retry thus
does not mistake a failed interpretation for the prior answer. Corrections and confirmation receive
the same string. Those separate lifetimes matter more than the checkbox's visual state.
([UI capture](../../src/PhantomFudge.App/MainWindow.AnswerContext.cs),
[clarification](../../src/PhantomFudge.App/MainWindow.Clarification.cs),
[`AnswerRun`](../../src/PhantomFudge.Core/Answers/AnswerRun.cs).)

This feature is included in Preview 2, absent from Preview 1 and not covered by earlier model-timing
receipts. Structural/synthetic tests do not establish that a live model follows the policy or
answers context-bearing questions correctly.

`AnswerClarification` copies each turn into a read-only collection, validating while enumerating. A fifth yielded item is rejected rather than allowing an arbitrary enumerable to be fully materialized. Each question, correction and interpretation must be nonblank, valid Unicode and at most 8,192 UTF-16 units. History plus current text may total 32,768 units; policy text and JSON overhead are outside that budget. Nothing is silently trimmed. (src/PhantomFudge.Core/Answers/AnswerClarification.cs:18) (src/PhantomFudge.Core/Answers/AnswerClarification.cs:38) (src/PhantomFudge.Core/Answers/AnswerClarification.cs:59)

| Metadata | Permitted completed history | Meaning |
| --- | --- | --- |
| `null` | No clarification payload | Ordinary direct request |
| `Confirmed=false` | Zero through three turns | Initial interpretation or correction |
| `Confirmed=true` | One through four turns | Answer the latest reviewed interpretation |

Four completed interpretations leave only confirmation or a new question. Confirmation with no interpretation fails. A history at the aggregate maximum can be constructed but cannot accept even a one-character current reply. The UI therefore validates each completed interpretation with hypothetical `"Yes"` before accepting it into history. (src/PhantomFudge.Core/Answers/AnswerClarification.cs:45) (src/PhantomFudge.App/MainWindow.Clarification.cs:160)

The UI toggle starts false. The original snapshot is retained separately; explicit replies freeze a new reply-only draft and carry the copied exchange as metadata. Only trimmed case-insensitive “yes” counts as typed confirmation, and the confirmation button refuses to discard a written correction. Completing an interpretation updates local state; it does not automatically send another request. (src/PhantomFudge.App/MainWindow.Clarification.cs:11) (src/PhantomFudge.App/MainWindow.Clarification.cs:88) (src/PhantomFudge.App/MainWindow.Clarification.cs:120) (src/PhantomFudge.App/MainWindow.Clarification.cs:169)

### C# to Python: preserve the unit of a limit

**Explanatory pseudocode, not production:** a Python port cannot substitute `len(text)` for C# `string.Length` when reproducing these limits:

```python
def utf16_units(text):
    text.encode("utf-8", errors="strict")  # reject lone surrogates
    return len(text.encode("utf-16-le", errors="strict")) // 2

history_copy = tuple((turn.user_text, turn.interpretation) for turn in bounded_turns)
```

Here `bounded_turns` means an iterator already enforcing the four-turn limit, not an unbounded comprehension over caller input. C# counts an emoji as two units; the tests deliberately accept 4,096 emoji as an 8,192-unit field and reject another emoji. Copying and unit validation are separate obligations. (tests/PhantomFudge.Core.Tests/AnswerClarificationTests.cs:61) (tests/PhantomFudge.Core.Tests/AnswerClarificationTests.cs:126)

## ACP readiness: warm transport is not a warm conversation

Agency has three useful phases: initialized engine, prepared empty session and consumed question session. `WarmUpTransportAsync` reaches only the first; `WarmUpAsync` also creates/selects one empty conversation. `StreamAsync` shares a semaphore with both, so preparation and requests cannot interleave RPCs on the same connection. Stop cancels operations **before** waiting for that semaphore, allowing a blocked read to unwind. (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:140) (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:171)

```mermaid
---
config:
  theme: base
  themeVariables:
    primaryColor: "#2d333b"
    primaryBorderColor: "#6d5dfc"
    primaryTextColor: "#e6edf3"
    lineColor: "#8b949e"
---
flowchart TD
    accTitle: Agency lifecycle and terminal evidence
    accDescr: Version and initialization lead to an empty selected session. A single explicit prompt streams bounded text. End-turn and close are required before completion. Failure destroys the engine; success may prepare an empty next session.
    A["Owned layout and wrapper check"] --> B["Initialize ACP"]
    B --> C["New unique session"]
    C --> D["Validate and select agent"]
    D --> E["Empty session ready"]
    E -->|Explicit request| F["One prompt"]
    F --> G["Notices then text deltas"]
    G --> H["Matching end_turn"]
    H --> I["Close session acknowledged"]
    I --> J["Completed event"]
    J -->|Prepare only| C
    D -->|Invalid configuration| X["Stop engine and delete owned tree"]
    G -->|Tool or protocol failure| X
    H -->|Nonanswer or incomplete| X
    I -->|Close failure| X
    style A fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style B fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style C fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style D fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style E fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style F fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style G fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style H fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style I fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style J fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
    style X fill:#2d333b,stroke:#6d5dfc,color:#e6edf3
```

This diagram omits engine rotation for readability. Reuse requires a live process, session capacity and an unchanged executable metadata stamp. The engine rotates after 32 sessions; wrapper verification can remain cached if path/length/timestamps match. That cache is not a cryptographic binary identity. Reuse also revalidates owned settings/TOML/agent content and rejects extra customizations. The sole allowed settings addition is bounded cosmetic `companyAnnouncements`, never changed safety booleans. (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:42) (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:246) (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:399)

There are two version gates: Agency's wrapper prefix includes build/commit, while initialize requires ACP protocol 1, Copilot engine 1.0.92 and close capability. New-session configuration must contain exactly five unique option IDs: agent mode, allow-all off, requested model, low reasoning and the controlled agent. An initially empty agent selection is tolerated only before explicit selection, while the controlled name must already be advertised. Subsequent configuration updates are rechecked. (src/PhantomFudge.Core/Answers/AgencyCliLaunch.cs:8) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:44) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:296)

### Terminal success is deliberately stronger than “some text arrived”

```csharp
await StepAsync(() => connection.CloseSessionAsync(token), token, cancellationToken).ConfigureAwait(false);
token.ThrowIfCancellationRequested();
completed = true;
```

These statements run **after** prompt terminal validation and **before** yielding Completed. Close acknowledgement prevents a failed conversation teardown from becoming a reusable success; the cancellation check prevents a cancelled request from being promoted afterward. A failed or abandoned stream resets the engine, while success may prepare the next empty session without prompting. (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:119) (src/PhantomFudge.Core/Answers/AgencyCliAnswerProvider.cs:125)

Incoming RPCs with a method and ID are reverse client requests and fail rather than opening a permission/input loop. Updates must match the established session, and tool/plan kinds fail. Before answer text, both the disabled-tools notice and exact unknown-sentinel notice are required. Notice parsing checks shape and bounds, not an exhaustive expected tool inventory. Terminal requires matching RPC ID, `end_turn` and nonempty text. (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:213) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:232) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:276)

Default/confirmed answers additionally pass a prose heuristic rejecting selected requests for more input. Code fences are skipped. Only unconfirmed interpretation bypasses that heuristic; it still requires every transport/tool/terminal check. Screening occurs at terminal, so rejected text may already have streamed and must remain labelled incomplete. This is not general semantic answer validation. (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:103) (src/PhantomFudge.Core/Answers/AgencyAcpConnection.cs:330) (tests/PhantomFudge.Core.Tests/AgencyCliAnswerProviderTests.cs:326)

## Process ownership and environment isolation are not a sandbox

The nonempty `--available-tools=phantom_fudge_no_tools` value is consequential. The direct launch source records that bare/empty allowlists restored all tools in the qualified 1.0.81-0 behavior. Therefore “simplifying” the sentinel to an empty value is not equivalent. Agent YAML `tools: []`, wrapper discovery restrictions, CLI deny flags, ACP configuration and observed protocol checks are separate layers. (src/PhantomFudge.Core/Answers/CopilotCliLaunch.cs:14) (src/PhantomFudge.Core/Answers/AgencyAnswerProfile.cs:29)

| Boundary | Direct Copilot / LocalModel | Agency |
| --- | --- | --- |
| Inference choice | Hosted Copilot or explicitly configured loopback model | Hosted Agency model |
| Home/profile | Isolated home; hosted mode uses its dedicated managed profile | Isolated home and generated controlled profile |
| App-data roots | Redirected into owned home | Real APPDATA/LOCALAPPDATA retained for wrapper installation/authentication |
| Engine lifetime | One answer process after a version process | Persistent ACP engine; fresh session per question |

Both paths use shell-free redirected pipes and strip inherited credentials/routing overrides. LocalModel accepts only literal HTTP loopback `/v1` endpoints and supplies offline/provider variables; hostname aliases, encoded paths, credentials and query/fragment are rejected. Validation does not itself implement redirect policy or require an explicit port. Installed-CLI redirect behavior is a separate opt-in integration test. (src/PhantomFudge.Core/Answers/CopilotCliLaunch.cs:84) (src/PhantomFudge.Core/Answers/CopilotCliLaunch.cs:95) (src/PhantomFudge.Core/Answers/AgencyCliLaunch.cs:40) (src/PhantomFudge.Core/Answers/AnswerContracts.cs:68) (tests/PhantomFudge.Core.Tests/CopilotOfflineIntegrationTests.cs:70)

`CopilotOwnedProcess` creates a Windows kill-on-close Job, starts the child, then assigns it. Assignment failure kills/reaps instead of continuing unowned; this post-start assignment is not a suspended-start security sandbox. stderr drains concurrently under a 65,536-character cap, preventing a full stderr pipe from blocking stdout. Cancellation targets the owned process tree/Job, never all processes by name. Disposal bounds waiting and reports cleanup failure. (src/PhantomFudge.Core/Answers/CopilotOwnedProcess.cs:31) (src/PhantomFudge.Core/Answers/CopilotOwnedProcess.cs:57) (src/PhantomFudge.Core/Answers/CopilotOwnedProcess.cs:122)

Deleting owned directories does not erase retained hosted-Copilot profile databases, Agency caches/authentication under real app-data roots, OS artifacts or remote retention. Authentication-marker detection reacts to observable output and cannot prove the wrapper had not already begun an external authentication action. No adapter deliberately launches a login command as recovery. (src/PhantomFudge.Core/Answers/CopilotCliAnswerProvider.cs:68) (src/PhantomFudge.Core/Answers/CopilotCliProfile.cs:76) (src/PhantomFudge.Core/Answers/AgencyCliLaunch.cs:53) (src/PhantomFudge.Core/Answers/AgencyJsonProtocol.cs:54)

## One-shot parsing and the final application state

Direct Copilot is not ACP. `CopilotJsonProtocol` requires one message/turn, matches deltas to message identity, checks the final message equals accumulated text, then requires turn-end, idle, result exit zero **and actual process exit zero**. Final-only output is supported once; reasoning and usage are not answer text. Unknown events and reported tools/customizations fail. Framing limits are independent: 262,144 characters per record, 2,097,152 total, 8,192 records and 32,768 answer characters. EOF without a final newline is incomplete. (src/PhantomFudge.Core/Answers/CopilotJsonProtocol.cs:11) (src/PhantomFudge.Core/Answers/CopilotJsonProtocol.cs:67) (src/PhantomFudge.Core/Answers/CopilotJsonProtocol.cs:137) (src/PhantomFudge.Core/Answers/CopilotJsonProtocol.cs:189)

`AnswerRun` adds application-level protection: foreign/late events are ignored; active matching events must have increasing sequence numbers; overflow rejects the entire new delta. Receiving Completed only sets terminal evidence. The UI waits for enumeration to end, validates clarification output, then calls `run.Complete()`, which requires nonblank text. This catches iterator/cleanup failure after terminal delivery and preserves partial text on cancellation/failure. (src/PhantomFudge.Core/Answers/AnswerRun.cs:34) (src/PhantomFudge.Core/Answers/AnswerRun.cs:57) (src/PhantomFudge.App/MainWindow.Answers.cs:200)

## What the tests and probes establish—and what they do not

The large fake ACP script is part of the test logic: it emits full option sets, verifies agent selection, captures envelopes and injects reverse RPCs, wrong IDs, drift, notices, delays and bad terminals. Launch transformation substitutes PowerShell for a nonexecutable sentinel. These tests exercise actual application protocol/lifecycle code without qualifying a real model or binary. Process cases return early on non-Windows. (tests/PhantomFudge.Core.Tests/AgencyCliAnswerProviderTests.cs:642) (tests/PhantomFudge.Core.Tests/AgencyCliAnswerProviderTests.cs:677)

Other tests separately cover immutable history, raw-default versus clarification stdin, stale run events and fragmented framing. The installed-Copilot integration fixture requires an explicit executable environment variable; absent that variable, it returns without running. When authorized, its loopback SSE peer withholds terminal until the consumer sees the first delta, checks zero tools and no sentinel-file expansion, and observes redirect behavior. Reading these assertions is not reporting a fresh pass. (tests/PhantomFudge.Core.Tests/AnswerClarificationTests.cs:8) (tests/PhantomFudge.Core.Tests/CopilotCliAnswerProviderTests.cs:36) (tests/PhantomFudge.Core.Tests/CopilotJsonProtocolTests.cs:154) (tests/PhantomFudge.Core.Tests/CopilotOfflineIntegrationTests.cs:19) (tests/PhantomFudge.Core.Tests/CopilotOfflineIntegrationTests.cs:134)

### Benchmark provenance is part of the measurement

`AgencyLatencyProbe` references production Core, separates preparation from Ask, retains slow/failed trials and hashes the loaded provider assembly. Counts labelled model attempts/submissions are client attempts, not observed upstream inference counts. Its Dictionary/C#-fence/O(n) usefulness filter is explicitly preliminary; under-five-second prepared samples are not a cold-start SLO or p50/p95 distribution. No generated result rows were interpreted here. (tools/AgencyLatencyProbe/AgencyLatencyProbe.csproj:7) (tools/AgencyLatencyProbe/Program.cs:104) (tools/AgencyLatencyProbe/Program.cs:133) (tools/AgencyLatencyProbe/Program.cs:168)

`check_answer_code.py` is a separate, explicitly reviewed-code execution harness, not production functionality. It hashes original fenced snippets before class renaming/import adaptation, then checks a narrow reflected signature against duplicates, negatives, zeros and overflow-sensitive pairs. Its oracle widens arithmetic before summing. The review flag is operator acknowledgement, not containment for arbitrary generated code. (tools/AgencyProbe/check_answer_code.py:18) (tools/AgencyProbe/check_answer_code.py:30) (tools/AgencyProbe/check_answer_code.py:56) (tools/AgencyProbe/check_answer_code.py:85)

### Python to JavaScript: retain the receipt's evidentiary scope

**Explanatory pseudocode, not production:** the Python probe's wire observations can be expressed in JavaScript without promoting them into a universal guarantee:

```javascript
const users = body.messages.filter(m => m.role === "user");
const observation = {
  toolsCount: (body.tools ?? []).length,
  userMessageCount: users.length,
  assistantMessageCount: body.messages.filter(m => m.role === "assistant").length,
  fixedPolicyInSystemRole: body.messages.some(
    m => m.role === "system" && plainContent(m.content).includes(policy))
};
```

Here `plainContent` represents normalization of string/text-block content, as in the Python helper. This observes the mock endpoint's request, not inaccessible cloud traffic. The Python client is also less strict than production ACP: it records many configuration values instead of enforcing them and skips notice prefixes without requiring both. (tools/AgencyProbe/acp_probe.py:47) (tools/AgencyProbe/acp_probe.py:106) (tools/AgencyProbe/acp_probe.py:184) (tools/AgencyProbe/acp_probe.py:284)

Finally, distinguish smoke tools from latency/cleanup qualification. The older Python one-shot probe retains raw synthetic receipts locally and does not implement the production terminal state machine. `AnswerProbe` checks only a fixed marker and holds its provider through `IAnswerProvider` without explicit Agency async disposal; its total timing is therefore not the latency suite's disposal-inclusive measurement. (tools/AgencyProbe/probe.py:91) (tools/AgencyProbe/probe.py:125) (tools/AnswerProbe/Program.cs:19) (tools/AnswerProbe/Program.cs:38)

## Review boundary

Mechanical line coverage is not proof every subtle behavior has been understood. The atlas explains source guards and test assertions; external CLI libraries, live provider semantics, real devices, model quality, human confirmation usability and recipient-side capture remain separate boundaries. The app's own qualification plan keeps meeting/OBS candidates unqualified; provider isolation does not change that. Direct Azure remains outside the implemented provider enum, and source-only selection/clarification changes are not retroactively included in the historical 045022 ZIP. (docs/QUALIFICATION.md:3) (src/PhantomFudge.Core/Answers/AnswerContracts.cs:33) (TODO/45_OPT_IN_QUESTION_CLARIFICATION.todo:34)
