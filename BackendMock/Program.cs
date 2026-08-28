using BackendMock.Data;
using BackendMock.Models;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<KioskDbContext>(options =>
{
    options.UseSqlite("Data Source=kiosk.db");
});

var app = builder.Build();

const string TestToken = "kiosk-test-token-123";


// --------------------------------------------------
// Create database and add test tickets
// --------------------------------------------------

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<KioskDbContext>();

    db.Database.EnsureCreated();

    if (!db.Tickets.Any())
    {
        db.Tickets.AddRange(
            new Ticket
            {
                Code = "2660488771",
                IsActive = true
            },
            new Ticket
            {
                Code = "5555555555",
                IsActive = true
            },
            new Ticket
            {
                Code = "9999999999",
                IsActive = false
            });

        db.SaveChanges();
    }
}


// --------------------------------------------------
// Health
// --------------------------------------------------

app.MapGet("/health", () =>
{
    return Results.Ok(new
    {
        status = "ok"
    });
});


// --------------------------------------------------
// Ticket validation
// --------------------------------------------------

app.MapPost(
    "/api/tickets/validate",
    async (
        HttpRequest http,
        TicketRequest request,
        KioskDbContext db) =>
    {
        string authorization =
            http.Headers.Authorization.ToString();

        if (authorization != $"Bearer {TestToken}")
        {
            return Results.Unauthorized();
        }


        // Test timeout
        if (request.Code == "TIMEOUT")
        {
            await Task.Delay(10000);
        }


        // Bad request
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.BadRequest(new
            {
                error = "Ticket code is required"
            });
        }


        // Test server error
        if (request.Code == "SERVER_ERROR")
        {
            return Results.Problem(
                "Test server error",
                statusCode: 500);
        }


        // Real database query
        Ticket? ticket = await db.Tickets
            .FirstOrDefaultAsync(
                t => t.Code == request.Code);


        if (ticket == null)
        {
            return Results.Ok(
                new TicketResponse(
                    false,
                    "Ticket not found"));
        }


        if (!ticket.IsActive)
        {
            return Results.Ok(
                new TicketResponse(
                    false,
                    "Ticket is inactive"));
        }


        return Results.Ok(
            new TicketResponse(
                true,
                "Ticket is valid"));
    });


app.Run();


record TicketRequest(string Code);

record TicketResponse(
    bool Valid,
    string Message);