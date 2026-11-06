# AI assistant

People chat with an AI model about their work: on its own from the **Assistant** button in the top bar or the
`/assistant` page, or about one issue with **Ask AI** on the issue page. The assistant finds issues, summarizes them,
explains them, links every issue it mentions, and, when asked, proposes changes.

## Turning it on

Under **Administration → AI assistant** (permission `SprintBoot.Ai.Manage`):

1. Add a connection. The first one becomes the default; the assistant always uses the default connection.
2. **Test** sends one short prompt and shows the answer or the provider's error.
3. Turn on **Turn on the assistant**. Everyone signed in can then use it.

| Provider | Base URL | API key | Model |
| --- | --- | --- | --- |
| OpenAI Compatible | Required: the chat completions API, such as `https://api.openai.com/v1`, `http://localhost:11434/v1` (Ollama), `https://openrouter.ai/api/v1` | Optional for servers without keys | Required, such as `gpt-5.1` or `llama3.3` |
| Anthropic | Optional, `https://api.anthropic.com` by default | Required | `claude-opus-5-5` by default |

API keys are stored encrypted and never returned. The model must support tool calling.

## What it can do

The assistant uses the same tools as the [MCP server](../apps/api/src/SprintBoot.Host/Mcp), in the person's own request:

- It sees and changes only what that person may. An issue they cannot open does not exist for it.
- Reading tools (search, get issue, comments, backlog, board, users, …) run at once.
- Tools that change something (create or edit issues, comment, transition, log work, move to a sprint, …) **never run on
  their own**. The assistant proposes them as a card with the arguments, and nothing happens until the person clicks
  **Confirm**. **Decline** tells the assistant the change was refused. Asking something else declines what is pending.

In an issue chat, the current issue with its comments goes along with every question, so answers follow the issue as it
changes.

## Conversations

Conversations are stored on the server and private to the person who started them. An issue's conversations open again
from that issue. Every assistant turn records its provider, model and token usage. Deleting a conversation does not undo
changes it made.

## Data sent to the provider

Each question sends the conversation so far, the tools' answers (issue content the person can read), and, for issue
chats, the issue with its comments. Only connect providers the organization trusts with that data.

## Anthropic defaults

- Model `claude-opus-5-5`, effort `low` (set per connection), up to 16,000 output tokens per turn, streamed.
- Prompt caching on every request; the system prompt and tools never change, and history is only appended to, so
  repeated turns read from the cache.
- Claude's own content blocks, signed thinking included, are stored and sent back unchanged.
- Server-side fallback (`fallbacks: "default"`) is on: when the model declines for policy reasons, the API retries on
  the model's default substitute. A turn that still ends in a refusal shows as such.
