// Copyright (c) <YEAR> <COPYRIGHT HOLDER>
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using StrideAssetStore.App;
using StrideAssetStore.App.Services;
using StrideAssetStore.Core.Catalog;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

var baseAddress = new Uri(builder.HostEnvironment.BaseAddress);
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = baseAddress });

// Where to fetch the aggregated index. appsettings.json points this at the registry's raw URL; the
// fallback is the same constant the desktop app and the CLI use — the previous one pointed at a
// bundled copy that does not exist, so a missing key would have meant a 404 rather than the registry.
var indexUrl = builder.Configuration["Catalog:IndexUrl"] ?? CatalogDefaults.IndexUrl;
builder.Services.AddScoped<ICatalogSource>(sp =>
    new HttpCatalogSource(sp.GetRequiredService<HttpClient>(), new Uri(baseAddress, indexUrl)));

builder.Services.AddStrideAssetStoreUi(
    builder.Configuration.GetSection("Registry").Get<RegistryOptions>(),
    builder.Configuration.GetSection("App").Get<AppInfo>());

await builder.Build().RunAsync();
