using Portfolio.Application.Common.Abstractions.Validation;

namespace Portfolio.Application.Features.PortfolioContent;

internal static class ContentValidation
{
    public static void RequiredText(
        ICollection<ValidationFailure> failures,
        string property,
        string? value,
        int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add(new(property, $"{property} is required."));
        }
        else if (value.Length > maximumLength)
        {
            failures.Add(new(property, $"{property} must not exceed {maximumLength} characters."));
        }
    }

    public static void OptionalText(
        ICollection<ValidationFailure> failures,
        string property,
        string? value,
        int maximumLength)
    {
        if (value?.Length > maximumLength)
        {
            failures.Add(new(property, $"{property} must not exceed {maximumLength} characters."));
        }
    }

    public static void DateRange(
        ICollection<ValidationFailure> failures,
        string property,
        DateOnly? start,
        DateOnly? end)
    {
        if (start.HasValue && end.HasValue && end < start)
        {
            failures.Add(new(property, "End date must not precede start date."));
        }
    }

    public static void DisplayOrder(ICollection<ValidationFailure> failures, int displayOrder)
    {
        if (displayOrder < 0)
        {
            failures.Add(new("displayOrder", "Display order must be greater than or equal to zero."));
        }
    }

    public static void HttpUrl(ICollection<ValidationFailure> failures, string property, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(new(property, $"{property} must be an absolute HTTP or HTTPS URL."));
        }
    }

    public static void ReorderItems(
        ICollection<ValidationFailure> failures,
        IReadOnlyCollection<ReorderItem> items)
    {
        if (items.Count == 0)
        {
            failures.Add(new("items", "At least one reorder item is required."));
            return;
        }

        if (items.Select(item => item.Id).Distinct().Count() != items.Count)
        {
            failures.Add(new("items", "Reorder item IDs must be unique."));
        }

        if (items.Any(item => item.DisplayOrder < 0))
        {
            failures.Add(new("items", "Display order must be greater than or equal to zero."));
        }
    }
}

public sealed record ReorderItem(Guid Id, int DisplayOrder);
