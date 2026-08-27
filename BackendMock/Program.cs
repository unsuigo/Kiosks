var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok"
    });
});

app.MapPost("/api/tickets/validate", (TicketRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Code))
    {
        return Results.BadRequest(new
        {
            error = "Ticket code is required"
        });
    }

    if (request.Code == "SERVER_ERROR")
    {
        return Results.Problem(
            "Test server error",
            statusCode: 500);
    }

    bool isValid = request.Code == "2660488771";

    return Results.Ok(new TicketResponse(
        isValid,
        isValid ? "Ticket is valid" : "Ticket not found"
    ));
});

app.Run();

record TicketRequest(string Code);

record TicketResponse(
    bool Valid,
    string Message
);