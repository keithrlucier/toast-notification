using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ToastRevival.Api.Controllers;

/// <summary>
/// REL-H1: Global exception handler endpoint. Catches unhandled exceptions routed
/// via UseExceptionHandler("/error") and returns a consistent RFC 7807 Problem
/// Details response, preventing stack-trace leakage in production.
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class ErrorController : ControllerBase
{
    [Route("/error")]
    public IActionResult HandleError() => Problem();
}
