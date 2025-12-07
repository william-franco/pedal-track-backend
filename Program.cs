using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// ======================= DATABASE =======================
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlite("Data Source=pedaltrack.db"));

// ======================= SWAGGER =======================
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(opt =>
{
    opt.SwaggerDoc("v1", new OpenApiInfo { Title = "PedalTrack API", Version = "v1" });

    opt.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Use: Bearer {token}",
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });

    opt.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, new string[] { } }
    });
});

// ======================= CORS =======================
builder.Services.AddCors(o =>
{
    o.AddPolicy("AllowAll", b =>
        b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

// ======================= JWT =======================
var jwtKey = Encoding.ASCII.GetBytes("SuperSecretKeyForJwtTokenDontShare");

builder.Services.AddAuthentication(o =>
{
    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(o =>
{
    o.RequireHttpsMetadata = false;
    o.SaveToken = true;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(jwtKey),
        ValidateIssuer = false,
        ValidateAudience = false,
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

// ======================= MIGRATIONS + PRAGMA =======================
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.OpenConnection();
    db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
    db.Database.CloseConnection();
    db.Database.Migrate();
}

// ======================= PIPELINE =======================
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();

// ======================= API GROUP =======================
var api = app.MapGroup("/api");

// ======================= AUTH =======================
var auth = api.MapGroup("/auth");

auth.MapPost("/register", async (RegisterDto dto, AppDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Email == dto.Email))
        return Results.BadRequest("Email já cadastrado.");

    var user = new User
    {
        Name = dto.Name,
        Email = dto.Email,
        Password = BCrypt.Net.BCrypt.HashPassword(dto.Password),
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", user);
});

auth.MapPost("/login", async (LoginDto dto, AppDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
    if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.Password))
        return Results.Unauthorized();

    var tokenHandler = new JwtSecurityTokenHandler();
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email)
        }),
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(
            new SymmetricSecurityKey(jwtKey),
            SecurityAlgorithms.HmacSha256)
    };

    var token = tokenHandler.CreateToken(tokenDescriptor);

    return Results.Ok(new { AccessToken = tokenHandler.WriteToken(token) });
});

// ======================= PROTECTED =======================
var protectedApi = api.MapGroup("/").RequireAuthorization();

// ======================= BIKES (USER + RELAÇÕES) =======================
protectedApi.MapPost("/bikes", async (CreateBikeDto dto, ClaimsPrincipal claims, AppDbContext db) =>
{
    var userId = int.Parse(claims.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var bike = new Bike
    {
        Brand = dto.Brand,
        Model = dto.Model,
        UserId = userId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    db.Bikes.Add(bike);
    await db.SaveChangesAsync();

    var result = await db.Bikes
        .Include(b => b.User)
        .Include(b => b.UsageRecords)
        .Include(b => b.MaintenanceAlerts)
        .Include(b => b.MaintenanceChecklist)
        .Where(b => b.Id == bike.Id)
        .Select(b => new
        {
            id = b.Id,
            userId = b.UserId,
            user = new
            {
                b.User!.Id,
                b.User.Name,
                b.User.Email,
                b.User.CreatedAt,
                b.User.UpdatedAt
            },
            brand = b.Brand,
            model = b.Model,
            createdAt = b.CreatedAt,
            updatedAt = b.UpdatedAt,
            usageRecords = b.UsageRecords,
            maintenanceAlerts = b.MaintenanceAlerts,
            maintenanceChecklist = b.MaintenanceChecklist
        })
        .FirstAsync();

    return Results.Created($"/api/bikes/{bike.Id}", result);
});

protectedApi.MapGet("/bikes", async (ClaimsPrincipal claims, AppDbContext db) =>
{
    var userId = int.Parse(claims.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var bikes = await db.Bikes
        .AsNoTracking()
        .Where(b => b.UserId == userId)
        .Include(b => b.User)
        .Include(b => b.UsageRecords)
        .Include(b => b.MaintenanceAlerts)
        .Include(b => b.MaintenanceChecklist)
        .Select(b => new
        {
            id = b.Id,
            userId = b.UserId,
            user = new
            {
                b.User!.Id,
                b.User.Name,
                b.User.Email,
                b.User.CreatedAt,
                b.User.UpdatedAt
            },
            brand = b.Brand,
            model = b.Model,
            createdAt = b.CreatedAt,
            updatedAt = b.UpdatedAt,
            usageRecords = b.UsageRecords,
            maintenanceAlerts = b.MaintenanceAlerts,
            maintenanceChecklist = b.MaintenanceChecklist
        })
        .ToListAsync();

    return Results.Ok(bikes);
});

protectedApi.MapGet("/bikes/{id:int}", async (int id, ClaimsPrincipal claims, AppDbContext db) =>
{
    var userId = int.Parse(claims.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var bike = await db.Bikes
        .AsNoTracking()
        .Where(b => b.Id == id && b.UserId == userId)
        .Include(b => b.User)
        .Include(b => b.UsageRecords)
        .Include(b => b.MaintenanceAlerts)
        .Include(b => b.MaintenanceChecklist)
        .Select(b => new
        {
            id = b.Id,
            userId = b.UserId,
            user = new
            {
                b.User!.Id,
                b.User.Name,
                b.User.Email,
                b.User.CreatedAt,
                b.User.UpdatedAt
            },
            brand = b.Brand,
            model = b.Model,
            createdAt = b.CreatedAt,
            updatedAt = b.UpdatedAt,
            usageRecords = b.UsageRecords,
            maintenanceAlerts = b.MaintenanceAlerts,
            maintenanceChecklist = b.MaintenanceChecklist
        })
        .FirstOrDefaultAsync();

    if (bike == null)
        return Results.NotFound("Bike não encontrada.");

    return Results.Ok(bike);
});

// ======================= USAGE RECORDS =======================
protectedApi.MapPost("/usage-records", async (CreateUsageRecordDto dto, ClaimsPrincipal claims, AppDbContext db) =>
{
    var userId = int.Parse(claims.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var bike = await db.Bikes.FirstOrDefaultAsync(b => b.Id == dto.BikeId && b.UserId == userId);
    if (bike == null)
        return Results.BadRequest("Bike não encontrada.");

    var record = new UsageRecord
    {
        BikeId = dto.BikeId,
        KmTravelled = dto.KmTravelled,
        RecordedAt = DateTime.UtcNow,
        CreatedAt = DateTime.UtcNow
    };

    db.UsageRecords.Add(record);
    await db.SaveChangesAsync();

    return Results.Created($"/api/usage-records/{record.Id}", record);
});

// ======================= ALERTS =======================
protectedApi.MapPost("/maintenance-alerts", async (
    CreateMaintenanceAlertDto dto,
    ClaimsPrincipal claims,
    AppDbContext db) =>
{
    var userId = int.Parse(claims.FindFirstValue(ClaimTypes.NameIdentifier)!);

    var bike = await db.Bikes
        .FirstOrDefaultAsync(b => b.Id == dto.BikeId && b.UserId == userId);

    if (bike == null)
        return Results.BadRequest("Bike não encontrada.");

    var alert = new MaintenanceAlert
    {
        BikeId = dto.BikeId,
        Type = dto.Type,
        ThresholdValue = dto.ThresholdValue,
        Status = dto.Status,
        AlertTriggeredAt = dto.AlertTriggeredAt,
        CreatedAt = DateTime.UtcNow
    };

    db.MaintenanceAlerts.Add(alert);
    await db.SaveChangesAsync();

    return Results.Created($"/api/maintenance-alerts/{alert.Id}", alert);
});

// ======================= CHECKLIST =======================
protectedApi.MapPost("/maintenance-checklist", async (CreateMaintenanceChecklistDto dto, AppDbContext db) =>
{
    if (!await db.Bikes.AnyAsync(b => b.Id == dto.BikeId))
        return Results.BadRequest("Bike não encontrada.");

    var item = new MaintenanceChecklist
    {
        BikeId = dto.BikeId,
        Item = dto.Item,
        Status = "pendente",
        CreatedAt = DateTime.UtcNow
    };

    db.MaintenanceChecklists.Add(item);
    await db.SaveChangesAsync();

    return Results.Created($"/api/maintenance-checklist/{item.Id}", item);
});

app.Run();

// ======================= MODELS =======================
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<Bike> Bikes { get; set; } = new List<Bike>();
}

public class Bike
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Brand { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ICollection<UsageRecord> UsageRecords { get; set; } = new List<UsageRecord>();
    public ICollection<MaintenanceAlert> MaintenanceAlerts { get; set; } = new List<MaintenanceAlert>();
    public ICollection<MaintenanceChecklist> MaintenanceChecklist { get; set; } = new List<MaintenanceChecklist>();
}

public class UsageRecord
{
    public int Id { get; set; }
    public int BikeId { get; set; }
    public float KmTravelled { get; set; }
    public DateTime RecordedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MaintenanceAlert
{
    public int Id { get; set; }
    public int BikeId { get; set; }

    public Bike? Bike { get; set; }

    public string Type { get; set; } = "";
    public float ThresholdValue { get; set; }

    public string Status { get; set; } = "pendente";
    public DateTime AlertTriggeredAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MaintenanceChecklist
{
    public int Id { get; set; }
    public int BikeId { get; set; }
    public string Item { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

// ======================= DTOs =======================
public record RegisterDto(string Name, string Email, string Password);
public record LoginDto(string Email, string Password);
public record CreateBikeDto(string Brand, string Model);
public record CreateUsageRecordDto(int BikeId, float KmTravelled);

public record CreateMaintenanceAlertDto(
    int BikeId,
    string Type,
    float ThresholdValue,
    string Status,
    DateTime AlertTriggeredAt
);

public record CreateMaintenanceChecklistDto(int BikeId, string Item);

// ======================= DB CONTEXT =======================
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Bike> Bikes => Set<Bike>();
    public DbSet<UsageRecord> UsageRecords => Set<UsageRecord>();
    public DbSet<MaintenanceAlert> MaintenanceAlerts => Set<MaintenanceAlert>();
    public DbSet<MaintenanceChecklist> MaintenanceChecklists => Set<MaintenanceChecklist>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>()
            .HasMany(u => u.Bikes)
            .WithOne(b => b.User!)
            .HasForeignKey(b => b.UserId);

        modelBuilder.Entity<Bike>()
            .HasMany(b => b.UsageRecords)
            .WithOne()
            .HasForeignKey(u => u.BikeId);

        modelBuilder.Entity<Bike>()
            .HasMany(b => b.MaintenanceAlerts)
            .WithOne()
            .HasForeignKey(m => m.BikeId);

        modelBuilder.Entity<Bike>()
            .HasMany(b => b.MaintenanceChecklist)
            .WithOne()
            .HasForeignKey(c => c.BikeId);
    }
}
