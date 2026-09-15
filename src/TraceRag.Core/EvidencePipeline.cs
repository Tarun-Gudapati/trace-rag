namespace TraceRag.Core;

public sealed class ExtractiveAnswerGenerator : IAnswerGenerator
{
    public AnswerDraft Generate(
        string question,
        IReadOnlyList<RetrievalMatch> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);

        if (matches.Count == 0)
        {
            return new AnswerDraft(string.Empty, []);
        }

        var queryTerms = TextAnalysis
            .Tokenize(question)
            .ToHashSet(StringComparer.Ordinal);

        var bestExtract = matches
            .SelectMany(
                (match, matchIndex) => TextAnalysis
                    .SplitClaims(match.Chunk.Text)
                    .Select(
                        (sentence, sentenceIndex) => new
                        {
                            Match = match,
                            MatchIndex = matchIndex,
                            Sentence = sentence,
                            SentenceIndex = sentenceIndex,
                            Overlap = TextAnalysis
                                .Tokenize(sentence)
                                .Distinct(StringComparer.Ordinal)
                                .Count(queryTerms.Contains)
                        }))
            .Where(candidate => candidate.Overlap > 0)
            .OrderByDescending(candidate => candidate.Overlap)
            .ThenByDescending(candidate => candidate.Match.Score)
            .ThenBy(candidate => candidate.MatchIndex)
            .ThenBy(candidate => candidate.SentenceIndex)
            .FirstOrDefault();

        if (bestExtract is null)
        {
            return new AnswerDraft(string.Empty, []);
        }

        var citation = new Citation(
            bestExtract.Match.Chunk.DocumentId,
            bestExtract.Match.Chunk.DocumentTitle,
            bestExtract.Match.Chunk.Index,
            bestExtract.Sentence);

        return new AnswerDraft(bestExtract.Sentence, [citation]);
    }
}

public sealed class StrictAnswerEvaluator : IAnswerEvaluator
{
    public SelfCheckResult Evaluate(AnswerDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var claims = TextAnalysis.SplitClaims(draft.Answer);
        var normalizedEvidence = draft.Citations
            .Select(citation => TextAnalysis.Normalize(citation.Evidence))
            .ToArray();

        var unsupportedClaims = claims
            .Where(
                claim =>
                {
                    var normalizedClaim = TextAnalysis.Normalize(claim);
                    return !normalizedEvidence.Any(
                        evidence => evidence.Contains(
                            normalizedClaim,
                            StringComparison.Ordinal));
                })
            .ToArray();

        return new SelfCheckResult(
            unsupportedClaims.Length == 0,
            claims.Count,
            unsupportedClaims);
    }
}

public sealed class TraceRagPipeline(
    ITextChunker chunker,
    IDocumentStore store,
    IRetriever retriever,
    IAnswerGenerator answerGenerator,
    IAnswerEvaluator evaluator) : ITraceRagPipeline
{
    public DocumentReceipt Ingest(DocumentInput document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var chunks = chunker.Chunk(document);
        var storedDocument = new StoredDocument(
            document.Id.Trim(),
            document.Title.Trim(),
            chunks);

        if (!store.TryAdd(storedDocument))
        {
            throw new DuplicateDocumentException(storedDocument.Id);
        }

        return new DocumentReceipt(
            storedDocument.Id,
            storedDocument.Title,
            chunks.Count);
    }

    public AskResult Ask(string question, int maxResults = 5)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("Question is required.", nameof(question));
        }

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                "At least one result must be requested.");
        }

        var matches = retriever.Retrieve(
            question.Trim(),
            store.GetAllChunks(),
            maxResults);

        if (matches.Count == 0)
        {
            return Insufficient();
        }

        var draft = answerGenerator.Generate(question.Trim(), matches);
        if (string.IsNullOrWhiteSpace(draft.Answer) || draft.Citations.Count == 0)
        {
            return Insufficient();
        }

        var selfCheck = evaluator.Evaluate(draft);
        if (!selfCheck.Passed)
        {
            return Insufficient(selfCheck);
        }

        var citedChunkKeys = draft.Citations
            .Select(citation => (citation.DocumentId, citation.Chunk))
            .ToHashSet();
        var confidence = matches
            .Where(
                match => citedChunkKeys.Contains(
                    (match.Chunk.DocumentId, match.Chunk.Index)))
            .Select(match => match.Score)
            .DefaultIfEmpty()
            .Max();

        return new AskResult(
            draft.Answer,
            HasSufficientEvidence: true,
            Confidence: Math.Round(confidence, 3),
            draft.Citations,
            selfCheck);
    }

    private static AskResult Insufficient(SelfCheckResult? selfCheck = null) =>
        new(
            TraceRagResponses.InsufficientEvidence,
            HasSufficientEvidence: false,
            Confidence: 0,
            Citations: [],
            SelfCheck: selfCheck ?? new SelfCheckResult(
                Passed: true,
                ClaimsChecked: 0,
                UnsupportedClaims: []));
}
