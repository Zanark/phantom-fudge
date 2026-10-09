# Azure answer boundary and identity design

Research date:2026-10-08. **Design only; no Azure resource, sign-in, inference call or upload
was performed for this design.** Installed-provider answers are now implemented separately.

The optional, default-off [Clarify first mode](QUESTION-CLARIFICATION.md) is an explicitly requested
exception to the single-question-only payload: it includes a fixed instruction and this question's
bounded interpretation/reply history. It does not authorize unrelated transcript, logs or machine
metadata. Direct Azure is still future work and must preserve this distinction if implemented.

## The proposed default

Keep Computer/Mic audio and ASR local. Only an explicit **Ask** action sends the immutable
question text visible in the draft to the configured Azure model. Do not upload the full
meeting transcript, notes, source audio, chat history, clipboard history, screenshots or
unselected Computer-track text. No automatic question detection or send-on-every-partial.

The owner wants Azure where useful. This authorizes designing the cloud boundary, not silently
changing the existing app into a cloud recorder. Azure Speech would be a separately enabled,
optional **cloud-audio** mode; it is not required for Azure text answers.

Connection setup should clearly display the chosen account/tenant, endpoint, deployment and
processing policy. This is actual provider configuration/authentication, not reintroduced
transcription-permission or recipient-rehearsal checkboxes. After configuration, Ask itself is
the deliberate send action; there is no second confirmation for every question.

## Desktop identity: public client, not an embedded service secret

For the first single-user/internal-desktop path, prefer direct Azure inference using a
tenant-bound public-client identity and Microsoft Entra delegated user access.

1. Register a desktop/public client in the owner's approved tenant; select the exact tenant
   and account intentionally. The client ID is not a secret. Do not ship an application secret,
   certificate private key, shared Azure API key or refresh token in the EXE/Python/config/ZIP.
2. Use supported MSAL/WAM or Azure.Identity broker integration and the existing WPF HWND.
   WAM is available in an interactive Windows session, not an arbitrary Windows service.
3. Prefer explicit initial Connect/Sign in outside a live presentation. Broker/browser
   authentication surfaces are outside the app's capture-exclusion guarantee.
4. In the Ask path, try cached/silent acquisition only. Azure.Identity supports
   `DisableAutomaticAuthentication=true`; `AuthenticationRequiredException` then becomes an
   inline **Sign-in required** state. MSAL equivalents use `AcquireTokenSilent` and surface
   `MsalUiRequiredException` without automatically invoking interactive UI.
5. Cache/reuse the credential/client; allow the library/broker to manage refresh. Never put
   access-token acquisition with a broad fallback chain, login dialogs, or CLI subprocesses
   on every question's critical path.
6. Grant the user inference permission at the narrowest practical resource scope. For the
   documented Azure OpenAI resource path, **Cognitive Services OpenAI User** permits Entra
   inference without key access or deployment creation. A generic management Contributor
   role is not a substitute for the required data-plane permission.
7. Request only the model-service permissions actually needed. There is no reason for this
   feature to request Graph chat/mail/contact permissions or subscription Owner.

Tenant consent policy, public-client registration, Conditional Access and model-resource
permissions must be verified during the authorized integration spike. The general broker
guide includes examples for other services; do not copy its Graph/Storage permissions into
this app automatically.

### Token audience is adapter-specific

Current Azure OpenAI v1 Entra documentation uses `https://ai.azure.com/.default`; ARM discovery
uses `https://management.azure.com/.default`. Older Azure SDK/API examples also use Cognitive
Services audiences. Pin the selected SDK/API and verify its required audience rather than
mixing snippets. A data-plane token must never be reused as an ARM management token.

`DefaultAzureCredential` can be convenient for an authorized developer experiment, but the
shipped desktop should have a deterministic, configured user-credential path. Managed identity
is for an Azure-hosted broker, not something the laptop automatically possesses.

## Optional organization-wide broker

Add a small Azure-hosted broker only if policy requires centrally enforced per-user quotas,
private networking, shared service identity, model routing or audit controls that a desktop
client cannot enforce. It adds an extra hop and hosting lifecycle; it is not the default
speed optimization.

The desktop authenticates to the broker's own audience/scope. The broker validates tenant,
issuer, audience and scopes, then uses a managed identity with model-resource inference RBAC.
It owns endpoint/model allowlists, request/token limits and redacted metadata. Do not accept
arbitrary upstream URLs, model names, tools, storage paths or downstream bearer tokens from
dictated content.

If APIM is later required, preserve SSE intentionally: `forward-request` defaults to response
buffering; Microsoft documents `buffer-response=false` for immediate chunk forwarding.
Disable response/body transformations or diagnostics that accidentally buffer/log the stream.
Do not introduce APIM, Functions cold starts, AKS, queues, databases or containers merely to
call one model. Docker remains optional final-stage packaging.

## What Azure's data statements do and do not mean

Microsoft's current **Models sold by Azure** statement says prompts/completions are not
available to other customers or model providers and are not used to train foundation models
without permission/instruction. That is **not** a promise that no processing or storage exists.

Important distinctions:

- Optional stateful features, including Responses history, Assistants threads, Files/vector
  stores and stored completions, have their own storage behavior. Prefer stateless requests
  and explicit non-storage flags where supported; keep bounded conversation state locally.
- Model statelessness and `store:false` do not disable abuse monitoring or override legal/
  organizational policy. Flagged content can be selected for automated or human review.
  Modified abuse monitoring requires eligibility/approval; never assume this account has it.
- Global deployments may process prompts in any supported geography. DataZone processing
  stays within the specified zone, not necessarily the resource's individual region. Regional
  deployment/privacy statements also distinguish geography from a single region.
- Therefore a resource in a geographically close region does not by itself establish either
  processing locality or low latency. Deployment type is a governance decision and a benchmark
  variable, not a string to choose automatically.
- Azure Speech real-time documentation states in-memory processing/no at-rest audio retention,
  but raw audio still leaves the machine. Batch storage and customer-managed logs are separate.
  None of these statements removes the need for the owner's approved data-processing policy.
- Preview features can carry different terms/constraints; a preview Speech commit or audio API
  must not silently become the production data path.

No cloud policy is inferred from the owner's standing permission to transcribe meetings.
The new Ask feature must display its actual Azure destination and send only the chosen text.

## Local application safeguards

- Plain-text streamed answers only initially. Do not render model text as XAML/HTML, load remote
  images, execute code/tool calls, read local files, open links or post replies automatically.
- Treat transcript content as data. No role promotion of spoken text into system/developer
  instructions, no secret injection, and no model-controlled tool permissions.
- Capture session, source, draft revision, question ID and request ID bind every operation.
  A stale final/commit/token cannot overwrite a newer draft or answer.
- Keep one active answer request; cancel explicitly and retain visible partial output as
  incomplete. Do not silently resend after ambiguous network failure or after partial output.
  Cancellation does not promise server-side compute termination or a refund.
- Enforce configured HTTPS endpoint/audience/deployment allowlists, byte/token/output limits,
  finite timeouts and bounded streams. No automatic failover to a different region/provider.
- Do not log question/answer/audio text, text hashes, access tokens, authorization headers,
  clipboard content or sensitive source metadata. Use random request IDs, stage timings,
  counts, result codes and model/config version instead.
- If telemetry is later enabled, verify SDK/HTTP/tracing content capture is off. A checkbox
  labelled "diagnostics" must not quietly export prompts. Synthetic benchmark data is separate.
- No automatic Q/A persistence; explicit export only. OS memory/dumps/clipboard and already
  captured frames remain outside this application's guarantee.

## Primary references actually retrieved

All accessed2026-10-08. These are service/library contracts, not proof of this user's account policy.

| ID | Source and supported claim |
| --- | --- |
| P01 | [Models sold by Azure data privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/openai/data-privacy): training/provider separation, stateful storage, processing geography and review |
| P02 | [Abuse monitoring](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/abuse-monitoring): automated/human review and modified-monitoring eligibility |
| P03 | [Speech data privacy](https://learn.microsoft.com/en-us/azure/foundry/responsible-ai/speech-service/speech-to-text/data-privacy-security): real-time memory processing versus batch storage |
| P04 | [Public/confidential client applications](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications): desktop apps cannot safely hold application secrets |
| P05 | [MSAL.NET WAM](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam): broker, HWND, interactive-session and silent-token behavior |
| P06 | [Azure.Identity broker integration](https://learn.microsoft.com/en-us/dotnet/azure/sdk/authentication/local-development-broker): broker credential options and app-registration integration |
| P07 | [DisableAutomaticAuthentication](https://learn.microsoft.com/en-us/dotnet/api/azure.identity.interactivebrowsercredentialoptions.disableautomaticauthentication?view=azure-dotnet): explicit opt-in interactive authentication instead of surprise prompts |
| P08 | [Azure OpenAI RBAC](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/role-based-access-control): OpenAI User inference and limits versus contributor roles |
| P09 | [Entra/managed identity for Azure OpenAI](https://learn.microsoft.com/en-us/azure/foundry-classic/openai/how-to/managed-identity): v1 audience, data/control planes and identity placement |
| P10 | [APIM forward-request](https://learn.microsoft.com/en-us/azure/api-management/forward-request-policy): default buffering and SSE forwarding setting |

SDK versions, exact inference adapter/audience, app registration and tenant consent still require
an authorized integration check. No password, resource key, account or subscription was inspected here.
