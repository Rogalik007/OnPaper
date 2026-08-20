using System.ComponentModel.DataAnnotations.Schema;

namespace OnPaper.Models;

[Table("ratings")]
public class Rating
{
    [Column("id")]
    public int Id { get; set; }

    [Column("user_id")]
    public int UserId { get; set; }

    [Column("score")]
    public int Score { get; set; }
}
