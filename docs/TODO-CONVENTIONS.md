# TODO catalogue conventions

This structure follows the recurring numbered catalogue observed in the owner's existing
projects. The survey also found narrative Markdown roadmaps and unstructured checklists;
it did not establish one universal format across all projects.

The survey covered indexed context bases and local home/game project roots using a bounded
planning-file inventory. Generated archives, dependencies, remote issue trackers, and
unavailable reference paths were not claimed as inspected. Detailed sibling-path provenance
is retained privately in the planning session, not copied into this repository.

## Canonical form

`TODO\00_INDEX.todo` lists stable `NN_UPPER_SNAKE_CASE.todo` workstreams.
Do not renumber completed IDs to insert work. Add a new ID and update dependencies instead.

```text
# Workstream name

Status: NOT_STARTED
Milestone: M2 Core utility
Dependencies: 04_PROJECT_FOUNDATION.todo

## Purpose
One scoped outcome.

## Tasks
- [ ] Concrete work with an observable result.

## Acceptance criteria
- [ ] Specific completion condition.

## Completion evidence
- None yet. Planned artifacts are not evidence.

## Completion log
- YYYY-MM-DD - Planned; implementation not started.
```

## Status and dependency semantics

| Status | Meaning |
| --- | --- |
| NOT_STARTED | Planned; no implementation/execution has begun |
| IN_PROGRESS | Active work or a partly resolved decision with evidence |
| BLOCKED | Named missing decision, dependency, environment, or human approval prevents progress |
| DONE | All scoped tasks and acceptance conditions met with recorded evidence |

An unstarted task may remain NOT_STARTED while dependencies are incomplete. Use BLOCKED
when attempting work encounters a concrete blocker; record owner, needed action, and impact.
References in `Dependencies` are hard completion prerequisites unless a document explicitly
labels them as reference-only. Dependencies are relative to the containing `.todo` file.

Mark IN_PROGRESS before execution. Update tasks, evidence, index, and ledger together.
Reopen a DONE task if a regression invalidates its acceptance; retain its earlier evidence
as historical rather than deleting or silently reusing it.

## Completion evidence

Record date/time and timezone, source revision or source hash, artifact path/hash, commands
and meaningful results, environment, remaining limits, and reviewer/owner approval where
required. Never invent a commit when working in an uncommitted tree.

Unit tests, human UX approval, capture observations, and source research are different
evidence classes. A getter/return code cannot satisfy a capture criterion. A negative OCR
result cannot establish pixel absence. Runtime success cannot supply missing owner approval.

`WORKFLOWS` contains authored procedures, not a second copy of implementation progress.
Those files can be DONE for **procedure definition** while their execution is NOT_STARTED.
Their headers and evidence explicitly distinguish the two.

Completed tasks and superseded decisions remain in place with dated logs. Keep current
status concise in the index; keep longer history in the development ledger.
