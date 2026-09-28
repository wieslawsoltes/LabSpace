namespace LabSpace.Core;

public sealed record WireRoute(IReadOnlyList<PointD> Points, bool ObstacleFree, int ExploredStates);

/// <summary>
/// Deterministic Manhattan routing on an obstacle-edge visibility grid. Search includes direction in its
/// state so that bend penalties do not break shortest-path bookkeeping. No renderer or UI dependencies.
/// </summary>
public static class OrthogonalRouter
{
    public static WireRoute Route(PointD start, PointD end, IReadOnlyList<RectD> obstacles,
        double clearance = 8, int searchLimit = 50000, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!Finite(start) || !Finite(end) || !double.IsFinite(clearance) || clearance < 0 || searchLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(clearance));
        cancellationToken.ThrowIfCancellationRequested();
        if (obstacles.Any(r => !double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height) || r.Width < 0 || r.Height < 0)) throw new ArgumentException("Obstacle rectangles must be finite with nonnegative sizes.", nameof(obstacles));
        var all = obstacles.Select(r => new RectD(r.X - clearance, r.Y - clearance, r.Width + 2 * clearance, r.Height + 2 * clearance)).ToArray();
        var a = new PointD(start.X + 16, start.Y); var b = new PointD(end.X - 16, end.Y);
        var mid = (a.X + b.X) / 2;
        var candidates = new[]
        {
            new[] { start, a, new PointD(mid, a.Y), new PointD(mid, b.Y), b, end },
            new[] { start, a, new PointD(a.X, b.Y), b, end },
            new[] { start, a, new PointD(b.X, a.Y), b, end }
        };
        foreach (var candidate in candidates)
            if (Clear(candidate, all)) return new(Simplify(candidate), true, 0);

        // Bound pathological imported scenes. Returned paths are still checked against EVERY obstacle,
        // so the budget can reduce routing quality but never silently claim an obstructed path is clear.
        var relevant = all.OrderBy(r => DistanceToSegmentBounds(r, a, b)).Take(64).ToArray();
        var xs = relevant.SelectMany(r => new[] { r.X, r.Right }).Append(a.X).Append(b.X).Distinct().Order().ToArray();
        var ys = relevant.SelectMany(r => new[] { r.Y, r.Bottom }).Append(a.Y).Append(b.Y).Distinct().Order().ToArray();
        var nx = xs.Length; var ny = ys.Length; var states = checked(nx * ny * 3);
        var g = new double[states]; System.Array.Fill(g, double.PositiveInfinity);
        var previous = new int[states]; System.Array.Fill(previous, -1);
        var closed = new bool[states]; var queue = new PriorityQueue<int, (double Cost, int Order)>();
        var initial = (System.Array.BinarySearch(ys, a.Y) * nx + System.Array.BinarySearch(xs, a.X)) * 3;
        g[initial] = 0; var order = 0; queue.Enqueue(initial, (Manhattan(a, b), order++));
        var explored = 0; var goal = -1;
        while (queue.TryDequeue(out var state, out _) && explored < searchLimit)
        {
            if (closed[state]) continue;
            closed[state] = true; explored++;
            if ((explored & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
            var cell = state / 3; var axis = state % 3; var ix = cell % nx; var iy = cell / nx;
            var p = new PointD(xs[ix], ys[iy]);
            if (p == b) { goal = state; break; }
            Visit(ix - 1, iy, 1); Visit(ix + 1, iy, 1); Visit(ix, iy - 1, 2); Visit(ix, iy + 1, 2);
            void Visit(int x, int y, int direction)
            {
                if (x < 0 || y < 0 || x >= nx || y >= ny) return;
                var q = new PointD(xs[x], ys[y]);
                if (relevant.Any(r => IntersectsInterior(p, q, r))) return;
                var next = (y * nx + x) * 3 + direction;
                var cost = g[state] + Manhattan(p, q) + (axis != 0 && axis != direction ? 18 : 0);
                if (cost >= g[next]) return;
                g[next] = cost; previous[next] = state; queue.Enqueue(next, (cost + Manhattan(q, b), order++));
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (goal >= 0)
        {
            var points = new List<PointD> { end };
            for (var state = goal; state >= 0; state = previous[state])
            { var cell = state / 3; points.Add(new(xs[cell % nx], ys[cell / nx])); }
            points.Add(start); points.Reverse(); var route = Simplify(points);
            return new(route, Clear(route, all), explored);
        }
        return new(Simplify(candidates[0]), false, explored);
    }
    public static bool IntersectsInterior(PointD a, PointD b, RectD r)
    {
        const double epsilon = 1e-7;
        if (Math.Abs(a.Y - b.Y) < epsilon)
            return a.Y > r.Y + epsilon && a.Y < r.Bottom - epsilon && Math.Max(a.X, b.X) > r.X + epsilon && Math.Min(a.X, b.X) < r.Right - epsilon;
        if (Math.Abs(a.X - b.X) < epsilon)
            return a.X > r.X + epsilon && a.X < r.Right - epsilon && Math.Max(a.Y, b.Y) > r.Y + epsilon && Math.Min(a.Y, b.Y) < r.Bottom - epsilon;
        throw new ArgumentException("Wire segments must be orthogonal.");
    }
    public static IReadOnlyList<PointD> Simplify(IEnumerable<PointD> source)
    {
        var result = new List<PointD>();
        foreach (var p in source)
        {
            if (result.Count > 0 && result[^1] == p) continue;
            while (result.Count > 1 && ((result[^2].X == result[^1].X && result[^1].X == p.X) || (result[^2].Y == result[^1].Y && result[^1].Y == p.Y)))
            {
                // Do not erase a U-turn: it can be a deliberate escape from an endpoint's body.
                var u = result[^2]; var v = result[^1];
                if ((v.X - u.X) * (p.X - v.X) + (v.Y - u.Y) * (p.Y - v.Y) < 0) break;
                result.RemoveAt(result.Count - 1);
            }
            result.Add(p);
        }
        return result;
    }
    private static bool Clear(IReadOnlyList<PointD> points, IReadOnlyList<RectD> obstacles)
    {
        for (var i = 1; i < points.Count; i++) if (obstacles.Any(r => IntersectsInterior(points[i - 1], points[i], r))) return false;
        return true;
    }
    private static bool Finite(PointD p) => double.IsFinite(p.X) && double.IsFinite(p.Y);
    private static double Manhattan(PointD a, PointD b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    private static double DistanceToSegmentBounds(RectD r, PointD a, PointD b) =>
        Math.Max(0, Math.Max(Math.Min(a.X, b.X) - r.Right, r.X - Math.Max(a.X, b.X))) +
        Math.Max(0, Math.Max(Math.Min(a.Y, b.Y) - r.Bottom, r.Y - Math.Max(a.Y, b.Y)));
}
