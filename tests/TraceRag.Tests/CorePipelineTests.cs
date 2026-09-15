using TraceRag.Core;

namespace TraceRag.Tests;

public sealed class CorePipelineTests
{
    [Fact]
    public void ChunkerIsDeterministicAndUsesOverlap()
    {
        var chunker = new DeterministicTextChunker(
            new ChunkingOptions(MaxWords: 4, OverlapWords: 1));
        var document = new DocumentInput(
            "doc-1",
            "Numbers",
            "one two three four five six seven");

        var firstRun = chunker.Chunk(document);
        var secondRun = chunker.Chunk(document);

        Assert.Equal(firstRun, secondRun);
        Assert.Collection(
            firstRun,
            chunk =>
            {
                Assert.Equal(1, chunk.Index);
                Assert.Equal("one two three four", chunk.Text);
            },
            chunk =>
            {
                Assert.Equal(2, chunk.Index);
                Assert.Equal("four five six seven", chunk.Text);
            });
    }

    [Fact]
    public void RetrieverRanksTheMostRelevantChunkFirst()
    {
        var retriever = new TokenSimilarityRetriever();
        DocumentChunk[] chunks =
        [
            new("basic", "Basic", 1, "The Basic plan includes email support."),
            new("enterprise", "Enterprise", 1, "The Enterprise plan includes audit logs.")
        ];

        var matches = retriever.Retrieve(
            "Which plan includes audit logs?",
            chunks,
            maxResults: 5);

        var match = Assert.Single(
            matches,
            candidate => candidate.Chunk.DocumentId == "enterprise");
        Assert.Equal("enterprise", matches[0].Chunk.DocumentId);
        Assert.Contains("audit", match.MatchedTerms);
        Assert.Contains("logs", match.MatchedTerms);
    }

    [Fact]
    public void PipelineReturnsExtractiveAnswerWithCitationAndPassingSelfCheck()
    {
        var pipeline = CreatePipeline();
        pipeline.Ingest(
            new DocumentInput(
                "returns",
                "Return policy",
                "Customers may request a refund within 30 calendar days of purchase. " +
                "The receipt must be retained."));

        var result = pipeline.Ask("What is the refund window?");

        Assert.True(result.HasSufficientEvidence);
        Assert.Equal(
            "Customers may request a refund within 30 calendar days of purchase.",
            result.Answer);
        Assert.InRange(result.Confidence, 0.001, 1);
        var citation = Assert.Single(result.Citations);
        Assert.Equal("returns", citation.DocumentId);
        Assert.Equal("Return policy", citation.Title);
        Assert.Equal(1, citation.Chunk);
        Assert.Equal(result.Answer, citation.Evidence);
        Assert.True(result.SelfCheck.Passed);
        Assert.Empty(result.SelfCheck.UnsupportedClaims);
    }

    [Fact]
    public void PipelineReturnsExplicitInsufficientEvidenceForUnmatchedQuestion()
    {
        var pipeline = CreatePipeline();
        pipeline.Ingest(
            new DocumentInput(
                "returns",
                "Return policy",
                "Refund requests are accepted within 30 days."));

        var result = pipeline.Ask("Where is the lunar research laboratory?");

        Assert.False(result.HasSufficientEvidence);
        Assert.Equal(TraceRagResponses.InsufficientEvidence, result.Answer);
        Assert.Equal(0, result.Confidence);
        Assert.Empty(result.Citations);
        Assert.True(result.SelfCheck.Passed);
        Assert.Equal(0, result.SelfCheck.ClaimsChecked);
    }

    [Fact]
    public void EvaluatorIdentifiesUnsupportedClaims()
    {
        var evaluator = new StrictAnswerEvaluator();
        var draft = new AnswerDraft(
            "Refunds are always completed in five days.",
            [
                new Citation(
                    "returns",
                    "Return policy",
                    1,
                    "Refund requests are accepted within 30 days.")
            ]);

        var result = evaluator.Evaluate(draft);

        Assert.False(result.Passed);
        Assert.Equal(1, result.ClaimsChecked);
        Assert.Equal(
            ["Refunds are always completed in five days."],
            result.UnsupportedClaims);
    }

    [Fact]
    public void PipelineWithholdsUnsupportedGeneratorOutput()
    {
        var pipeline = new TraceRagPipeline(
            new DeterministicTextChunker(),
            new InMemoryDocumentStore(),
            new TokenSimilarityRetriever(),
            new UnsupportedAnswerGenerator(),
            new StrictAnswerEvaluator());
        pipeline.Ingest(
            new DocumentInput(
                "returns",
                "Return policy",
                "Refund requests are accepted within 30 days."));

        var result = pipeline.Ask("How long are refund requests accepted?");

        Assert.False(result.HasSufficientEvidence);
        Assert.Equal(TraceRagResponses.InsufficientEvidence, result.Answer);
        Assert.Empty(result.Citations);
        Assert.False(result.SelfCheck.Passed);
        Assert.Equal(
            ["Refunds are always completed in five days."],
            result.SelfCheck.UnsupportedClaims);
    }

    [Fact]
    public void PipelineRejectsDuplicateDocumentIds()
    {
        var pipeline = CreatePipeline();
        var document = new DocumentInput("same-id", "First", "Some useful content.");
        pipeline.Ingest(document);

        var exception = Assert.Throws<DuplicateDocumentException>(
            () => pipeline.Ingest(
                new DocumentInput("same-id", "Second", "Different content.")));

        Assert.Equal("same-id", exception.DocumentId);
    }

    private static TraceRagPipeline CreatePipeline() =>
        new(
            new DeterministicTextChunker(),
            new InMemoryDocumentStore(),
            new TokenSimilarityRetriever(),
            new ExtractiveAnswerGenerator(),
            new StrictAnswerEvaluator());

    private sealed class UnsupportedAnswerGenerator : IAnswerGenerator
    {
        public AnswerDraft Generate(
            string question,
            IReadOnlyList<RetrievalMatch> matches)
        {
            var chunk = matches[0].Chunk;
            return new AnswerDraft(
                "Refunds are always completed in five days.",
                [
                    new Citation(
                        chunk.DocumentId,
                        chunk.DocumentTitle,
                        chunk.Index,
                        chunk.Text)
                ]);
        }
    }
}
