using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Azure.Identity;
using Dapper;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---------- configuration & secrets ----------
// Azure: secrets come from Key Vault via managed identity. Local: environment variables from .env.
if (builder.Configuration["KeyVaultUri"] is { Length: > 0 } kv)
    builder.Configuration.AddAzureKeyVault(new Uri(kv), new DefaultAzureCredential());

var apiKey = builder.Configuration["Auth:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Length < 32)
    throw new InvalidOperationException("Auth:ApiKey must be configured and at least 32 characters");
var apiKeyBytes = Encoding.UTF8.GetBytes(apiKey);

// ---------- services ----------
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ASX Sector Portfolio API",
        Version = "v1",
        Description = "Historical ASX top-100 data (2000-2020), sector statistics and portfolio optimisation. " +
                      "All /api endpoints require the X-Api-Key header. Results are historical, not forecasts."
    });
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = "X-Api-Key",
        Description = "API key issued to the client"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" } },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddFixedWindowLimiter("per-minute", opt =>
    {
        opt.PermitLimit = 120;
        opt.Window = TimeSpan.FromMinutes(1);
        opt.QueueLimit = 0;
        opt.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
    });
});

builder.Services.AddHttpClient<OptimiserClient>(c =>
{
    c.BaseAddress = new Uri(builder.Configuration["Optimiser:BaseUrl"]
        ?? throw new InvalidOperationException("Optimiser:BaseUrl not configured"));
    c.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddSingleton<ReturnsRepository>();

var app = builder.Build();

// ---------- pipeline ----------
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseRateLimiter();

// API-key check for everything under /api (constant-time comparison)
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        var supplied = Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
        if (!CryptographicOperations.FixedTimeEquals(supplied, apiKeyBytes))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsJsonAsync(new { error = "missing or invalid X-Api-Key header" });
            return;
        }
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithTags("System").WithSummary("Liveness check (no key needed)");

var api = app.MapGroup("/api").RequireRateLimiting("per-minute");

// ---- reference data ----
api.MapGet("/sectors", async (ReturnsRepository repo) => Results.Ok(await repo.GetSectorsAsync()))
   .WithTags("Reference").WithSummary("List all sectors");

api.MapGet("/companies", async (string? sector, ReturnsRepository repo) =>
        Results.Ok(await repo.GetCompaniesAsync(sector)))
   .WithTags("Reference").WithSummary("List companies, optionally filtered by sector");

api.MapGet("/companies/{ticker}", async (string ticker, DateOnly? from, DateOnly? to,
        ReturnsRepository repo, CancellationToken ct) =>
{
    if (!Validation.IsTicker(ticker)) return Results.BadRequest(new { error = "ticker must be 2-6 letters/digits" });
    var company = await repo.GetCompanyAsync(ticker.ToUpperInvariant());
    if (company is null) return Results.NotFound();
    var series = await repo.GetTickerSeriesAsync(company.Ticker, from, to, ct);
    return Results.Ok(new CompanyDetail(company, Window(series), PerformanceStats.Compute(series.Select(s => s.Ret).ToList())));
})
   .WithTags("Companies").WithSummary("Company details and performance statistics");

api.MapGet("/companies/{ticker}/returns", async (string ticker, DateOnly? from, DateOnly? to,
        ReturnsRepository repo, CancellationToken ct) =>
{
    if (!Validation.IsTicker(ticker)) return Results.BadRequest(new { error = "ticker must be 2-6 letters/digits" });
    var series = await repo.GetTickerSeriesAsync(ticker.ToUpperInvariant(), from, to, ct);
    return series.Count == 0
        ? Results.NotFound()
        : Results.Ok(series.Select(s => new ReturnPoint(DateOnly.FromDateTime(s.TradeDate), s.Ret)));
})
   .WithTags("Companies").WithSummary("Daily cleaned returns for one company");

// ---- sectors ----
api.MapGet("/sectors/{sector}/tickers", async (string sector, ReturnsRepository repo) =>
{
    var tickers = await repo.GetTickersAsync(sector);
    return tickers.Count == 0 ? Results.NotFound() : Results.Ok(tickers);
})
   .WithTags("Sectors").WithSummary("Tickers in a sector");

api.MapGet("/sectors/{sector}/stats", async (string sector, DateOnly? from, DateOnly? to,
        ReturnsRepository repo, CancellationToken ct) =>
{
    var series = await repo.GetSectorSeriesAsync(sector, from, to, ct);
    if (series.Count == 0) return Results.NotFound();
    return Results.Ok(new SectorStats(sector, "equal-weight", Window(series),
        PerformanceStats.Compute(series.Select(s => s.Ret).ToList())));
})
   .WithTags("Sectors").WithSummary("Equal-weight sector portfolio statistics");

// ---- portfolios ----
api.MapPost("/portfolios/optimise", async (OptimiseCommand cmd, ReturnsRepository repo,
    OptimiserClient optimiser, CancellationToken ct) =>
{
    var errors = new List<ValidationResult>();
    if (!Validator.TryValidateObject(cmd, new ValidationContext(cmd), errors, true))
        return Results.ValidationProblem(new Dictionary<string, string[]>
            { ["request"] = errors.Select(e => e.ErrorMessage ?? "invalid").ToArray() });
    if (cmd.From >= cmd.To) return Results.BadRequest(new { error = "from must be before to" });
    if (cmd.Tickers is { Count: > 100 }) return Results.BadRequest(new { error = "at most 100 tickers" });
    if (cmd.Tickers is { Count: > 0 } && cmd.Tickers.Any(t => !Validation.IsTicker(t)))
        return Results.BadRequest(new { error = "invalid ticker in list" });

    var tickers = cmd.Tickers is { Count: > 0 }
        ? cmd.Tickers.Select(t => t.ToUpperInvariant()).Distinct().ToList()
        : await repo.GetTickersAsync(cmd.Sector ?? "");
    if (tickers.Count < 2) return Results.BadRequest(new { error = "need a valid sector or at least two tickers" });
    if (cmd.MaxWeight * tickers.Count < 1)
        return Results.BadRequest(new { error = $"maxWeight must be at least {Math.Ceiling(100.0 / tickers.Count) / 100:0.00} for {tickers.Count} stocks" });

    var (cols, rows) = await repo.GetAlignedReturnsAsync(tickers, cmd.From, cmd.To, ct);
    if (rows.Count < 60) return Results.BadRequest(new { error = $"only {rows.Count} days where all stocks have data; widen the window" });

    var result = await optimiser.OptimiseAsync(
        new OptimiserRequest(cols, rows, cmd.Method, cmd.MaxWeight, cmd.RiskFreeRate), ct);
    return Results.Ok(result);
})
   .WithTags("Portfolios").WithSummary("Optimal weights for a sector or ticker list");

app.Run();

static DataWindow? Window(IReadOnlyList<(DateTime TradeDate, double Ret)> s) =>
    s.Count == 0 ? null : new DataWindow(DateOnly.FromDateTime(s[0].TradeDate), DateOnly.FromDateTime(s[^1].TradeDate));

// ---------- contracts ----------
public record OptimiseCommand(
    string? Sector,
    List<string>? Tickers,
    DateOnly From,
    DateOnly To,
    [property: RegularExpression("min_variance|max_sharpe|risk_parity")] string Method = "min_variance",
    [property: Range(0.01, 1.0)] double MaxWeight = 1.0,
    [property: Range(0.0, 0.2)] double RiskFreeRate = 0.0);

public record Company(string Ticker, string Name, string Sector);
public record ReturnPoint(DateOnly Date, double Return);
public record DataWindow(DateOnly From, DateOnly To);
public record StatsResult(int Days, double AnnualReturn, double AnnualVolatility, double? ReturnToRisk,
    double MaxDrawdown, double TotalReturn);
public record CompanyDetail(Company Company, DataWindow? Window, StatsResult? Stats);
public record SectorStats(string Sector, string Weighting, DataWindow? Window, StatsResult? Stats);

public record OptimiserRequest(
    [property: System.Text.Json.Serialization.JsonPropertyName("tickers")] IReadOnlyList<string> Tickers,
    [property: System.Text.Json.Serialization.JsonPropertyName("returns")] IReadOnlyList<double[]> Returns,
    [property: System.Text.Json.Serialization.JsonPropertyName("method")] string Method,
    [property: System.Text.Json.Serialization.JsonPropertyName("max_weight")] double MaxWeight,
    [property: System.Text.Json.Serialization.JsonPropertyName("risk_free_rate")] double RiskFreeRate);

public record OptimiserResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("weights")] Dictionary<string, double> Weights,
    [property: System.Text.Json.Serialization.JsonPropertyName("expected_return")] double ExpectedReturn,
    [property: System.Text.Json.Serialization.JsonPropertyName("volatility")] double Volatility,
    [property: System.Text.Json.Serialization.JsonPropertyName("sharpe")] double? Sharpe);

// ---------- domain logic ----------
public static partial class Validation
{
    [GeneratedRegex("^[A-Za-z0-9]{2,6}$")]
    private static partial Regex TickerRegex();
    public static bool IsTicker(string t) => TickerRegex().IsMatch(t);
}

public static class PerformanceStats
{
    private const int TradingDays = 252;

    /// Annualised geometric return, annualised volatility, return/risk, max drawdown, total return.
    public static StatsResult? Compute(IReadOnlyList<double> r)
    {
        if (r.Count < 2) return null;
        double growth = 1, peak = 1, maxDd = 0, sum = 0;
        foreach (var x in r)
        {
            growth *= 1 + x;
            peak = Math.Max(peak, growth);
            maxDd = Math.Min(maxDd, growth / peak - 1);
            sum += x;
        }
        var mean = sum / r.Count;
        var variance = r.Sum(x => (x - mean) * (x - mean)) / (r.Count - 1);
        var annRet = Math.Pow(growth, (double)TradingDays / r.Count) - 1;
        var annVol = Math.Sqrt(variance * TradingDays);
        double? ratio = annVol > 0 ? Math.Round(annRet / annVol, 3) : null;
        return new StatsResult(r.Count, Math.Round(annRet, 4), Math.Round(annVol, 4), ratio,
            Math.Round(maxDd, 4), Math.Round(growth - 1, 4));
    }
}

// ---------- infrastructure ----------
public sealed class OptimiserClient(HttpClient http)
{
    public async Task<OptimiserResponse> OptimiseAsync(OptimiserRequest req, CancellationToken ct)
    {
        var resp = await http.PostAsJsonAsync("/optimise", req, ct);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<OptimiserResponse>(cancellationToken: ct))!;
    }
}

public sealed class ReturnsRepository(IConfiguration config)
{
    private const string DateFilter =
        "TradeDate >= COALESCE(@from, '1900-01-01') AND TradeDate <= COALESCE(@to, '9999-12-31')";

    // Retry transient Azure SQL errors (e.g. 40613 while a serverless database resumes)
    private static readonly SqlRetryLogicBaseProvider Retry =
        SqlConfigurableRetryFactory.CreateExponentialRetryProvider(new SqlRetryLogicOption
        {
            NumberOfTries = 6,
            DeltaTime = TimeSpan.FromSeconds(2),
            MaxTimeInterval = TimeSpan.FromSeconds(20)
        });

    private SqlConnection Open() => new(config.GetConnectionString("Sql")
        ?? throw new InvalidOperationException("ConnectionStrings:Sql not configured"))
    { RetryLogicProvider = Retry };

    private static object DateRange(DateOnly? from, DateOnly? to) => new
    {
        from = from?.ToDateTime(TimeOnly.MinValue),
        to = to?.ToDateTime(TimeOnly.MinValue)
    };

    public async Task<IReadOnlyList<string>> GetSectorsAsync()
    {
        await using var c = Open();
        return (await c.QueryAsync<string>("SELECT DISTINCT Sector FROM dbo.Company ORDER BY Sector")).ToList();
    }

    public async Task<IReadOnlyList<Company>> GetCompaniesAsync(string? sector)
    {
        await using var c = Open();
        return (await c.QueryAsync<Company>(
            "SELECT Ticker, Name, Sector FROM dbo.Company WHERE (@sector IS NULL OR Sector = @sector) ORDER BY Ticker",
            new { sector })).ToList();
    }

    public async Task<Company?> GetCompanyAsync(string ticker)
    {
        await using var c = Open();
        return await c.QuerySingleOrDefaultAsync<Company>(
            "SELECT Ticker, Name, Sector FROM dbo.Company WHERE Ticker = @ticker", new { ticker });
    }

    public async Task<IReadOnlyList<string>> GetTickersAsync(string sector)
    {
        await using var c = Open();
        return (await c.QueryAsync<string>(
            "SELECT Ticker FROM dbo.Company WHERE Sector = @sector ORDER BY Ticker", new { sector })).ToList();
    }

    public async Task<IReadOnlyList<(DateTime TradeDate, double Ret)>> GetTickerSeriesAsync(
        string ticker, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await using var c = Open();
        var p = new DynamicParameters(DateRange(from, to));
        p.Add("ticker", ticker);
        return (await c.QueryAsync<(DateTime TradeDate, double Ret)>(new CommandDefinition(
            $"SELECT TradeDate, Ret FROM dbo.DailyReturn WHERE Ticker = @ticker AND {DateFilter} ORDER BY TradeDate",
            p, cancellationToken: ct))).ToList();
    }

    /// Equal-weight sector series: each day, the average return of the sector's stocks that traded.
    public async Task<IReadOnlyList<(DateTime TradeDate, double Ret)>> GetSectorSeriesAsync(
        string sector, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await using var c = Open();
        var p = new DynamicParameters(DateRange(from, to));
        p.Add("sector", sector);
        return (await c.QueryAsync<(DateTime TradeDate, double Ret)>(new CommandDefinition(
            $"""
            SELECT d.TradeDate, AVG(d.Ret) AS Ret
            FROM dbo.DailyReturn d JOIN dbo.Company co ON co.Ticker = d.Ticker
            WHERE co.Sector = @sector AND {DateFilter}
            GROUP BY d.TradeDate ORDER BY d.TradeDate
            """, p, cancellationToken: ct))).ToList();
    }

    /// Only dates where every ticker has a return (complete-case alignment).
    public async Task<(IReadOnlyList<string> Cols, List<double[]> Rows)> GetAlignedReturnsAsync(
        IReadOnlyList<string> tickers, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await using var c = Open();
        var data = await c.QueryAsync<(string Ticker, DateTime TradeDate, double Ret)>(new CommandDefinition(
            "SELECT Ticker, TradeDate, Ret FROM dbo.DailyReturn WHERE Ticker IN @tickers AND TradeDate BETWEEN @from AND @to",
            new { tickers, from = from.ToDateTime(TimeOnly.MinValue), to = to.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: ct));

        var cols = tickers.OrderBy(t => t).ToList();
        var index = cols.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => x.i);
        var rows = data.GroupBy(d => d.TradeDate)
            .Where(g => g.Count() == cols.Count)
            .OrderBy(g => g.Key)
            .Select(g => { var r = new double[cols.Count]; foreach (var d in g) r[index[d.Ticker]] = d.Ret; return r; })
            .ToList();
        return (cols, rows);
    }
}

public partial class Program;
