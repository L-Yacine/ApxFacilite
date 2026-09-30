using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace APXEMI.Services;

/// <summary>
/// Verrou global : tant que la licence n'est pas activée (et que
/// License:Enabled est vrai), toute requête est redirigée vers l'écran
/// d'activation. Le contrôleur License reste toujours accessible.
/// </summary>
public sealed class LicenseGateFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        if (!configuration.GetValue<bool>("License:Enabled", false))
        {
            await next();
            return;
        }

        var controllerName = (context.ActionDescriptor as ControllerActionDescriptor)?.ControllerName;
        if (string.Equals(controllerName, "License", StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }

        var license = context.HttpContext.RequestServices.GetRequiredService<LicenseService>();
        if (!license.IsActivated)
        {
            context.Result = new RedirectToActionResult("Index", "License", null);
            return;
        }

        await next();
    }
}
