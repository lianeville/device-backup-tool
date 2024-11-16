using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace DeviceManagerApi.Controllers
{
	[Route("api/[controller]")]
	[ApiController]
	public class AuthController : ControllerBase
	{
		private readonly IConfiguration _configuration;

		public AuthController(IConfiguration configuration)
		{
			_configuration = configuration;
		}

		[HttpPost("login")]
		public IActionResult Login([FromBody] UserLogin model)
		{
			if (model.Username != "admin" || model.Password != "password123")
			{
				return Unauthorized();
			}

			var claims = new[]
			{
				new Claim(ClaimTypes.Name, model.Username),
				new Claim(ClaimTypes.Role, "Admin")
			};

			var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET_KEY")));
			var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

			var token = new JwtSecurityToken(
				 issuer: Environment.GetEnvironmentVariable("JWT_ISSUER"),
				 audience: Environment.GetEnvironmentVariable("JWT_AUDIENCE"),
				 claims: claims,
				 expires: DateTime.Now.AddMinutes(9000),
				 signingCredentials: creds);

			return Ok(new
			{
				token = new JwtSecurityTokenHandler().WriteToken(token)
			});
		}
	}

	public class UserLogin
	{
		public string? Username { get; set; }
		public string? Password { get; set; }
	}
}
