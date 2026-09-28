namespace OnPaper.Common.Models.Utils;

// ConnectionId нужен, чтобы адресовать игрока в комнате; Position - его текущая клетка.
public record RacePlayerModel(string UserId, string ConnectionId, int Position, string Nickname);

public class RaceRoomModel
{
    public string GameId { get; set; } = string.Empty;
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public RacePlayerModel[] Players { get; set; } = [];
}

public record RacePlayerPositionModel(string UserId, int Position, string Nickname);

// Ответ GetRaceState: снимок комнаты для игрока, который в неё зашёл (в том числе после перезагрузки)
public class RaceSnapshotModel
{
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public int MyPosition { get; set; }
    public List<RacePlayerPositionModel> Players { get; set; } = [];
}
