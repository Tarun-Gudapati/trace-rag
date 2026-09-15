using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace TraceRag.Core;

public sealed record ChunkingOptions(
    int MaxWords = 120,
    int OverlapWords = 20);

public sealed record RetrievalOptions(
    double MinimumScore = 0.18,
    double CosineWeight = 0.65,
    double CoverageWeight = 0.35);

public sealed class DeterministicTextChunker : ITextChunker
{
    private readonly ChunkingOptions _options;

    public DeterministicTextChunker(ChunkingOptions? options = null)
    {
        _options = options ?? new ChunkingOptions();

        if (_options.MaxWords < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MaxWords must be greater than zero.");
        }

        if (_options.OverlapWords < 0 ||
            _options.OverlapWords >= _options.MaxWords)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "OverlapWords must be non-negative and smaller than MaxWords.");
        }
    }

    public IReadOnlyList<DocumentChunk> Chunk(DocumentInput document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(document.Id))
        {
            throw new ArgumentException("Document ID is required.", nameof(document));
        }

        if (string.IsNullOrWhiteSpace(document.Title))
        {
            throw new ArgumentException("Document title is required.", nameof(document));
        }

        if (string.IsNullOrWhiteSpace(document.Content))
        {
            throw new ArgumentException("Document content is required.", nameof(document));
        }

        var words = Regex
            .Split(document.Content.Trim(), @"\s+")
            .Where(word => word.Length > 0)
            .ToArray();

        var chunks = new List<DocumentChunk>();
        var step = _options.MaxWords - _options.OverlapWords;
        var chunkNumber = 1;

        for (var start = 0; start < words.Length; start += step)
        {
            var length = Math.Min(_options.MaxWords, words.Length - start);
            var text = string.Join(' ', words, start, length);

            chunks.Add(new DocumentChunk(
                document.Id.Trim(),
                document.Title.Trim(),
                chunkNumber,
                text));

            chunkNumber++;

            if (start + length >= words.Length)
            {
                break;
            }
        }

        return chunks;
    }
}

public sealed class InMemoryDocumentStore : IDocumentStore
{
    private readonly ConcurrentDictionary<string, StoredDocument> _documents =
        new(StringComparer.Ordinal);

    public int DocumentCount => _documents.Count;

    public bool TryAdd(StoredDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return _documents.TryAdd(document.Id, document);
    }

    public IReadOnlyList<DocumentChunk> GetAllChunks() =>
        _documents.Values
            .OrderBy(document => document.Id, StringComparer.Ordinal)
            .SelectMany(document => document.Chunks.OrderBy(chunk => chunk.Index))
            .ToArray();
}

public sealed class TokenSimilarityRetriever : IRetriever
{
    private readonly RetrievalOptions _options;

    public TokenSimilarityRetriever(RetrievalOptions? options = null)
    {
        _options = options ?? new RetrievalOptions();

        if (_options.MinimumScore is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "MinimumScore must be between zero and one.");
        }

        if (_options.CosineWeight < 0 ||
            _options.CoverageWeight < 0 ||
            Math.Abs(_options.CosineWeight + _options.CoverageWeight - 1) > 0.000001)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Similarity weights must be non-negative and sum to one.");
        }
    }

    public IReadOnlyList<RetrievalMatch> Retrieve(
        string question,
        IReadOnlyList<DocumentChunk> chunks,
        int maxResults)
    {
        ArgumentNullException.ThrowIfNull(chunks);

        if (maxResults < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxResults),
                "At least one result must be requested.");
        }

        var queryTokens = TextAnalysis.Tokenize(question);
        if (queryTokens.Count == 0 || chunks.Count == 0)
        {
            return [];
        }

        var queryCounts = TextAnalysis.CountTokens(queryTokens);
        var queryNorm = VectorNorm(queryCounts);
        var distinctQueryTerms = queryCounts.Keys.ToHashSet(StringComparer.Ordinal);
        var matches = new List<RetrievalMatch>();

        foreach (var chunk in chunks)
        {
            var chunkTokens = TextAnalysis.Tokenize(chunk.Text);
            if (chunkTokens.Count == 0)
            {
                continue;
            }

            var chunkCounts = TextAnalysis.CountTokens(chunkTokens);
            var matchedTerms = distinctQueryTerms
                .Where(chunkCounts.ContainsKey)
                .Order(StringComparer.Ordinal)
                .ToArray();

            if (matchedTerms.Length == 0)
            {
                continue;
            }

            var dotProduct = queryCounts.Sum(
                pair => pair.Value * chunkCounts.GetValueOrDefault(pair.Key));
            var cosine = dotProduct / (queryNorm * VectorNorm(chunkCounts));
            var coverage = (double)matchedTerms.Length / distinctQueryTerms.Count;
            var score = Math.Round(
                (_options.CosineWeight * cosine) +
                (_options.CoverageWeight * coverage),
                6);

            if (score >= _options.MinimumScore)
            {
                matches.Add(new RetrievalMatch(chunk, score, matchedTerms));
            }
        }

        return matches
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Chunk.DocumentId, StringComparer.Ordinal)
            .ThenBy(match => match.Chunk.Index)
            .Take(maxResults)
            .ToArray();
    }

    private static double VectorNorm(IReadOnlyDictionary<string, int> counts) =>
        Math.Sqrt(counts.Values.Sum(value => value * value));
}

internal static partial class TextAnalysis
{
    private static readonly HashSet<string> StopWords = new(
        [
            "a", "an", "and", "are", "as", "at", "be", "by", "can", "did",
            "do", "does", "for", "from", "had", "has", "have", "how", "i",
            "in", "is", "it", "its", "may", "of", "on", "or", "our", "that",
            "the", "their", "this", "to", "was", "were", "what", "when",
            "where", "which", "who", "why", "will", "with", "you", "your"
        ],
        StringComparer.Ordinal);

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return WordPattern()
            .Matches(text.ToLowerInvariant())
            .Select(match => match.Value)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToArray();
    }

    public static IReadOnlyDictionary<string, int> CountTokens(
        IEnumerable<string> tokens) =>
        tokens
            .GroupBy(token => token, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    public static IReadOnlyList<string> SplitClaims(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return SentenceBoundaryPattern()
            .Split(text.Trim())
            .Select(claim => claim.Trim())
            .Where(claim => claim.Length > 0)
            .ToArray();
    }

    public static string Normalize(string text) =>
        WhitespacePattern()
            .Replace(text.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();
}
