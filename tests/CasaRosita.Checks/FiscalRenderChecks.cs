using CasaRosita.Components.Pages;
using CasaRosita.Data;
using CasaRosita.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

static class FiscalRenderChecks
{
    public static async Task Run(IDbContextFactory<StoreDb> factory,Actor actor,FiscalOptions options,FiscalService fiscal,long id,Action<bool,string> check)
    {
        var services=new ServiceCollection();
        services.AddLogging();services.AddSingleton(factory);services.AddSingleton(actor);services.AddSingleton(options);services.AddSingleton(fiscal);services.AddSingleton<IJSRuntime,NoJs>();
        using var provider=services.BuildServiceProvider();
        await using var renderer=new HtmlRenderer(provider,provider.GetRequiredService<ILoggerFactory>());
        var html=await renderer.Dispatcher.InvokeAsync(async()=>
        {
            var output=await renderer.RenderComponentAsync<Invoice>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Id",id}}));
            return output.ToHtmlString();
        });
        check(html.Contains("CÓD. 011") && html.Contains("12345678901234") && html.Contains("data:image/png;base64,"),"Render de factura autorizada incluye código, CAE y QR");
        check(html.Contains("SIN VALIDEZ FISCAL") && html.Contains("Domicilio hist") && !html.Contains("Emitir prueba de factura C"),"Render de homologación conserva emisor histórico y no permite reemitir");
    }
    sealed class NoJs:IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>throw new NotSupportedException();
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)=>throw new NotSupportedException();
    }
}
