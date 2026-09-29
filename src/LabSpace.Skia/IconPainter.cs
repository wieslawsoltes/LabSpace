using SkiaSharp;

namespace LabSpace.Skia;

/// <summary>Original vector toolbar iconography, independent of system symbol fonts.</summary>
public static class IconPainter
{
    public static void Draw(SKCanvas c, string name, SKRect r, LabDrawing d, bool active = false)
    {
        c.Save(); c.Translate(r.Left, r.Top); c.Scale(r.Width / 24, r.Height / 24); var ink = LabDrawing.Color("#424242");
        switch (name)
        {
            case "frames":
                d.Rect(c, 3, 6, 16, 15, "#FFFFFF"); d.Border(c, new(3, 6, 19, 21), ink);
                d.Line(c, 7, 3, 22, 3, ink); d.Line(c, 22, 3, 22, 17, ink); d.Rect(c, 4, 7, 14, 3, "#C5D6E5");
                d.Text(c, "0", 11, 18, 10, "#424242", true); break;
            case "formula":
                d.Rect(c, 2, 3, 20, 18, "#FFFFFF"); d.Border(c, new(2, 3, 22, 21), ink); d.Text(c, "f(x)", 12, 17, 12, "#424242", true); break;
            case "step-out":
                d.Line(c, 5, 19, 5, 5, ink, 1.6f); d.Line(c, 5, 5, 18, 5, ink, 1.6f);
                d.Line(c, 18, 5, 13, 1, ink, 1.6f); d.Line(c, 18, 5, 13, 10, ink, 1.6f); d.Rect(c, 2, 21, 20, 2, "#5A5A5A"); break;
            case "quick-drop":
                d.Circle(c, 10, 9, 6, SKColors.White); d.Circle(c, 10, 9, 6, ink, false);
                d.Line(c, 14, 14, 21, 21, ink, 2); d.Line(c, 10, 5, 10, 13, ink); d.Line(c, 6, 9, 14, 9, ink);
                break;
            case "run": using (var p = new SKPath()) { p.MoveTo(4, 5); p.LineTo(11, 5); p.LineTo(11, 2); p.LineTo(21, 12); p.LineTo(11, 22); p.LineTo(11, 18); p.LineTo(4, 18); p.Close(); d.Path(c, p, SKColors.White, fill: true); d.Path(c, p, ink, 1.4f); } break;
            case "run-broken":
                Draw(c, "run", new(0, 0, 24, 24), d);
                d.Line(c, 4, 18, 20, 5, LabDrawing.Color("#EFEFEF"), 6);
                d.Line(c, 5, 17, 9, 11, LabDrawing.Color("#AC332A"), 2);
                d.Line(c, 9, 11, 14, 12, LabDrawing.Color("#AC332A"), 2);
                d.Line(c, 14, 12, 19, 6, LabDrawing.Color("#AC332A"), 2);
                break;
            case "continuous":
                using (var p = new SKPath()) { p.AddArc(new(3, 4, 20, 21), 205, 265); d.Path(c, p, ink, 1.6f); } d.Line(c, 5, 4, 5, 10, ink, 1.6f); d.Line(c, 5, 4, 11, 5, ink, 1.6f); break;
            case "stop": using (var p = new SKPath()) { p.MoveTo(7, 3); p.LineTo(17, 3); p.LineTo(22, 8); p.LineTo(22, 17); p.LineTo(17, 22); p.LineTo(7, 22); p.LineTo(2, 17); p.LineTo(2, 8); p.Close(); d.Path(c, p, LabDrawing.Color("#BF423B"), fill: true); d.Path(c, p, ink); } break;
            case "pause": d.Rect(c, 6, 4, 4, 16, "#363636"); d.Rect(c, 14, 4, 4, 16, "#363636"); break;
            case "step": d.Line(c, 4, 5, 4, 14, ink, 1.5f); d.Line(c, 4, 14, 18, 14, ink, 1.5f); d.Line(c, 18, 14, 13, 9, ink, 1.5f); d.Line(c, 18, 14, 13, 19, ink, 1.5f); d.Rect(c, 3, 20, 18, 2, "#5A5A5A"); break;
            case "highlight": d.Circle(c, 12, 9, 7, LabDrawing.Color(active ? "#FFDD52" : "#F7EEBD")); d.Circle(c, 12, 9, 7, ink, false); d.Rect(c, 9, 15, 6, 5, "#999999"); d.Line(c, 9, 22, 15, 22, ink); break;
            case "save": d.Bevel(c, new(3, 2, 21, 22), "#5D80A6"); d.Rect(c, 7, 3, 10, 7, "#E9E9E9"); d.Rect(c, 7, 13, 10, 8, "#FFFFFF"); d.Rect(c, 13, 3, 3, 5, "#707070"); break;
            case "open": using (var p = new SKPath()) { p.MoveTo(2, 6); p.LineTo(10, 6); p.LineTo(12, 9); p.LineTo(22, 9); p.LineTo(19, 21); p.LineTo(2, 21); p.Close(); d.Path(c, p, LabDrawing.Color("#EDD68D"), fill: true); d.Path(c, p, ink); } break;
            case "new": d.Bevel(c, new(5, 2, 19, 22), "#FFFFFF"); d.Line(c, 8, 9, 16, 9, LabDrawing.Color("#8095AF")); d.Line(c, 8, 13, 16, 13, LabDrawing.Color("#8095AF")); break;
            case "diagram": d.Rect(c, 2, 3, 7, 7, "#F8EBA1"); d.Rect(c, 15, 14, 7, 7, "#C7E7BC"); d.Line(c, 9, 6, 12, 6, LabDrawing.Color("#CD761A"), 2); d.Line(c, 12, 6, 12, 17, LabDrawing.Color("#CD761A"), 2); d.Line(c, 12, 17, 15, 17, LabDrawing.Color("#CD761A"), 2); break;
            case "panel": d.Bevel(c, new(2, 2, 22, 22)); d.Circle(c, 8, 9, 4, SKColors.White); d.Rect(c, 14, 6, 6, 9, "#1B3927"); d.Line(c, 5, 18, 18, 18, ink); break;
            case "fit": d.Border(c, new(3, 3, 21, 21), ink); d.Border(c, new(7, 7, 17, 17), LabDrawing.Color("#327CB6")); break;
            case "undo": d.Line(c, 20, 17, 20, 8, ink, 1.5f); d.Line(c, 20, 8, 4, 8, ink, 1.5f); d.Line(c, 4, 8, 9, 3, ink, 1.5f); d.Line(c, 4, 8, 9, 13, ink, 1.5f); break;
            case "redo": c.Translate(24, 0); c.Scale(-1, 1); Draw(c, "undo", new(0, 0, 24, 24), d); break;
            case "probe": d.Circle(c, 10, 9, 6, LabDrawing.Color("#D8EAB1")); d.Circle(c, 10, 9, 6, ink, false); d.Line(c, 14, 14, 21, 21, ink, 2); break;
            case "layout": d.Rect(c, 3, 3, 5, 5, "#C7D9E5"); d.Rect(c, 15, 3, 5, 5, "#C7D9E5"); d.Rect(c, 9, 16, 5, 5, "#C7D9E5"); d.Line(c, 5, 10, 5, 12, ink); d.Line(c, 5, 12, 18, 12, ink); d.Line(c, 18, 12, 18, 10, ink); d.Line(c, 11, 12, 11, 15, ink); break;
            case "help": d.Circle(c, 12, 12, 10, LabDrawing.Color("#FFF2B6")); d.Text(c, "?", 12, 18, 19, "#5B6557", true); break;
            case "edit": d.Line(c, 5, 20, 19, 5, LabDrawing.Color("#8B692C"), 5); d.Line(c, 5, 20, 4, 23, ink, 2); break;
            default: d.Text(c, name, 12, 17, name.Length > 3 ? 9 : 14, "#444444", true); break;
        }
        c.Restore();
    }
}
