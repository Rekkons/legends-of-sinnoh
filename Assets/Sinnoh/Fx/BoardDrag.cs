using System;

namespace Sinnoh.Fx
{
    /// <summary>끌기 한 프레임의 결과(<see cref="BoardDrag.Step"/>).</summary>
    public enum DragEvent
    {
        /// <summary>아무 일도 없다(단추가 안 눌렸거나, 눌린 채 문턱 안이거나, 무시하는 끌기다).</summary>
        None,
        /// <summary>단추가 막 눌렸다.</summary>
        Began,
        /// <summary>문턱을 넘어 끌고 있다 — 보는 점을 옮긴다.</summary>
        Pan,
        /// <summary>왼쪽 단추를 끌지 않고 뗐다 — 칸 누르기.</summary>
        Click,
        /// <summary>끌기가 끝났다(누르기가 아니다).</summary>
        Ended,
    }

    /// <summary>
    /// 마우스로 보드 끌기의 순수 부분(결정 66 ②, 04 §3-2 — 엔진 무관): 끌기 문턱 · 화면 광선 → 보드 평면 · 보는 점의 범위.
    /// 왼쪽 단추는 누른 자리에서 문턱보다 멀리 옮겨야 끌기이고, 그 전에 떼면 칸 누르기다. 가운데·오른쪽 단추도 끌지만 누르기는 내지 않는다.
    /// HUD·창 위에서 시작한 끌기는 뗄 때까지 무시한다. 화면 계산이라 부동소수를 쓴다 — 규칙 상태에는 닿지 않는다.
    /// </summary>
    public sealed class BoardDrag
    {
        /// <summary>끌기 문턱(1080 높이 기준 px) [가정].</summary>
        public const double ThresholdAt1080 = 6.0;
        /// <summary>작은 창에서도 손떨림을 끌기로 치지 않게 두는 문턱의 아래 한계(px) [가정].</summary>
        public const double ThresholdMin = 4.0;

        /// <summary>그 창 높이(물리 px)의 끌기 문턱.</summary>
        public static double Threshold(int height) => Math.Max(ThresholdMin, ThresholdAt1080 * height / 1080.0);

        double startX, startY;

        /// <summary>단추가 눌려 있다.</summary>
        public bool Held { get; private set; }
        /// <summary>문턱을 넘어 끌고 있다.</summary>
        public bool Panning { get; private set; }
        /// <summary>무시하는 끌기다(HUD·창 위에서 시작했거나 도중에 끊겼다) — 옮기지도 누르지도 않는다.</summary>
        public bool Dead { get; private set; }
        /// <summary>왼쪽 단추로 시작했다.</summary>
        public bool ByLeft { get; private set; }

        /// <summary>
        /// 한 프레임: 마우스 자리(물리 px) · 왼쪽 단추가 눌려 있나 · 가운데나 오른쪽 단추가 눌려 있나 · 그 자리가 HUD·창 위인가(누르는 순간만 본다) · 끌기 문턱.
        /// </summary>
        public DragEvent Step(double x, double y, bool left, bool other, bool overUi, double threshold)
        {
            bool any = left || other;
            if (!Held)
            {
                if (!any) return DragEvent.None;
                Held = true;
                Dead = overUi;
                Panning = false;
                ByLeft = left;
                startX = x;
                startY = y;
                return DragEvent.Began;
            }
            if (!any)
            {
                // 떼는 프레임의 자리도 문턱과 견준다 — 눌린 채 들어온 프레임 없이 멀리서 뗀 것(빠른 끌기 · 끊긴 프레임)은 누르기가 아니다
                var e = !Dead && !Panning && ByLeft && !Past(x, y, threshold) ? DragEvent.Click : DragEvent.Ended;
                Held = Panning = Dead = ByLeft = false;
                return e;
            }
            if (!left) ByLeft = false;   // 왼쪽을 먼저 떼고 다른 단추를 마저 떼는 것은 누르기가 아니다
            if (Dead) return DragEvent.None;
            if (!Panning && Past(x, y, threshold)) Panning = true;
            return Panning ? DragEvent.Pan : DragEvent.None;
        }

        bool Past(double x, double y, double threshold)
        {
            double dx = x - startX, dy = y - startY;
            return dx * dx + dy * dy > threshold * threshold;
        }

        /// <summary>눌린 채로 끌기를 끊는다(창이 떴다) — 뗄 때까지 옮기지도 누르지도 않는다.</summary>
        public void Kill()
        {
            if (!Held) return;
            Dead = true;
            Panning = false;
        }

        /// <summary>
        /// 점 (ox, oy, oz)에서 방향 (dx, dy, dz)로 가는 광선이 높이 planeY인 수평면과 만나는 점 (x, z). 광선이 그 면을 향하지 않으면 false.
        /// </summary>
        public static bool Ground(double ox, double oy, double oz, double dx, double dy, double dz, double planeY, out double x, out double z)
        {
            x = z = 0.0;
            if (Math.Abs(dy) < 1e-9) return false;
            double t = (planeY - oy) / dy;
            if (t <= 0.0) return false;
            x = ox + dx * t;
            z = oz + dz * t;
            return true;
        }

        /// <summary>
        /// 끈 뒤의 보는 점: 잡은 땅의 점(grab)이 지금 커서 아래의 점(hit — 잡은 점과 같은 높이의 수평면)에 오도록 보는 점을 옮기고 범위 안으로 자른다.
        /// 카메라의 방향·줌이 그대로라 보는 점을 옮긴 만큼 화면의 땅이 옮겨진다.
        /// </summary>
        public static void Pan(ref double targetX, ref double targetZ, double grabX, double grabZ, double hitX, double hitZ, double minX, double maxX, double minZ, double maxZ)
        {
            targetX = Clamp(targetX + grabX - hitX, minX, maxX);
            targetZ = Clamp(targetZ + grabZ - hitZ, minZ, maxZ);
        }

        /// <summary>
        /// 보드 카메라(기울기 tilt° · 방향 yaw° · 세로 시야 fov° · 보는 점까지 거리 distance — 보는 점 (tx, ty, tz))로 본 월드의 점 (px, py, pz)의 화면 자리(물리 px, 왼쪽 아래가 (0, 0)).
        /// 카메라 뒤의 점이면 false.
        /// </summary>
        public static bool Project(double px, double py, double pz, double tx, double ty, double tz, double tilt, double yaw, double fov, double distance, double width, double height, out double sx, out double sy)
        {
            double a = tilt * Math.PI / 180.0, th = yaw * Math.PI / 180.0;
            double sa = Math.Sin(a), ca = Math.Cos(a), st = Math.Sin(th), ct = Math.Cos(th);
            double fx = ca * st, fy = -sa, fz = ca * ct;
            double vx = px - (tx - fx * distance), vy = py - (ty - fy * distance), vz = pz - (tz - fz * distance);
            double depth = vx * fx + vy * fy + vz * fz;
            sx = sy = 0.0;
            if (depth <= 1e-9) return false;
            double k = height * 0.5 / Math.Tan(fov * Math.PI / 360.0);
            sx = width * 0.5 + (vx * ct - vz * st) / depth * k;
            sy = height * 0.5 + (vx * sa * st + vy * ca + vz * sa * ct) / depth * k;
            return true;
        }

        /// <summary>
        /// 점들(xyz — x, y, z를 차례로 이은 배열)이 화면에서 차지하는 네모의 가운데가 빈 네모(left ~ right, bottom ~ top — 물리 px, 왼쪽 아래가 (0, 0))의 가운데에 오는 보는 점 (x, z).
        /// 게임 시작의 전체 줌이 판을 HUD 판에 가리지 않는 자리에 놓는 데 쓴다(화면 한가운데가 아니라 위 띠와 아래 판 사이의 가운데). 보는 점의 높이는 ty 그대로다.
        /// </summary>
        public static void Frame(double[] xyz, double ty, double tilt, double yaw, double fov, double distance, double width, double height, double left, double bottom, double right, double top, out double x, out double z)
        {
            x = z = 0.0;
            if (xyz == null || xyz.Length < 3 || height <= 0.0 || distance <= 0.0) return;
            double th = yaw * Math.PI / 180.0, st = Math.Sin(th), ct = Math.Cos(th);
            double k = height * 0.5 / (distance * Math.Tan(fov * Math.PI / 360.0)), ky = k * Math.Sin(tilt * Math.PI / 180.0);
            for (int pass = 0; pass < 8; pass++)
            {
                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                for (int i = 0; i + 2 < xyz.Length; i += 3)
                {
                    if (!Project(xyz[i], xyz[i + 1], xyz[i + 2], x, ty, z, tilt, yaw, fov, distance, width, height, out double sx, out double sy)) continue;
                    if (sx < minX) minX = sx;
                    if (sx > maxX) maxX = sx;
                    if (sy < minY) minY = sy;
                    if (sy > maxY) maxY = sy;
                }
                if (minX > maxX) return;
                // 보는 점을 오른쪽으로 옮기면 화면의 땅은 왼쪽으로, 앞으로 옮기면 아래로 간다
                double side = ((minX + maxX) * 0.5 - (left + right) * 0.5) / k, ahead = ((minY + maxY) * 0.5 - (bottom + top) * 0.5) / ky;
                x += ct * side + st * ahead;
                z += -st * side + ct * ahead;
                if (Math.Abs(side * k) < 0.01 && Math.Abs(ahead * ky) < 0.01) break;
            }
        }

        public static double Clamp(double v, double min, double max) => v < min ? min : v > max ? max : v;
    }
}
