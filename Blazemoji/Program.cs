using Blazemoji;
using Blazemoji.Components;
using Blazemoji.Contracts.Messages;
using Blazemoji.Services;
using Blazemoji.Services.Compiler;
using Blazemoji.Services.Execution;
using Blazemoji.Services.Library;
using Blazemoji.Shared.State;
using MassTransit;
using MudBlazor.Services;
using System.Text.Encodings.Web;
using System.Text.Unicode;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddMudServices();
builder.Services.AddBlazoredLocalStorage();
builder.Services.AddTransient<ILibraryService, LibraryService>();
builder.Services.AddTransient<ICompilerService, CompilerService>();
builder.Services.AddTransient<ICodeExecutionService, CodeExecutionService>();
builder.Services.AddTransient<ICodeRunner, CodeRunner>();
builder.Services.AddSingleton(new LocalStorageFiles());

//Register emojicode keyword implementations
var emojicodeKeywordTypes = typeof(EmojicodeKeyword).Assembly.GetTypes()
    .Where(t => t.IsSubclassOf(typeof(EmojicodeKeyword)));

foreach (var type in emojicodeKeywordTypes)
{
    builder.Services.AddSingleton(typeof(EmojicodeKeyword), type);
}

if(!builder.Environment.IsProduction())
{
    builder.Services.AddMassTransit(x =>
    {
        x.AddRequestClient<IExecuteCodeRequest>();

        x.UsingRabbitMq((context, cfg) =>
        {
            cfg.ConfigureJsonSerializerOptions(options =>
            {
                options.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All);
                options.WriteIndented = true;
                return options;
            });

            cfg.Host("localhost", "/", h => {
                {
                    h.Username("guest");
                    h.Password("guest");
                }
            });

            cfg.ConfigureEndpoints(context);
        });
    });
}

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
