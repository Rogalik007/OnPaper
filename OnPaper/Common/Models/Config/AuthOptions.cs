using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace OnPaper.Common.Models.Config;

public static class AuthOptions
{
    public static string ISSUER = "OnPaper";
    public static string AUDIENCE = "GameClient";
    public static string KEY = null!;
    public static SymmetricSecurityKey GetSymmetricSecurityKey()
    {
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(KEY));
    }
}
