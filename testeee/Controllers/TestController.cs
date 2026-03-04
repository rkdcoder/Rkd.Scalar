using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace testeee.Controllers
{
    [ApiController]
    [ApiVersion("1.0")]
    [ApiVersion("3.1")]
    [Route("api/v{version:apiVersion}/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet("secure")]
        [MapToApiVersion("1.0")]
        public IActionResult SecureV1()
        {
            return Ok("v1 endpoint");
        }

        [HttpGet("secure")]
        [MapToApiVersion("3.1")]
        public IActionResult SecureV2()
        {
            return Ok("v2 endpoint");
        }
    }
}
