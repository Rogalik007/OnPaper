using Microsoft.AspNetCore.Mvc;
using OnPaper.Common.Enums;

namespace OnPaper.Common.Models.Utils;
public class ResultModel
{
    public static ResultModel<T> Ok<T>(T value)
        => new(value);

    public static ResultModel<T> Fail<T>(string error, ErrorType errorType)
        => new(error, errorType);
}

public class ResultModel<T>
{
    public T? Value { get; set; }
    public ResultError? Error { get; set; }
    public bool IsSuccess => Error is null;

    internal ResultModel(T value)
    {
        Value = value;
    }

    internal ResultModel(string error, ErrorType errorType)
    {
        Error = new ResultError(error, errorType);
    }
}

public record ResultError(string Message, ErrorType Type);