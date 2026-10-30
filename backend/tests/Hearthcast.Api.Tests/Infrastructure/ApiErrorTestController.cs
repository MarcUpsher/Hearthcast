using Microsoft.AspNetCore.Mvc;

namespace Hearthcast.Api.Tests.Infrastructure;

// Discovered only by the test factory's explicit application part; never ships in the API.
[ApiController]
[Route("_tests/errors")]
public sealed class ApiErrorTestController : ControllerBase
{
    [HttpGet]
    public IActionResult Unexpected() => throw new InvalidOperationException("Synthetic private database detail");

    [HttpGet("argument")]
    public IActionResult ArgumentFailure() => throw new ArgumentException("Synthetic private argument detail");

    [HttpGet("key")]
    public IActionResult LookupFailure() => throw new KeyNotFoundException("Synthetic private lookup detail");
}
