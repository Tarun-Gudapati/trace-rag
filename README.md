# TraceRAG

> **Status: Concept scaffold (working local retrieval baseline).**

TraceRAG is a citation-first, self-checking document retrieval starter built
with .NET 10. It intentionally uses deterministic local algorithms so its
behavior is inspectable: there is no hosted model, hidden network call,
embedding model, or claim that keyword matching is artificial intelligence.

The current baseline can:

- ingest bounded plain-text documents into process memory;
- create deterministic, overlapping chunks;
- retrieve chunks with weighted token-frequency cosine similarity and keyword
  coverage;
- return one verbatim sentence as an extractive answer;
- cite its document ID, title, one-based chunk number, and exact evidence;
- return an explicit insufficient-evidence response when retrieval finds no
  acceptable match; and
- check every answer sentence against the cited evidence, withholding an answer
  if a claim is unsupported.

## Architecture

```text
POST /api/documents
  -> ITextChunker
  -> IDocumentStore (in memory)

POST /api/ask
  -> IRetriever
  -> IAnswerGenerator (extractive)
  -> IAnswerEvaluator (strict evidence check)
  -> answer + citations + confidence + selfCheck
```

| Project | Responsibility |
| --- | --- |
| `src/TraceRag.Core` | Domain records, abstractions, deterministic chunking, storage, retrieval, extraction, evaluation, and orchestration |
| `src/TraceRag.Api` | Minimal HTTP API, request validation, dependency wiring, and OpenAPI |
| `tests/TraceRag.Tests` | Deterministic unit tests and in-process API integration tests |

The important seams are `ITextChunker`, `IRetriever`, `IAnswerGenerator`, and
`IAnswerEvaluator`. `IEmbeddingProvider` and `ILanguageModelProvider` are
unwired extension contracts only; the baseline does not pretend to implement
them and requires no API keys.

### Baseline algorithm

1. The chunker normalizes whitespace and groups at most 120
   whitespace-delimited words, with 20 words of overlap. Chunk numbers start at
   one.
2. The tokenizer lowercases Unicode letter/number tokens and removes a small,
   explicit stop-word set.
3. Retrieval scores every chunk with `0.65 * cosine similarity + 0.35 * query
   term coverage`. Chunks below `0.18` are rejected. Ties are resolved by
   document ID and chunk number, so results are repeatable.
4. The answer generator selects the highest-overlap sentence from retrieved
   evidence and returns that sentence verbatim.
5. The evaluator treats answer sentences as claims. A claim passes only when
   its normalized text occurs in a citation's evidence. If a future generator
   emits an unsupported claim, the pipeline returns insufficient evidence and
   reports the unsupported claim in `selfCheck.unsupportedClaims`.

`confidence` is the top lexical retrieval score rounded to three decimal
places. It is a ranking heuristic, **not** a calibrated probability, factuality
guarantee, or model confidence.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

No database, model runtime, account, secret, or external service is required.

## Run locally

```powershell
dotnet restore TraceRag.sln
dotnet run --project src/TraceRag.Api --launch-profile http
```

The HTTP profile listens at `http://localhost:5080`.

- Health: `http://localhost:5080/health`
- OpenAPI JSON: `http://localhost:5080/openapi/v1.json`
- Ready-to-run requests: `samples/requests.http`

`.env.example` documents optional ASP.NET Core environment values for shell or
container tooling. `dotnet run` does not automatically load `.env` files.

## API

### `GET /health`

Returns service state, storage type, and the number of indexed documents.

```json
{
  "status": "healthy",
  "storage": "in-memory",
  "indexedDocuments": 0
}
```

### `POST /api/documents`

Ingests one plain-text document. `id` is optional; the API creates a GUID when
it is omitted.

```json
{
  "id": "return-policy",
  "title": "Return policy",
  "content": "Customers may request a refund within 30 calendar days of purchase. A receipt or order number is required."
}
```

Success is `201 Created`:

```json
{
  "id": "return-policy",
  "title": "Return policy",
  "chunkCount": 1
}
```

The API returns RFC 7807 validation responses for malformed input and
`409 Conflict` for a duplicate ID. Limits are:

- ID: 1-100 letters, numbers, `.`, `_`, or `-`;
- title: 1-200 characters; and
- content: 1-250,000 characters.

Documents cannot currently be listed, updated, or deleted.

### `POST /api/ask`

```json
{
  "question": "What is the refund window?",
  "maxResults": 5
}
```

`maxResults` is optional and must be 1-10. Questions are limited to 1,000
characters.

A supported answer has this shape:

```json
{
  "answer": "Customers may request a refund within 30 calendar days of purchase.",
  "hasSufficientEvidence": true,
  "confidence": 0.65,
  "citations": [
    {
      "documentId": "return-policy",
      "title": "Return policy",
      "chunk": 1,
      "evidence": "Customers may request a refund within 30 calendar days of purchase."
    }
  ],
  "selfCheck": {
    "passed": true,
    "claimsChecked": 1,
    "unsupportedClaims": []
  }
}
```

An unmatched question still returns `200 OK`, because the retrieval operation
completed successfully:

```json
{
  "answer": "Insufficient evidence in the indexed documents.",
  "hasSufficientEvidence": false,
  "confidence": 0,
  "citations": [],
  "selfCheck": {
    "passed": true,
    "claimsChecked": 0,
    "unsupportedClaims": []
  }
}
```

Clients must inspect `hasSufficientEvidence`; HTTP success does not mean an
answer was found.

### PowerShell example using a sample file

With the API running:

```powershell
$content = Get-Content samples/documents/return-policy.txt -Raw
$document = @{
  id = "return-policy"
  title = "Return policy"
  content = $content
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5080/api/documents `
  -ContentType application/json `
  -Body $document

$question = @{
  question = "What is the refund window?"
  maxResults = 5
} | ConvertTo-Json

Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5080/api/ask `
  -ContentType application/json `
  -Body $question
```

## Build and test

```powershell
dotnet build TraceRag.sln --configuration Release
dotnet test TraceRag.sln --configuration Release --no-build
```

Warnings are treated as errors. GitHub Actions runs restore, Release build, and
the test suite on pushes to `main`, pull requests, and manual dispatches.

## Privacy and security

- Document text stays in the API process. The baseline sends no telemetry,
  document content, prompts, or questions to model providers.
- Data is not encrypted at rest because there is no persistence; all indexed
  content disappears when the process stops.
- The API has bounded request fields to reduce accidental memory exhaustion,
  but it has no total corpus quota.
- There is no authentication, authorization, tenant isolation, rate limiting,
  audit trail, malware scanning, or document-level access control.
- The sample HTTP profile is unencrypted, and OpenAPI is always exposed.

Treat this as a local development scaffold. Do not expose it to an untrusted
network or ingest confidential/regulated material. A deployment must add HTTPS,
identity and authorization, per-tenant isolation, request/body limits at the
server or proxy, rate limits, safe logging, retention/deletion controls,
dependency monitoring, and an abuse model.

If an embedding or language-model provider is added later, document its data
retention and training policy, make network use visible and configurable,
obtain the required consent, keep secrets out of source control, and preserve
the citation/self-check boundary.

## Limitations

- Process-local storage is lost on restart and cannot be shared across
  instances.
- Only JSON-wrapped plain text is accepted; there is no file upload, PDF/Office
  parser, OCR, metadata filtering, or URL ingestion.
- Retrieval is bag-of-words matching. It has no stemming, synonyms, semantic
  understanding, reranking, or learned embeddings.
- Whitespace word counts and punctuation-based sentence splitting are simple
  heuristics and are not suitable for every language.
- Extractive answers are intentionally narrow and cannot safely synthesize
  facts across passages.
- The strict self-check detects text unsupported by the supplied citation; it
  does not prove that a source is true, current, complete, or non-malicious.
- Confidence is uncalibrated and should not drive high-impact decisions.
- The in-memory store uses linear corpus scanning, so latency and memory usage
  grow with every document.

## Roadmap

1. Add document lifecycle APIs, durable storage, content hashing, corpus quotas,
   and metadata filters.
2. Add a stronger local lexical index (for example, BM25) and benchmark it on a
   versioned retrieval evaluation set.
3. Add opt-in embedding providers behind `IEmbeddingProvider`, with an explicit
   offline option and provenance for model/version changes.
4. Add opt-in grounded generation behind `ILanguageModelProvider`, constrained
   to retrieved evidence and followed by claim-level evaluation.
5. Measure retrieval recall, citation precision, answer support, refusal
   quality, latency, and cost in CI.
6. Add authentication, authorization, tenancy, rate limiting, observability,
   redaction, and retention controls before any hosted deployment.

## License

MIT. See `LICENSE`.
