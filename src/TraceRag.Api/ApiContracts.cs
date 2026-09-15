using System.Text.RegularExpressions;

namespace TraceRag.Api;

public sealed record CreateDocumentRequest(
    string? Id,
    string? Title,
    string? Content);

public sealed record AskRequest(
    string? Question,
    int? MaxResults);

internal static partial class RequestValidation
{
    public const int MaximumDocumentCharacters = 250_000;
    public const int MaximumQuestionCharacters = 1_000;

    public static Dictionary<string, string[]> Validate(
        CreateDocumentRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (!string.IsNullOrWhiteSpace(request.Id) &&
            !DocumentIdPattern().IsMatch(request.Id.Trim()))
        {
            errors["id"] =
            [
                "ID must be 1-100 characters using letters, numbers, '.', '_', or '-'."
            ];
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors["title"] = ["Title is required."];
        }
        else if (request.Title.Trim().Length > 200)
        {
            errors["title"] = ["Title must be 200 characters or fewer."];
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            errors["content"] = ["Plain-text content is required."];
        }
        else if (request.Content.Length > MaximumDocumentCharacters)
        {
            errors["content"] =
            [
                $"Content must be {MaximumDocumentCharacters:N0} characters or fewer."
            ];
        }

        return errors;
    }

    public static Dictionary<string, string[]> Validate(AskRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Question))
        {
            errors["question"] = ["Question is required."];
        }
        else if (request.Question.Trim().Length > MaximumQuestionCharacters)
        {
            errors["question"] =
            [
                $"Question must be {MaximumQuestionCharacters:N0} characters or fewer."
            ];
        }

        if (request.MaxResults is < 1 or > 10)
        {
            errors["maxResults"] = ["MaxResults must be between 1 and 10."];
        }

        return errors;
    }

    [GeneratedRegex(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,99}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DocumentIdPattern();
}
