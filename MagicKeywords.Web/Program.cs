using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MagicKeywords.Web;
using MagicKeywords.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton<KeywordStore>();
builder.Services.AddSingleton<Favorites>();
builder.Services.AddSingleton<SimState>();

var host = builder.Build();
await host.Services.GetRequiredService<Favorites>().InitAsync();
await host.RunAsync();
