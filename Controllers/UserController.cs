using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Plugins;
using stage_api.Models;

namespace stage_api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly AuthenticationService _authenticationService;
       

        public UserController(AuthenticationService authenticationService)
        {
            _authenticationService = authenticationService;
        }


        //POST: api/user/login
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest loginRequest)
        {

            var token = await _authenticationService.AuthenticateUser(loginRequest.username, loginRequest.password);

            if (token == null)
            {
                return Unauthorized("Invalid username or password");
            }

            return Ok(new { Token = token });
        }


    }
}
