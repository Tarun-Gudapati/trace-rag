namespace TraceRag.Core;

public interface ITextChunker
{
    IReadOnlyList<DocumentChunk> Chunk(DocumentInput document);
}

public interface IDocumentStore
{
    int DocumentCount { get; }

    bool TryAdd(StoredDocument document);

    IReadOnlyList<DocumentChunk> GetAllChunks();
}

public interface IRetriever
{
    IReadOnlyList<RetrievalMatch> Retrieve(
        string question,
        IReadOnlyList<DocumentChunk> chunks,
        int maxResults);
}

public interface IAnswerGenerator
{
    AnswerDraft Generate(
        string question,
        IReadOnlyList<RetrievalMatch> matches);
}

public interface IAnswerEvaluator
{
    SelfCheckResult Evaluate(AnswerDraft draft);
}

public interface ITraceRagPipeline
{
    DocumentReceipt Ingest(DocumentInput document);

    AskResult Ask(string question, int maxResults = 5);
}

// Extension seam only: the local baseline does not call an embedding service.
public interface IEmbeddingProvider
{
    Task<ReadOnlyMemory<float>> EmbedAsync(
        string text,
        CancellationToken cancellationToken = default);
}

public sealed record LanguageModelRequest(
    string Question,
    IReadOnlyList<Citation> Evidence);

// Extension seam only: the local baseline does not call an LLM.
public interface ILanguageModelProvider
{
    Task<string> GenerateAsync(
        LanguageModelRequest request,
        CancellationToken cancellationToken = default);
}
