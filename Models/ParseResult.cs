namespace PjskBundle2Parts.Models;

public sealed record ParseResult(
    bool IsSuccess,
    ConversionOptions? Options,
    string ErrorMessage
);
