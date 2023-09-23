using Microsoft.IdentityModel.Tokens;
using stage_api.Models;
using System.DirectoryServices.AccountManagement;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace stage_api
{
    public class AuthenticationService
    {

        public async Task<string> AuthenticateUser(string username, string password)
        {
            using (PrincipalContext context = new PrincipalContext(ContextType.Domain, "10.10.10.5"))
            {
                bool isValid = context.ValidateCredentials(username, password);

                if (isValid)
                {
                    var token = GenerateJwtToken(username);
                    return token;
                }

                return null;
            }
        }


        private string GenerateJwtToken(string username)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
          
            byte[] key = new byte[32];
            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(key);
            }

           

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new Claim[]
                {
                    new Claim(ClaimTypes.Name, username)
                }),

                Expires = DateTime.UtcNow.AddDays(1), // Token expiration time

                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }



    }
}

