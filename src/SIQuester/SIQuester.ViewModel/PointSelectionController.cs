using System.Globalization;

namespace SIQuester.ViewModel;

/// <summary>
/// Owns framework-neutral editing and layout calculations for an image point answer.
/// </summary>
public sealed class PointSelectionController : ModelViewBase
{
    private string _currentAnswer;
    private double _currentDeviation;

    public string CurrentAnswer
    {
        get => _currentAnswer;
        private set
        {
            if (_currentAnswer != value)
            {
                _currentAnswer = value;
                OnPropertyChanged();
            }
        }
    }

    public double CurrentDeviation
    {
        get => _currentDeviation;
        set
        {
            var normalized = NormalizeDeviation(value);

            if (Math.Abs(_currentDeviation - normalized) > double.Epsilon)
            {
                _currentDeviation = normalized;
                OnPropertyChanged();
            }
        }
    }

    public PointSelectionController(string answer, double deviation)
    {
        _currentAnswer = answer;
        _currentDeviation = NormalizeDeviation(deviation);
    }

    public bool SelectFromViewport(
        double pointerX,
        double pointerY,
        double viewportWidth,
        double viewportHeight,
        int pixelWidth,
        int pixelHeight)
    {
        if (!double.IsFinite(pointerX)
            || !double.IsFinite(pointerY)
            || !TryGetImageBounds(viewportWidth, viewportHeight, pixelWidth, pixelHeight, out var imageBounds))
        {
            return false;
        }

        var x = Math.Clamp((pointerX - imageBounds.Left) / imageBounds.Width, 0.0, 1.0);
        var y = Math.Clamp((pointerY - imageBounds.Top) / imageBounds.Height, 0.0, 1.0);
        SetSelection(x, y, pixelWidth, pixelHeight);
        return true;
    }

    public bool NudgeSelection(double deltaX, double deltaY, int pixelWidth, int pixelHeight)
    {
        if (!double.IsFinite(deltaX)
            || !double.IsFinite(deltaY)
            || pixelWidth <= 0
            || pixelHeight <= 0)
        {
            return false;
        }

        var selection = TryParseAnswer(_currentAnswer, out var current)
            ? current
            : new PointSelection(0.5, 0.5);
        SetSelection(
            Math.Clamp(selection.X + deltaX, 0.0, 1.0),
            Math.Clamp(selection.Y + deltaY, 0.0, 1.0),
            pixelWidth,
            pixelHeight);
        return true;
    }

    public bool TryGetVisualLayout(
        double viewportWidth,
        double viewportHeight,
        int pixelWidth,
        int pixelHeight,
        out PointSelectionVisualLayout layout)
    {
        layout = default;

        if (!TryParseAnswer(_currentAnswer, out var selection)
            || !TryGetImageBounds(viewportWidth, viewportHeight, pixelWidth, pixelHeight, out var imageBounds))
        {
            return false;
        }

        var markerX = imageBounds.Left + Math.Clamp(selection.X, 0.0, 1.0) * imageBounds.Width;
        var markerY = imageBounds.Top + Math.Clamp(selection.Y, 0.0, 1.0) * imageBounds.Height;
        var radius = _currentDeviation * Math.Min(imageBounds.Width, imageBounds.Height);
        layout = new PointSelectionVisualLayout(markerX, markerY, radius);
        return true;
    }

    public void ApplyTo(PointAnswerViewModel target)
    {
        target.Answer = _currentAnswer;
        target.Deviation = Math.Round(_currentDeviation, 2);
    }

    private static bool TryParseAnswer(string answer, out PointSelection selection)
    {
        selection = default;
        var parts = answer.Split(',');

        if (parts.Length < 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
            || !double.IsFinite(x)
            || !double.IsFinite(y))
        {
            return false;
        }

        selection = new PointSelection(x, y);
        return true;
    }

    private static bool TryGetImageBounds(
        double viewportWidth,
        double viewportHeight,
        int pixelWidth,
        int pixelHeight,
        out PointSelectionImageBounds bounds)
    {
        bounds = default;

        if (!double.IsFinite(viewportWidth)
            || !double.IsFinite(viewportHeight)
            || viewportWidth <= 0
            || viewportHeight <= 0
            || pixelWidth <= 0
            || pixelHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(viewportWidth / pixelWidth, viewportHeight / pixelHeight);
        var imageWidth = pixelWidth * scale;
        var imageHeight = pixelHeight * scale;
        bounds = new PointSelectionImageBounds(
            (viewportWidth - imageWidth) / 2,
            (viewportHeight - imageHeight) / 2,
            imageWidth,
            imageHeight);
        return true;
    }

    private static double NormalizeDeviation(double value)
    {
        if (double.IsNaN(value))
        {
            return 0.0;
        }

        return Math.Clamp(value, 0.0, 0.5);
    }

    private void SetSelection(double x, double y, int pixelWidth, int pixelHeight)
    {
        var aspectRatio = Math.Round((double)pixelWidth / pixelHeight, 2);
        CurrentAnswer = string.Join(",",
            Math.Round(x, 2).ToString(CultureInfo.InvariantCulture),
            Math.Round(y, 2).ToString(CultureInfo.InvariantCulture),
            aspectRatio.ToString(CultureInfo.InvariantCulture));
    }

    private readonly record struct PointSelection(double X, double Y);

    private readonly record struct PointSelectionImageBounds(double Left, double Top, double Width, double Height);
}

public readonly record struct PointSelectionVisualLayout(double MarkerX, double MarkerY, double Radius);
