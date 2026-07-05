namespace OnPaper.Common.Models.Utils;

public class AuthModel
{
    public string Token { get; set; }
    public int Id { get; set; }

    public AuthModel(string token, int id)
    {
        Token = token;
        Id = id;
    }
}