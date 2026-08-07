namespace OnPaper.Common.Models.Utils;

public class MazeModel
{
    public string HorizontalWalls { get; set; }  // Base64 строка
    public string VerticalWalls { get; set; }    // Base64 строка
    public int Width { get; set; }
    public int Height { get; set; }
    public int Treasure { get; set; }
}