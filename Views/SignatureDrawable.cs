using System.Globalization;
using System.Text;

namespace MkalInventoryApp.Views;

/// <summary>Collects touch/mouse strokes so separate strokes are not joined by stray lines.</summary>
public class SignatureDrawable : IDrawable
{
    private readonly List<List<PointF>> _strokes = new();

    public bool HasInk => _strokes.Any(s => s.Count > 1);

    public void BeginStroke(PointF p) => _strokes.Add(new List<PointF> { p });

    public void AddPoint(PointF p)
    {
        if (_strokes.Count == 0) BeginStroke(p);
        else _strokes[^1].Add(p);
    }

    public void Clear() => _strokes.Clear();

    /// <summary>Compact text form "x,y;x,y|x,y;..." stored with the loan record.</summary>
    public string ToData()
    {
        var sb = new StringBuilder();
        foreach (var stroke in _strokes.Where(s => s.Count > 1))
        {
            if (sb.Length > 0) sb.Append('|');
            sb.Append(string.Join(';', stroke.Select(p =>
                string.Create(CultureInfo.InvariantCulture, $"{p.X:0.#},{p.Y:0.#}"))));
        }
        return sb.ToString();
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.StrokeColor = Colors.Black;
        canvas.StrokeSize = 3;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        foreach (var stroke in _strokes)
        {
            if (stroke.Count < 2) continue;
            var path = new PathF(stroke[0]);
            for (int i = 1; i < stroke.Count; i++) path.LineTo(stroke[i]);
            canvas.DrawPath(path);
        }
    }
}
