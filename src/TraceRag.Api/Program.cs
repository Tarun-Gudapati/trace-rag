using TraceRag.Api;
using TraceRag.Core;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<ITextChunker, DeterministicTextChunker>();
builder.Services.AddSingleton<IDocumentStore, InMemoryDocumentStore>();
builder.Services.AddSingleton<IRetriever, TokenSimilarityRetriever>();
builder.Services.AddSingleton<IAnswerGenerator, ExtractiveAnswerGenerator>();
builder.Services.AddSingleton<IAnswerEvaluator, StrictAnswerEvaluator>();
builder.Services.AddSingleton<ITraceRagPipeline, TraceRagPipeline>();

var app = builder.Build();

app.MapOpenApi();

app.MapGet(
        "/health",
        (IDocumentStore store) => Results.Ok(
            new
            {
                status = "healthy",
                storage = "in-memory",
                indexedDocuments = store.DocumentCount
            }))
    .WithName("GetHealth")
    .WithSummary("Check service health")
    .Produces(StatusCodes.Status200OK);

app.MapPost(
        "/api/documents",
        (CreateDocumentRequest request, ITraceRagPipeline pipeline) =>
        {
            var errors = RequestValidation.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var id = string.IsNullOrWhiteSpace(request.Id)
                ? Guid.NewGuid().ToString("N")
                : request.Id.Trim();

            try
            {
                var receipt = pipeline.Ingest(
                    new DocumentInput(
                        id,
                        request.Title!.Trim(),
                        request.Content!));

                return Results.Json(
                    receipt,
                    statusCode: StatusCodes.Status201Created);
            }
            catch (DuplicateDocumentException exception)
            {
                return Results.Problem(
                    title: "Duplicate document ID",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status409Conflict);
            }
        })
    .WithName("CreateDocument")
    .WithSummary("Ingest one plain-text document")
    .Accepts<CreateDocumentRequest>("application/json")
    .Produces<DocumentReceipt>(StatusCodes.Status201Created)
    .ProducesValidationProblem()
    .ProducesProblem(StatusCodes.Status409Conflict);

app.MapPost(
        "/api/ask",
        (AskRequest request, ITraceRagPipeline pipeline) =>
        {
            var errors = RequestValidation.Validate(request);
            if (errors.Count > 0)
            {
                return Results.ValidationProblem(errors);
            }

            var result = pipeline.Ask(
                request.Question!.Trim(),
                request.MaxResults ?? 5);

            return Results.Ok(result);
        })
    .WithName("AskQuestion")
    .WithSummary("Retrieve evidence and return an extractive answer")
    .Accepts<AskRequest>("application/json")
    .Produces<AskResult>(StatusCodes.Status200OK)
    .ProducesValidationProblem();

app.Run();

public partial class Program;
