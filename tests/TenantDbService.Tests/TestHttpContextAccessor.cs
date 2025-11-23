using Microsoft.AspNetCore.Http;

namespace TenantDbService.Tests;

public class TestHttpContextAccessor : IHttpContextAccessor
{
    private HttpContext? _context;

    public HttpContext? HttpContext
    {
        get => _context;
        set => _context = value;
    }
}

