# AI assistant

People chat with an AI model about their work: on its own from the **Assistant** button in the top bar or the
`/assistant` page, or about one issue with **Ask AI** on the issue page. The assistant finds issues, summarizes them,
explains them, links every issue it mentions, and, when asked, proposes changes.

## Turning it on

Under **Administration → AI assistant** (permission `SprintBoot.Ai.Manage`):

1. Add a connection. The first one becomes the default; the assistant always uses the default connection.
   In the form, **Load models** fetches the provider's model list (`GET /models`) so the Model field offers it, and
   **Test connection** tries the values as typed, before saving. When editing, a blank API key field means the
   stored key.
2. **Test** in the list sends one short prompt through a saved connection and shows the answer or the provider's error.
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

The issue a conversation is about, and the issue the person has open while asking, go along with every question with
their comments, so "this ticket" or "this page" needs no explaining and answers follow the issue as it changes. An issue
the person cannot open is left out. On an issue, the panel offers one-click questions such as **Summarize this page**.

## Conversations

Conversations are stored on the server and private to the person who started them. Opening the panel starts a new
conversation, tied to the issue on screen if there is one. The three latest conversations show above it, those about the
issue on screen first; **View all** and the **History** button search the rest. The full page (`/assistant`) lists them
all, without the app sidebar. Every assistant turn records its provider, model and token usage. Deleting a conversation
does not undo changes it made.

A conversation starts with the beginning of its first question as its title. Once the assistant has answered it,
`ConversationTitleWorker` asks the default connection for a short title from that exchange, without tools or issue
content (poll interval `Assistant:Titles:PollMilliseconds`, 5000 by default). A title the person set is never replaced;
if naming fails, the draft title stays.

## Data sent to the provider

Each question sends the conversation so far, the tools' answers (issue content the person can read), and the issues
described above with their comments. Naming a conversation sends the text of its first question and answer again. Only
connect providers the organization trusts with that data.

## Anthropic defaults

- Model `claude-opus-5-5`, effort `low` (set per connection), up to 16,000 output tokens per turn, streamed.
- Prompt caching on every request; the system prompt and tools never change, and history is only appended to, so
  repeated turns read from the cache.
- Claude's own content blocks, signed thinking included, are stored and sent back unchanged.
- Server-side fallback (`fallbacks: "default"`) is on: when the model declines for policy reasons, the API retries on
  the model's default substitute. A turn that still ends in a refusal shows as such.
