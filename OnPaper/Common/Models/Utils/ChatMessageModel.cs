using System;

namespace OnPaper.Common.Models.Utils;

public record ChatMessageModel(string UserId, string Nickname, string Text, DateTimeOffset SentAt);
