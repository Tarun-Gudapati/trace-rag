namespace TraceRag.Core;

public sealed record DocumentInput(string Id, string Title, string Content);

public sealed record DocumentChunk(
    string DocumentId,
    string DocumentTitle,
    int Index,
    string Text);

public sealed record StoredDocument(
    string Id,
    string Title,
    IReadOnlyList<DocumentChunk> Chunks);

public sealed record DocumentReceipt(
    string Id,
    string Title,
    int ChunkCount);

public sealed record RetrievalMatch(
    DocumentChunk Chunk,
    double Score,
    IReadOnlyList<string> MatchedTerms);

public sealed record Citation(
    string DocumentId,
    string Title,
    int Chunk,
    string Evidence);

public sealed record AnswerDraft(
    string Answer,
    IReadOnlyList<Citation> Citations);

public sealed record SelfCheckResult(
    bool Passed,
    int ClaimsChecked,
    IReadOnlyList<string> UnsupportedClaims);

public sealed record AskResult(
    string Answer,
    bool HasSufficientEvidence,
    double Confidence,
    IReadOnlyList<Citation> Citations,
    SelfCheckResult SelfCheck);

public static class TraceRagResponses
{
    public const string InsufficientEvidence =
        "Insufficient evidence in the indexed documents.";
}

public sealed class DuplicateDocumentException(string documentId)
    : InvalidOperationException($"A document with ID '{documentId}' already exists.")
{
    public string DocumentId { get; } = documentId;
}
