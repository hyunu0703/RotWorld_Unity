using System;
using System.Collections.Generic;
using UnityEngine;

namespace RotWorld
{
    /// <summary>
    /// 지형과 지역 면적 및 생활권에 따른 도로와 필지 배치
    /// </summary>
    internal sealed class CityLayout
    {
        private const int MainWidth = 6;
        private const int LocalWidth = 4;
        private const int BankBuffer = 3;
        private const int MaxEntranceLength = 32;
        private readonly CityMap _map;
        private readonly CitySettings _settings;
        private readonly System.Random _random;
        private readonly System.Random _stocks;
        private readonly Vector2Int[] _centers = new Vector2Int[5];
        private readonly List<Vector2Int> _nodes = new List<Vector2Int>();
        private readonly List<Vector2Int> _banks = new List<Vector2Int>();
        private readonly int _size;
        private readonly double _phase;
        private readonly int _rotation;
        private bool[] _mainSafe;
        private bool[] _localSafe;
        private bool _roadsReady;

        // 지도와 설정에서 독립 난수 흐름 준비
        internal CityLayout(CityMap map, CitySettings settings)
        {
            _map = map;
            _settings = settings;
            _size = map.Size;
            _random = new System.Random(unchecked(map.Seed ^ 0x73AE12));
            _stocks = new System.Random(unchecked(map.Seed ^ 0x4A326F));
            _phase = ((uint)map.Seed % 997) / 997.0 * Math.PI * 2;
            _rotation = (int)((uint)map.Seed >> 4) & 3;
        }

        // 지형 계획에서 도로와 필지까지 순차 생성
        internal void Generate()
        {
            CarveRiver();
            PlanCenters();
            AssignRegions();
            CreateBridges();
            ReserveLandmarks();
            _mainSafe = BuildRoadMask(MainWidth);
            _localSafe = BuildRoadMask(LocalWidth);
            KeepConnectedRoadSpace();
            BuildNetwork();
            BuildLocalStreets();
            _roadsReady = true;
            ConnectBuildings();
            PopulateBuildings();
            ConnectBuildings();
        }

        // 실제 좌표를 회전된 계획 좌표로 변환
        private Vector2 Plan(int x, int y)
        {
            float px = x / (float)_size - .5f;
            float py = y / (float)_size - .5f;
            for (int i = 0; i < _rotation; i++) { (px, py) = (-py, px); }
            return new Vector2(px, py);
        }

        // 계획 좌표를 실제 타일로 변환
        private Vector2Int World(float x, float y)
        {
            for (int i = 0; i < _rotation; i++) { (x, y) = (y, -x); }
            return new Vector2Int((int)((x + .5f) * _size), (int)((y + .5f) * _size));
        }

        // 계획 좌표의 하천 중심 계산
        private float RiverX(float y)
        {
            return .14f + (float)Math.Sin(y * 12 + _phase) * .035f;
        }

        // 육지와 해안에 하천 및 하구 생성
        private void CarveRiver()
        {
            float halfWidth = Math.Max(1.5f, _size * .012f);
            for (int y = 0; y < _size; y++)
            {
                for (int x = 0; x < _size; x++)
                {
                    int i = y * _size + x;
                    Vector2 p = Plan(x, y);
                    if (Math.Abs(p.x - RiverX(p.y)) * _size <= halfWidth &&
                        _map.Terrain[i] != CityGenerator.Ocean && _map.Terrain[i] != CityGenerator.Shallows)
                    {
                        _map.Terrain[i] = _map.Tiles[i] = CityGenerator.River;
                    }
                }
            }
        }

        // 여유 공간을 포함한 건설 가능 육지 판정
        private bool Inland(int x, int y, int margin = 0)
        {
            if (x - margin < 0 || y - margin < 0 || x + margin >= _size || y + margin >= _size) { return false; }
            for (int dy = -margin; dy <= margin; dy++)
            {
                for (int dx = -margin; dx <= margin; dx++)
                {
                    if (_map.Terrain[(y + dy) * _size + x + dx] != CityGenerator.Grass) { return false; }
                }
            }
            return true;
        }

        // 원하는 지점 주변의 안전한 생활권 중심 선택
        private Vector2Int FindCenter(Vector2Int target, int margin, List<Vector2Int> avoid = null)
        {
            int best = -1;
            double score = double.MaxValue;
            for (int y = margin; y < _size - margin; y++)
            {
                for (int x = margin; x < _size - margin; x++)
                {
                    double distance = (x - target.x) * (x - target.x) + (y - target.y) * (y - target.y);
                    if (distance >= score || !Inland(x, y, margin)) { continue; }
                    bool separated = true;
                    if (avoid != null)
                    {
                        foreach (var other in avoid)
                        {
                            if ((other - new Vector2Int(x, y)).sqrMagnitude < _size * _size * .012f) { separated = false; }
                        }
                    }
                    if (separated) { best = y * _size + x; score = distance; }
                }
            }
            if (best < 0) { throw new InvalidOperationException("생활권 배치에 필요한 육지 부족"); }
            return new Vector2Int(best % _size, best / _size);
        }

        // 맵 크기에 맞는 도심과 독립 마을 중심 배치
        private void PlanCenters()
        {
            _centers[2] = FindCenter(World(-.05f, -.02f), 6);
            _centers[0] = FindCenter(World(-.10f, -.16f), 5);
            _centers[4] = FindCenter(World(.24f, -.09f), 8);
            _centers[3] = FindCenter(World(RiverX(.09f) - .055f, .09f), 4);
            int count = _size == 128 ? 2 : _size == 256 ? 3 : _random.Next(4, 6);
            for (int i = 0; i < count; i++)
            {
                double angle = .3 + i * Math.PI * 1.25 / Math.Max(1, count - 1) + _random.NextDouble() * .12;
                var target = World((float)Math.Cos(angle) * .24f, (float)Math.Sin(angle) * .24f);
                _map.VillageCenters.Add(FindCenter(target, 4, _map.VillageCenters));
            }
            _centers[1] = _map.VillageCenters[0];
        }

        // 지역 거리와 하천 및 완만한 경계 변화에 따른 우선도 계산
        private double RegionScore(int index, CityRegion region)
        {
            int x = index % _size;
            int y = index / _size;
            Vector2 p = Plan(x, y);
            double roughness = Math.Sin(p.x * 21 + _phase) * Math.Sin(p.y * 17) * .0007;
            if (region == CityRegion.Riverside) { return Math.Abs(p.x - RiverX(p.y) + .065f) * .08 + Math.Abs(p.y) * .002 + roughness; }
            double best = double.MaxValue;
            if (region == CityRegion.Villages)
            {
                foreach (var center in _map.VillageCenters)
                {
                    best = Math.Min(best, (center - new Vector2Int(x, y)).sqrMagnitude / (double)(_size * _size));
                }
            }
            else { best = (_centers[(int)region] - new Vector2Int(x, y)).sqrMagnitude / (double)(_size * _size); }
            return best + roughness;
        }

        // 목표 면적 비율에 맞춘 지역 할당과 자연 공간 보존
        private void AssignRegions()
        {
            float[] weights = { .23f, .15f, .08f, .10f, .09f, .35f };
            Array.Copy(weights, _map.RegionTargets, weights.Length);
            for (int i = 0; i < 18; i++)
            {
                int a = _random.Next(weights.Length);
                int b = _random.Next(weights.Length);
                float change = (float)(_random.NextDouble() * .02 - .01);
                if (Math.Abs(_map.RegionTargets[a] + change - weights[a]) <= .025f &&
                    Math.Abs(_map.RegionTargets[b] - change - weights[b]) <= .025f)
                {
                    _map.RegionTargets[a] += change;
                    _map.RegionTargets[b] -= change;
                }
            }
            int landCount = 0;
            var remaining = new List<int>();
            for (int i = 0; i < _map.Terrain.Length; i++)
            {
                _map.Regions[i] = CityRegion.Natural;
                if (_map.Terrain[i] == CityGenerator.Grass) { remaining.Add(i); landCount++; }
                else if (_map.Terrain[i] == CityGenerator.Sand) { landCount++; }
            }
            CityRegion[] order = { CityRegion.Downtown, CityRegion.Industrial, CityRegion.Villages, CityRegion.Riverside, CityRegion.Mixed };
            foreach (CityRegion region in order)
            {
                var scores = new double[_map.Tiles.Length];
                foreach (int index in remaining) { scores[index] = RegionScore(index, region); }
                remaining.Sort((a, b) =>
                {
                    int comparison = scores[a].CompareTo(scores[b]);
                    return comparison != 0 ? comparison : a.CompareTo(b);
                });
                int count = Math.Min(remaining.Count, (int)(landCount * _map.RegionTargets[(int)region]));
                for (int i = 0; i < count; i++) { _map.Regions[remaining[i]] = region; }
                remaining.RemoveRange(0, count);
            }
        }

        // 지정 횡단 위치에서만 하천 교량과 육지 접근부 생성
        private void CreateBridges()
        {
            int target = _size == 128 ? 2 : _size == 256 ? _random.Next(2, 4) : _random.Next(3, 5);
            for (int b = 0; b < target; b++)
            {
                float desiredY = -.18f + b * .36f / (target - 1);
                bool placed = false;
                for (int attempt = 0; attempt < _size && !placed; attempt++)
                {
                    float py = desiredY + (attempt % 2 == 0 ? 1 : -1) * (attempt / 2) / (float)_size;
                    if (Math.Abs(py) > .28f) { continue; }
                    float px = RiverX(py);
                    float half = Math.Max(1.5f, _size * .012f) + 7;
                    Vector2Int a = World(px - half / _size, py);
                    Vector2Int z = World(px + half / _size, py);
                    var bounds = a.y == z.y
                        ? new RectInt(Math.Min(a.x, z.x), a.y - 4, Math.Abs(a.x - z.x) + 1, 8)
                        : new RectInt(a.x - 4, Math.Min(a.y, z.y), 8, Math.Abs(a.y - z.y) + 1);
                    bool valid = Inland(a.x, a.y, 3) && Inland(z.x, z.y, 3);
                    foreach (var previous in _map.Bridges) { if (previous.Overlaps(bounds)) { valid = false; } }
                    for (int y = bounds.yMin; y < bounds.yMax && valid; y++)
                    {
                        for (int x = bounds.xMin; x < bounds.xMax; x++)
                        {
                            if (x < 0 || y < 0 || x >= _size || y >= _size ||
                                (_map.Terrain[y * _size + x] != CityGenerator.Grass && _map.Terrain[y * _size + x] != CityGenerator.River))
                            { valid = false; break; }
                        }
                    }
                    if (!valid) { continue; }
                    for (int y = bounds.yMin; y < bounds.yMax; y++)
                    {
                        for (int x = bounds.xMin; x < bounds.xMax; x++)
                        {
                            int index = y * _size + x;
                            _map.Tiles[index] = _map.Terrain[index] == CityGenerator.River ? CityGenerator.Bridge : CityGenerator.Pavement;
                        }
                    }
                    _map.Bridges.Add(bounds);
                    _banks.Add(a);
                    _banks.Add(z);
                    placed = true;
                }
                if (!placed) { throw new InvalidOperationException("안전한 교량 접근부 확보 실패"); }
            }
        }

        // 지역 최소 시설과 독립 마을 부지를 도로보다 먼저 확보
        private void ReserveLandmarks()
        {
            CityRegion[] order = { CityRegion.Industrial, CityRegion.Downtown, CityRegion.Riverside, CityRegion.Mixed };
            foreach (var region in order)
            {
                ReserveBuilding(region, _centers[(int)region]);
            }
            foreach (var center in _map.VillageCenters) { ReserveBuilding(CityRegion.Villages, center); }
        }

        // 지역의 최소 건물 규격으로 목표 중심 근처 필지 확보
        private void ReserveBuilding(CityRegion region, Vector2Int center)
        {
            var size = BuildingSize(region, true);
            var candidates = RegionCandidates(region);
            candidates.Sort((a, b) =>
            {
                int da = (new Vector2Int(a % _size, a / _size) - center).sqrMagnitude;
                int db = (new Vector2Int(b % _size, b / _size) - center).sqrMagnitude;
                int compare = da.CompareTo(db);
                return compare != 0 ? compare : a.CompareTo(b);
            });
            int kind = region == CityRegion.Industrial ? 2 : region == CityRegion.Downtown ? 3
                : region == CityRegion.Riverside ? 4 : 0;
            foreach (int index in candidates)
            {
                if (TryBuilding(new RectInt(index % _size - size.x / 2, index / _size - size.y / 2, size.x, size.y),
                    region, kind, region == CityRegion.Villages ? 4 : 2)) { return; }
            }
            throw new InvalidOperationException("지역 최소 시설 부지 확보 실패: " + region);
        }

        // 건물과 물을 피하는 도로 전체 폭 마스크 생성
        private bool[] BuildRoadMask(int width)
        {
            var mask = new bool[_map.Tiles.Length];
            int low = -width / 2;
            for (int y = width; y < _size - width; y++)
            {
                for (int x = width; x < _size - width; x++)
                {
                    bool valid = true;
                    for (int dy = low; dy < width + low && valid; dy++)
                    {
                        for (int dx = low; dx < width + low; dx++)
                        {
                            int i = (y + dy) * _size + x + dx;
                            if (_map.Tiles[i] == CityGenerator.Building ||
                                (_map.Terrain[i] != CityGenerator.Grass && _map.Tiles[i] != CityGenerator.Bridge))
                            { valid = false; break; }
                        }
                    }
                    mask[y * _size + x] = valid;
                }
            }
            return mask;
        }

        // 좁은 반도의 고립된 도로 후보를 제외하고 가장 큰 연결 공간 보존
        private void KeepConnectedRoadSpace()
        {
            var visited = new bool[_localSafe.Length];
            var largest = new List<int>();
            var queue = new Queue<int>();
            int[] dx = { -1, 1, 0, 0 };
            int[] dy = { 0, 0, -1, 1 };
            for (int i = 0; i < _localSafe.Length; i++)
            {
                if (!_localSafe[i] || visited[i]) { continue; }
                var component = new List<int>();
                visited[i] = true;
                queue.Enqueue(i);
                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    component.Add(index);
                    for (int d = 0; d < dx.Length; d++)
                    {
                        int x = index % _size + dx[d];
                        int y = index / _size + dy[d];
                        if (x < 0 || y < 0 || x >= _size || y >= _size) { continue; }
                        int next = y * _size + x;
                        if (_localSafe[next] && !visited[next]) { visited[next] = true; queue.Enqueue(next); }
                    }
                }
                if (component.Count > largest.Count) { largest = component; }
            }
            Array.Clear(visited, 0, visited.Length);
            foreach (int i in largest) { visited[i] = true; }
            for (int i = 0; i < visited.Length; i++)
            {
                _localSafe[i] &= visited[i];
                _mainSafe[i] &= visited[i];
            }
        }

        // 원하는 지점에 가장 가까운 도로 폭 확보 위치 선택
        private Vector2Int RoadPoint(Vector2Int target, bool[] mask)
        {
            int best = -1;
            int distance = int.MaxValue;
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) { continue; }
                int dx = i % _size - target.x;
                int dy = i / _size - target.y;
                int d = dx * dx + dy * dy;
                if (d < distance) { distance = d; best = i; }
            }
            if (best < 0) { throw new InvalidOperationException("도로 폭 확보 실패"); }
            return new Vector2Int(best % _size, best / _size);
        }

        // 지역별 도로 간격 범위에서 블록 내부 폭 선택
        private int BlockSpacing(CityRegion region)
        {
            switch (region)
            {
                case CityRegion.Downtown: return _random.Next(12, 21);
                case CityRegion.Mixed: return _random.Next(20, 33);
                case CityRegion.Villages: return _random.Next(16, 29);
                case CityRegion.Riverside: return _random.Next(18, 31);
                default: return _random.Next(28, 45);
            }
        }

        // 작은 격자와 순환로 및 가지형 도로를 지역마다 조합
        private void BuildLocalStreets()
        {
            for (int region = 0; region < 5; region++)
            {
                if (region == (int)CityRegion.Villages) { continue; }
                var center = _centers[region];
                int spacing = BlockSpacing((CityRegion)region);
                float radius = _size == 128 ? spacing * .4f : spacing * .75f;
                var hub = RoadPoint(center, _localSafe);
                Route(hub, _nodes[region == 2 ? 0 : region == 0 ? 1 : region == 4 ? 2 : 3], LocalWidth, false);
                if (region == (int)CityRegion.Downtown)
                {
                    double angle = _random.NextDouble() * .6 - .3;
                    int halfLines = _size == 128 ? 0 : 1;
                    for (int line = -halfLines; line <= halfLines; line++)
                    {
                        float shift = line * (spacing + LocalWidth);
                        Vector2Int a = new Vector2Int(center.x + (int)(-radius * Math.Cos(angle) - shift * Math.Sin(angle)),
                            center.y + (int)(-radius * Math.Sin(angle) + shift * Math.Cos(angle)));
                        Vector2Int b = new Vector2Int(center.x + (int)(radius * Math.Cos(angle) - shift * Math.Sin(angle)),
                            center.y + (int)(radius * Math.Sin(angle) + shift * Math.Cos(angle)));
                        var start = RoadPoint(a, _localSafe);
                        Route(hub, start, LocalWidth, false);
                        Route(start, RoadPoint(b, _localSafe), LocalWidth, false);
                    }
                }
                else if (region == (int)CityRegion.Riverside)
                {
                    var p = Plan(center.x, center.y);
                    for (int i = -2; i <= 2; i++)
                    {
                        float y = p.y + i * spacing / (float)_size * .5f;
                        var next = RoadPoint(World(RiverX(y) - .055f, y), _localSafe);
                        Route(hub, next, LocalWidth, true);
                        hub = next;
                    }
                }
                else
                {
                    var previous = hub;
                    Vector2Int first = hub;
                    for (int i = 0; i < 6; i++)
                    {
                        double angle = i * Math.PI / 3 + .25;
                        var point = RoadPoint(new Vector2Int(center.x + (int)(Math.Cos(angle) * radius),
                            center.y + (int)(Math.Sin(angle) * radius * .75)), _localSafe);
                        if (i == 0) { first = point; }
                        Route(previous, point, LocalWidth, region != (int)CityRegion.Industrial);
                        previous = point;
                    }
                    Route(previous, first, LocalWidth, true);
                }
            }
            foreach (var center in _map.VillageCenters)
            {
                var hub = RoadPoint(center, _localSafe);
                Route(RoadPoint(center, _mainSafe), hub, LocalWidth, false);
                int spacing = BlockSpacing(CityRegion.Villages);
                for (int branch = 0; branch < 3; branch++)
                {
                    double angle = branch * Math.PI * 2 / 3 + _random.NextDouble() * .6;
                    float length = _size == 128 ? spacing * .35f : spacing * .6f;
                    var end = RoadPoint(new Vector2Int(center.x + (int)(Math.Cos(angle) * length),
                        center.y + (int)(Math.Sin(angle) * length)), _localSafe);
                    Route(hub, end, LocalWidth, true);
                }
            }
        }

        // 완만한 중간 지점 또는 직접 연결로 도로 경로 생성
        private bool Route(Vector2Int from, Vector2Int to, int width, bool bend)
        {
            if (from == to) { PaintRoad(from, width); return true; }
            if (bend && (from - to).sqrMagnitude > 400)
            {
                var middle = new Vector2Int((from.x + to.x) / 2 + _random.Next(-8, 9),
                    (from.y + to.y) / 2 + _random.Next(-8, 9));
                middle = RoadPoint(middle, width == MainWidth ? _mainSafe : _localSafe);
                var first = FindRoute(from, middle, width);
                var second = FindRoute(middle, to, width);
                if (first != null && second != null)
                {
                    DrawRoute(first, width);
                    DrawRoute(second, width);
                    return true;
                }
            }
            var path = FindRoute(from, to, width);
            if (path == null) { return false; }
            DrawRoute(path, width);
            return true;
        }

        // 도로 전체 폭을 보장하는 마스크에서 A별 경로 탐색
        private List<Vector2Int> FindRoute(Vector2Int from, Vector2Int to, int width)
        {
            var mask = width == MainWidth ? _mainSafe : _localSafe;
            int start = from.y * _size + from.x;
            int goal = to.y * _size + to.x;
            var distance = new int[_map.Tiles.Length];
            var parents = new int[distance.Length];
            Array.Fill(distance, int.MaxValue);
            Array.Fill(parents, -1);
            var open = new SortedSet<(int Cost, int Index)>();
            distance[start] = 0;
            open.Add((0, start));
            int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
            int[] dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
            while (open.Count > 0)
            {
                var item = open.Min;
                open.Remove(item);
                int index = item.Index;
                if (index == goal)
                {
                    var path = new List<Vector2Int>();
                    for (int p = goal; p != start; p = parents[p]) { path.Add(new Vector2Int(p % _size, p / _size)); }
                    path.Add(from);
                    path.Reverse();
                    return path;
                }
                int x = index % _size;
                int y = index / _size;
                for (int d = 0; d < dx.Length; d++)
                {
                    int nx = x + dx[d];
                    int ny = y + dy[d];
                    if (nx < 1 || ny < 1 || nx >= _size - 1 || ny >= _size - 1) { continue; }
                    int next = ny * _size + nx;
                    if (!mask[next] || (d >= 4 && (!mask[y * _size + nx] || !mask[ny * _size + x]))) { continue; }
                    Vector2 p = Plan(nx, ny);
                    int noise = (int)((Math.Sin(p.x * 27 + _phase) * Math.Cos(p.y * 23) + 1) * 3);
                    int cost = distance[index] + (d >= 4 ? 14 : 10) + noise;
                    if (cost >= distance[next]) { continue; }
                    distance[next] = cost;
                    parents[next] = index;
                    int heuristic = Math.Max(Math.Abs(nx - to.x), Math.Abs(ny - to.y)) * 10;
                    open.Add((cost + heuristic, next));
                }
            }
            return null;
        }

        // 도로 경로 보존과 픽셀 격자 폭 적용
        private void DrawRoute(List<Vector2Int> path, int width)
        {
            _map.RoadRoutes.Add(path);
            foreach (var point in path) { PaintRoad(point, width); }
        }

        // 육지와 승인된 교량에만 도로 폭 기록
        private void PaintRoad(Vector2Int point, int width)
        {
            int low = -width / 2;
            int high = width + low;
            for (int dy = low; dy < high; dy++)
            {
                for (int dx = low; dx < high; dx++)
                {
                    int i = (point.y + dy) * _size + point.x + dx;
                    if (_map.Terrain[i] == CityGenerator.River && _map.Tiles[i] == CityGenerator.Bridge) { continue; }
                    if (_map.Terrain[i] != CityGenerator.Grass || _map.Tiles[i] == CityGenerator.Building) { continue; }
                    bool edge = dx == low || dx == high - 1 || dy == low || dy == high - 1;
                    if (_map.Tiles[i] != CityGenerator.Road) { _map.Tiles[i] = edge ? CityGenerator.Pavement : CityGenerator.Road; }
                }
            }
        }

        // 지역 중심에서 가까운 필지 후보의 고정 순서 생성
        private List<int> RegionCandidates(CityRegion region)
        {
            var candidates = new List<int>();
            for (int i = 0; i < _map.Tiles.Length; i++)
            {
                if (_map.Regions[i] == region && _map.Terrain[i] == CityGenerator.Grass) { candidates.Add(i); }
            }
            candidates.Sort((a, b) =>
            {
                int comparison = RegionScore(a, region).CompareTo(RegionScore(b, region));
                return comparison != 0 ? comparison : a.CompareTo(b);
            });
            return candidates;
        }

        // 지역별 실제 바닥 크기 범위 선택
        private Vector2Int BuildingSize(CityRegion region, bool minimum)
        {
            switch (region)
            {
                case CityRegion.Downtown: return new Vector2Int(minimum ? 5 : _random.Next(5, 11), minimum ? 6 : _random.Next(6, 13));
                case CityRegion.Villages: return new Vector2Int(minimum ? 4 : _random.Next(4, 7), minimum ? 5 : _random.Next(5, 8));
                case CityRegion.Riverside: return new Vector2Int(minimum ? 4 : _random.Next(4, 9), minimum ? 6 : _random.Next(6, 11));
                case CityRegion.Industrial: return new Vector2Int(minimum ? 10 : _random.Next(10, 19), minimum ? 12 : _random.Next(12, 25));
                default: return new Vector2Int(minimum ? 4 : _random.Next(4, 11), minimum ? 5 : _random.Next(5, 11));
            }
        }

        // 필지 비율을 밀도 설정으로 보정한 지역별 건물 배치
        private void PopulateBuildings()
        {
            float[] fill = { .675f, .425f, .90f, .55f, .475f };
            int[] lotAreas = { 110, 160, 85, 140, 400 };
            CityRegion[] order = { CityRegion.Downtown, CityRegion.Villages, CityRegion.Riverside, CityRegion.Mixed, CityRegion.Industrial };
            foreach (var region in order)
            {
                var candidates = RegionCandidates(region);
                int target = Math.Max(region == CityRegion.Downtown ? 3 : 1,
                    (int)(candidates.Count / (float)lotAreas[(int)region] * fill[(int)region] * _settings.Density / 65f));
                int count = CountBuildings(region);
                int step = Math.Max(1, candidates.Count / Math.Max(1, target * 60));
                for (int pass = 0; pass < 2 && count < target; pass++)
                {
                    for (int i = 0; i < candidates.Count && count < target; i += step)
                    {
                        int index = candidates[(i + pass * 17) % candidates.Count];
                        var size = BuildingSize(region, pass == 1);
                        var bounds = new RectInt(index % _size - size.x / 2, index / _size - size.y / 2, size.x, size.y);
                        int gap = region == CityRegion.Downtown ? 1 : region == CityRegion.Villages ? 4 : 2;
                        int kind = region == CityRegion.Industrial ? 2 : region == CityRegion.Downtown ? 3
                            : region == CityRegion.Riverside ? 4 : region == CityRegion.Villages ? 0 : _random.Next(2);
                        if (TryBuilding(bounds, region, kind, gap)) { count++; }
                    }
                }
                if (count == 0) { throw new InvalidOperationException("지역 최소 건물 배치 실패: " + region); }
            }
            foreach (var village in _map.VillageCenters)
            {
                bool populated = false;
                foreach (var building in _map.Buildings)
                {
                    if (building.Kind == 0 && ((Vector2)village - building.Bounds.center).sqrMagnitude < _size * _size * .01f) { populated = true; }
                }
                for (int r = 0; r < _size / 8 && !populated; r++)
                {
                    for (int y = village.y - r; y <= village.y + r && !populated; y++)
                    {
                        for (int x = village.x - r; x <= village.x + r && !populated; x++)
                        {
                            if (x < 1 || y < 1 || x >= _size - 5 || y >= _size - 6 || _map.Regions[y * _size + x] != CityRegion.Villages) { continue; }
                            populated = TryBuilding(new RectInt(x, y, 4, 5), CityRegion.Villages, 0, 3);
                        }
                    }
                }
                if (!populated) { throw new InvalidOperationException("마을 주거 부지 부족"); }
            }
        }

        // 지역별 실제 건물 수 조회
        private int CountBuildings(CityRegion region)
        {
            int count = 0;
            foreach (var building in _map.Buildings)
            {
                if (_map.Regions[(int)building.Bounds.center.y * _size + (int)building.Bounds.center.x] == region) { count++; }
            }
            return count;
        }

        // 건물 바닥과 간격 및 수변 여유 공간 검사 후 배치
        private bool TryBuilding(RectInt bounds, CityRegion region, int kind, int gap)
        {
            int cx = (int)bounds.center.x;
            int cy = (int)bounds.center.y;
            if (bounds.xMin < gap + 1 || bounds.yMin < gap + 1 || bounds.xMax >= _size - gap - 1 ||
                bounds.yMax >= _size - gap - 1 || _map.Regions[cy * _size + cx] != region) { return false; }
            for (int y = bounds.yMin - gap; y < bounds.yMax + gap; y++)
            {
                for (int x = bounds.xMin - gap; x < bounds.xMax + gap; x++)
                {
                    int i = y * _size + x;
                    if (_map.Tiles[i] == CityGenerator.Building) { return false; }
                    if (bounds.Contains(new Vector2Int(x, y)) && (_map.Tiles[i] != CityGenerator.Grass || !Inland(x, y, 1))) { return false; }
                }
            }
            for (int y = bounds.yMin - BankBuffer; y < bounds.yMax + BankBuffer; y++)
            {
                for (int x = bounds.xMin - BankBuffer; x < bounds.xMax + BankBuffer; x++)
                {
                    if (x >= 0 && y >= 0 && x < _size && y < _size && _map.Terrain[y * _size + x] == CityGenerator.River) { return false; }
                }
            }
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++) { _map.Tiles[y * _size + x] = CityGenerator.Building; }
            }
            var door = new Vector2Int(cx, bounds.yMin - 1);
            if (_roadsReady && !FindEntrance(bounds, out door))
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    for (int x = bounds.xMin; x < bounds.xMax; x++) { _map.Tiles[y * _size + x] = CityGenerator.Grass; }
                }
                return false;
            }
            var stock = new int[5];
            for (int i = 0; i < stock.Length; i++) { stock[i] = (int)(_stocks.Next(0, i == 4 ? 3 : 9) * _settings.Supplies / 100f); }
            _map.Buildings.Add(new CityBuilding(bounds, door, kind, stock));
            return true;
        }

        // 건물마다 바다와 강을 피하는 출입 통로 확보
        private void ConnectBuildings()
        {
            for (int b = 0; b < _map.Buildings.Count; b++)
            {
                var building = _map.Buildings[b];
                var bounds = building.Bounds;
                if (!FindEntrance(bounds, out var door)) { throw new InvalidOperationException("건물 출입 통로 확보 실패"); }
                if (door != building.Door) { _map.Buildings[b] = new CityBuilding(bounds, door, building.Kind, building.Stock); }
            }
        }

        // 건물 네 면에서 연결 가능한 출입구 선택
        private bool FindEntrance(RectInt bounds, out Vector2Int door)
        {
            var doors = new[] { new Vector2Int((int)bounds.center.x, bounds.yMin - 1), new Vector2Int((int)bounds.center.x, bounds.yMax),
                new Vector2Int(bounds.xMin - 1, (int)bounds.center.y), new Vector2Int(bounds.xMax, (int)bounds.center.y) };
            foreach (var candidate in doors)
            {
                if (Entrance(candidate)) { door = candidate; return true; }
            }
            door = default;
            return false;
        }

        // 출입구에서 제한 거리 안의 도로까지 보행 경로 탐색
        private bool Entrance(Vector2Int door)
        {
            int start = door.y * _size + door.x;
            if (_map.Tiles[start] == CityGenerator.Building || !Inland(door.x, door.y)) { return false; }
            var parents = new Dictionary<int, int>();
            var queue = new Queue<(int Index, int Depth)>();
            queue.Enqueue((start, 0));
            parents[start] = start;
            int[] offsets = { -1, 1, -_size, _size };
            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                int index = item.Index;
                byte tile = _map.Tiles[index];
                if (tile == CityGenerator.Road || tile == CityGenerator.Pavement || tile == CityGenerator.Path)
                {
                    for (int p = index; p != start; p = parents[p])
                    {
                        if (_map.Tiles[p] == CityGenerator.Grass) { _map.Tiles[p] = CityGenerator.Path; }
                    }
                    if (_map.Tiles[start] == CityGenerator.Grass) { _map.Tiles[start] = CityGenerator.Path; }
                    return true;
                }
                if (item.Depth >= MaxEntranceLength) { continue; }
                foreach (int offset in offsets)
                {
                    int next = index + offset;
                    if (next < 0 || next >= _map.Tiles.Length || Math.Abs(next % _size - index % _size) > 1 ||
                        parents.ContainsKey(next) || _map.Terrain[next] != CityGenerator.Grass || _map.Tiles[next] == CityGenerator.Building) { continue; }
                    parents[next] = index;
                    queue.Enqueue((next, item.Depth + 1));
                }
            }
            return false;
        }

        // 생활권과 다리를 최소 연결망 및 대체 경로로 연결
        private void BuildNetwork()
        {
            _nodes.Add(RoadPoint(_centers[2], _mainSafe));
            _nodes.Add(RoadPoint(_centers[0], _mainSafe));
            _nodes.Add(RoadPoint(_centers[4], _mainSafe));
            _nodes.Add(RoadPoint(_centers[3], _mainSafe));
            foreach (var center in _map.VillageCenters) { _nodes.Add(RoadPoint(center, _mainSafe)); }
            foreach (var bank in _banks) { _nodes.Add(RoadPoint(bank, _mainSafe)); }
            if (_size == 512) { _nodes.Add(RoadPoint(World(-.18f, .04f), _mainSafe)); }
            for (int i = 1; i < _nodes.Count; i++)
            {
                int nearest = 0;
                for (int j = 1; j < i; j++)
                {
                    if ((_nodes[i] - _nodes[j]).sqrMagnitude < (_nodes[i] - _nodes[nearest]).sqrMagnitude) { nearest = j; }
                }
                if (!Route(_nodes[nearest], _nodes[i], MainWidth, true) &&
                    !Route(_nodes[nearest], _nodes[i], LocalWidth, true))
                {
                    throw new InvalidOperationException($"생활권 간 도로 연결 실패: {_size}/{_map.Seed}, {_nodes[nearest].x},{_nodes[nearest].y} → {_nodes[i].x},{_nodes[i].y}");
                }
            }
            for (int i = 0; i < 4; i++) { Route(_nodes[i], _nodes[(i + 2) % 4], MainWidth, true); }
            for (int i = 0; i < _banks.Count; i += 2)
            {
                Route(RoadPoint(_banks[i], _mainSafe), RoadPoint(_banks[i + 1], _mainSafe), MainWidth, false);
            }
        }
    }
}
