namespace OnPaper.Common.Models.Utils;

// Всё, что нужно клиенту, чтобы восстановить экран после перезагрузки страницы
public class GameSnapshotModel
{
    public int[] Size { get; set; } = [];

    public string OpponentNickname { get; set; } = string.Empty;

    // Лабиринт, который игрок уже сохранил. null - ещё рисует
    public MazeModel? MyMaze { get; set; }

    // Партия началась, когда лабиринты сохранили оба
    public bool IsStarted { get; set; }

    public int MyPosition { get; set; }
    public int OpponentPosition { get; set; }
    public bool YourTurn { get; set; }
    public int SecondsLeft { get; set; }

    // Стенки чужого лабиринта, которые игрок уже нашёл
    public WallHitModel[] DiscoveredWalls { get; set; } = [];

    // Мины и супер-мины чужого лабиринта, на которые игрок уже наступил
    public MineHitModel[] DiscoveredMines { get; set; } = [];

    // null - партия ещё идёт
    public bool? YouWon { get; set; }

    // Партия закончилась тем, что противник не вернулся
    public bool EndedByDisconnect { get; set; }

    // Сколько секунд осталось ждать отвалившегося противника. null - он на месте
    public int? OpponentLeftSeconds { get; set; }

    // История чата партии - нужна, чтобы её не терять при перезагрузке страницы
    public ChatMessageModel[] ChatMessages { get; set; } = [];
}
