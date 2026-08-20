using System;

namespace OnPaper.Common.Models.Utils;

public record ChatMessageModel(string UserId, string Text, DateTimeOffset SentAt);
