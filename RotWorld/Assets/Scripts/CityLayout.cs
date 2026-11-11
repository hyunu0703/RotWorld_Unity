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
        // 도로 폭과 수변 및 출입 통로 제한
        private const int MainWidth = 6;
        private const int LocalWidth = MainWidth;
        private const int PavementWidth = 1;
        private const int BankBuffer = 3;
        private const int MaxEntranceLength = 32;

        // 하천 굴곡과 너비 설정
        private const float RiverBaseX = .14f;
        private const int RiverWaveFrequency = 12;
        private const float RiverWaveAmplitude = .035f;
        private const float MinimumRiverHalfWidth = 1.5f;
        private const float RiverHalfWidthRatio = .012f;

        // 지역 면적 보정과 개발 및 물자 기준
        private const int RegionAdjustmentPasses = 18;
        private const float MaxRegionDeviation = .025f;
        private const float DefaultDensity = 65f;
        private const float SupplyPercentageBase = 100f;

        // 도로 굴곡 적용 거리
        private const int MinimumBendDistanceSquared = 400;

        // 생성 입력과 지도 크기 보관
        private readonly CityMap mapData;
        private readonly CitySettings settingsData;
        private readonly int mapSize;

        // 지형 배치와 재고 난수 흐름 분리
        private readonly System.Random layoutRandom;
        private readonly System.Random stockRandom;

        // 생활권 중심과 도로 연결 대상 보관
        private readonly Vector2Int[] regionCenters = new Vector2Int[5];
        private readonly List<Vector2Int> roadNodes = new List<Vector2Int>();
        private readonly List<Vector2Int> bridgeBanks = new List<Vector2Int>();

        // 시드별 하천 굴곡과 계획 회전 보관
        private readonly double riverPhase;
        private readonly int planRotation;

        // 폭별 도로 후보와 시설 연결 준비 상태
        private bool[] mainRoadMask;
        private bool[] localRoadMask;
        private bool areRoadsReady;

        // 지도와 설정에서 독립 난수 흐름 준비
        internal CityLayout(CityMap map, CitySettings settings)
        {
            // 지도와 설정 보관 및 지형 배치와 재고용 난수 흐름 분리
            mapData = map;
            settingsData = settings;
            mapSize = map.Size;
            layoutRandom = new System.Random(unchecked(map.Seed ^ 0x73AE12));
            stockRandom = new System.Random(unchecked(map.Seed ^ 0x4A326F));
            riverPhase = ((uint)map.Seed % 997) / 997.0 * Math.PI * 2;
            planRotation = (int)((uint)map.Seed >> 4) & 3;
        }


        // 지형 계획에서 도로와 필지까지 순차 생성
        internal void Generate()
        {
            // 입력 설정에서 지형과 시설 생성 결과 구성
            CarveRiver();
            PlanCenters();
            AssignRegions();
            CreateBridges();
            ReserveLandmarks();

            // 필수 시설을 피하는 연결 도로 공간 확보
            mainRoadMask = BuildRoadMask(MainWidth);
            localRoadMask = BuildRoadMask(LocalWidth);
            KeepConnectedRoadSpace();
            BuildNetwork();
            BuildLocalStreets();

            // 기존 시설을 연결한 뒤 추가 건물과 최종 출입 통로 확보
            areRoadsReady = true;
            ConnectBuildings();
            PopulateBuildings();
            ConnectBuildings();
        }


        // 실제 좌표를 회전된 계획 좌표로 변환
        private Vector2 Plan(int x, int y)
        {
            // 지도 크기로 정규화한 좌표에 계획 회전 적용
            float px = x / (float)mapSize - .5f;
            float py = y / (float)mapSize - .5f;

            for (int i = 0; i < planRotation; i++)
            {
                (px, py) = (-py, px);
            }

            return new Vector2(px, py);
        }


        // 계획 좌표를 실제 타일로 변환
        private Vector2Int World(float x, float y)
        {
            // 계획 회전을 역으로 적용한 좌표를 타일 위치로 환산
            for (int i = 0; i < planRotation; i++)
            {
                (x, y) = (y, -x);
            }

            return new Vector2Int((int)((x + .5f) * mapSize), (int)((y + .5f) * mapSize));
        }


        // 계획 좌표의 하천 중심 계산
        private float RiverX(float y)
        {
            // 시드별 위상을 반영한 하천 굴곡 계산
            return RiverBaseX + (float)Math.Sin(y * RiverWaveFrequency + riverPhase) * RiverWaveAmplitude;
        }


        // 육지와 해안에 하천 및 하구 생성
        private void CarveRiver()
        {
            // 계획 좌표의 하천 폭에 포함된 육지만 강으로 교체
            float halfWidth = Math.Max(MinimumRiverHalfWidth, mapSize * RiverHalfWidthRatio);

            for (int y = 0; y < mapSize; y++)
            {
                for (int x = 0; x < mapSize; x++)
                {
                    int i = y * mapSize + x;
                    Vector2 p = Plan(x, y);

                    if (Math.Abs(p.x - RiverX(p.y)) * mapSize <= halfWidth &&
                        mapData.Terrain[i] != CityGenerator.Ocean &&
                        mapData.Terrain[i] != CityGenerator.Shallows)
                    {
                        mapData.Terrain[i] = mapData.Tiles[i] = CityGenerator.River;
                    }
                }
            }
        }


        // 여유 공간을 포함한 건설 가능 육지 판정
        private bool Inland(int x, int y, int margin = 0)
        {
            // 지도 경계와 여유 폭 안의 초지 여부 검증
            if (x - margin < 0 || y - margin < 0 || x + margin >= mapSize || y + margin >= mapSize)
            {
                return false;
            }

            for (int dy = -margin; dy <= margin; dy++)
            {
                for (int dx = -margin; dx <= margin; dx++)
                {
                    if (mapData.Terrain[(y + dy) * mapSize + x + dx] != CityGenerator.Grass)
                    {
                        return false;
                    }
                }
            }

            return true;
        }


        // 원하는 지점 주변의 안전한 생활권 중심 선택
        private Vector2Int FindCenter(Vector2Int target, int margin, List<Vector2Int> avoid = null)
        {
            // 요청 위치와 기존 중심 간 거리로 안전한 육지 후보 평가
            int best = -1;
            double score = double.MaxValue;

            for (int y = margin; y < mapSize - margin; y++)
            {
                for (int x = margin; x < mapSize - margin; x++)
                {
                    double distance = (x - target.x) * (x - target.x) + (y - target.y) * (y - target.y);

                    if (distance >= score || !Inland(x, y, margin))
                    {
                        continue;
                    }

                    bool separated = true;

                    if (avoid != null)
                    {
                        foreach (var other in avoid)
                        {
                            if ((other - new Vector2Int(x, y)).sqrMagnitude < mapSize * mapSize * .012f)
                            {
                                separated = false;
                            }
                        }
                    }

                    if (separated)
                    {
                        best = y * mapSize + x;
                        score = distance;
                    }
                }
            }

            if (best < 0)
            {
                throw new InvalidOperationException("생활권 배치에 필요한 육지 부족");
            }

            return new Vector2Int(best % mapSize, best / mapSize);
        }


        // 맵 크기에 맞는 도심과 독립 마을 중심 배치
        private void PlanCenters()
        {
            // 계획 좌표에서 지역별 중심과 맵 크기별 마을 후보 선정
            regionCenters[2] = FindCenter(World(-.05f, -.02f), 6);
            regionCenters[0] = FindCenter(World(-.10f, -.16f), 5);
            regionCenters[4] = FindCenter(World(.24f, -.09f), 8);
            regionCenters[3] = FindCenter(World(RiverX(.09f) - .055f, .09f), 4);
            int count = mapSize == 128 ? 2 : mapSize == 256 ? 3 : layoutRandom.Next(4, 6);

            for (int i = 0; i < count; i++)
            {
                double angle = .3 + i * Math.PI * 1.25 / Math.Max(1, count - 1) + layoutRandom.NextDouble() * .12;
                var target = World((float)Math.Cos(angle) * .24f, (float)Math.Sin(angle) * .24f);
                mapData.VillageCenters.Add(FindCenter(target, 4, mapData.VillageCenters));
            }

            regionCenters[1] = mapData.VillageCenters[0];
        }


        // 지역 거리와 하천 및 완만한 경계 변화에 따른 우선도 계산
        private double RegionScore(int index, CityRegion region)
        {
            // 계획 좌표의 경계 변화와 생활권 중심 거리 합산
            int x = index % mapSize;
            int y = index / mapSize;
            Vector2 p = Plan(x, y);
            double roughness = Math.Sin(p.x * 21 + riverPhase) * Math.Sin(p.y * 17) * .0007;

            if (region == CityRegion.Riverside)
            {
                return Math.Abs(p.x - RiverX(p.y) + .065f) * .08 + Math.Abs(p.y) * .002 + roughness;
            }

            double best = double.MaxValue;

            if (region == CityRegion.Villages)
            {
                foreach (var center in mapData.VillageCenters)
                {
                    best = Math.Min(best, (center - new Vector2Int(x, y)).sqrMagnitude / (double)(mapSize * mapSize));
                }
            }
            else
            {
                best = (regionCenters[(int)region] - new Vector2Int(x, y)).sqrMagnitude / (double)(mapSize * mapSize);
            }

            return best + roughness;
        }


        // 목표 면적 비율에 맞춘 지역 할당과 자연 공간 보존
        private void AssignRegions()
        {
            // 기본 면적 비율을 시드 난수로 보정한 뒤 지역별 육지 분배
            float[] weights =
            {
                .23f,
                .15f,
                .08f,
                .10f,
                .09f,
                .35f
            };
            Array.Copy(weights, mapData.RegionTargets, weights.Length);

            for (int i = 0; i < RegionAdjustmentPasses; i++)
            {
                int a = layoutRandom.Next(weights.Length);
                int b = layoutRandom.Next(weights.Length);
                float change = (float)(layoutRandom.NextDouble() * .02 - .01);

                if (Math.Abs(mapData.RegionTargets[a] + change - weights[a]) <= MaxRegionDeviation &&
                    Math.Abs(mapData.RegionTargets[b] - change - weights[b]) <= MaxRegionDeviation)
                {
                    mapData.RegionTargets[a] += change;
                    mapData.RegionTargets[b] -= change;
                }
            }

            // 해변을 포함한 육지 면적과 지역 할당 후보 수집
            int landCount = 0;
            var remaining = new List<int>();

            for (int i = 0; i < mapData.Terrain.Length; i++)
            {
                mapData.Regions[i] = CityRegion.Natural;

                if (mapData.Terrain[i] == CityGenerator.Grass)
                {
                    remaining.Add(i);
                    landCount++;
                }
                else if (mapData.Terrain[i] == CityGenerator.Sand)
                {
                    landCount++;
                }
            }

            // 지역 우선순위에 따른 처리 순서
            CityRegion[] order =
            {
                CityRegion.Downtown,
                CityRegion.Industrial,
                CityRegion.Villages,
                CityRegion.Riverside,
                CityRegion.Mixed
            };

            foreach (CityRegion region in order)
            {
                var scores = new double[mapData.Tiles.Length];

                foreach (int index in remaining)
                {
                    scores[index] = RegionScore(index, region);
                }

                remaining.Sort((a, b) =>
                {
                    int comparison = scores[a].CompareTo(scores[b]);
                    return comparison != 0 ? comparison : a.CompareTo(b);
                });
                int count = Math.Min(remaining.Count, (int)(landCount * mapData.RegionTargets[(int)region]));

                for (int i = 0; i < count; i++)
                {
                    mapData.Regions[remaining[i]] = region;
                }

                remaining.RemoveRange(0, count);
            }
        }


        // 지정 횡단 위치에서만 하천 교량과 육지 접근부 생성
        private void CreateBridges()
        {
            // 하천 횡단 후보에서 육지 접근부와 중복 여부 검증
            int target = mapSize == 128 ? 2 : mapSize == 256 ? layoutRandom.Next(2, 4) : layoutRandom.Next(3, 5);

            for (int b = 0; b < target; b++)
            {
                float desiredY = -.18f + b * .36f / (target - 1);
                bool placed = false;

                for (int attempt = 0; attempt < mapSize && !placed; attempt++)
                {
                    float py = desiredY + (attempt % 2 == 0 ? 1 : -1) * (attempt / 2) / (float)mapSize;

                    if (Math.Abs(py) > .28f)
                    {
                        continue;
                    }

                    float px = RiverX(py);
                    float half = Math.Max(MinimumRiverHalfWidth, mapSize * RiverHalfWidthRatio) + 7;
                    Vector2Int a = World(px - half / mapSize, py);
                    Vector2Int z = World(px + half / mapSize, py);
                    var bounds = a.y == z.y
                        ? new RectInt(Math.Min(a.x, z.x), a.y - 4, Math.Abs(a.x - z.x) + 1, 8)
                        : new RectInt(a.x - 4, Math.Min(a.y, z.y), 8, Math.Abs(a.y - z.y) + 1);
                    bool valid = Inland(a.x, a.y, 3) && Inland(z.x, z.y, 3);

                    foreach (var previous in mapData.Bridges)
                    {
                        if (previous.Overlaps(bounds))
                        {
                            valid = false;
                        }
                    }

                    for (int y = bounds.yMin; y < bounds.yMax && valid; y++)
                    {
                        for (int x = bounds.xMin; x < bounds.xMax; x++)
                        {
                            if (x < 0 ||
                                y < 0 ||
                                x >= mapSize ||
                                y >= mapSize ||
                                (mapData.Terrain[y * mapSize + x] != CityGenerator.Grass &&
                                mapData.Terrain[y * mapSize + x] != CityGenerator.River))
                            {
                                valid = false;
                                break;
                            }
                        }
                    }

                    if (!valid)
                    {
                        continue;
                    }

                    for (int y = bounds.yMin; y < bounds.yMax; y++)
                    {
                        for (int x = bounds.xMin; x < bounds.xMax; x++)
                        {
                            int index = y * mapSize + x;
                            mapData.Tiles[index] = mapData.Terrain[index] == CityGenerator.River ? CityGenerator.Bridge : CityGenerator.Pavement;
                        }
                    }

                    mapData.Bridges.Add(bounds);
                    bridgeBanks.Add(a);
                    bridgeBanks.Add(z);
                    placed = true;
                }

                if (!placed)
                {
                    throw new InvalidOperationException("안전한 교량 접근부 확보 실패");
                }
            }
        }


        // 지역 최소 시설과 독립 마을 부지를 도로보다 먼저 확보
        private void ReserveLandmarks()
        {
            // 지역별 필수 시설과 마을 주거 부지 우선 확보
            // 지역 우선순위에 따른 처리 순서
            CityRegion[] order =
            {
                CityRegion.Industrial,
                CityRegion.Downtown,
                CityRegion.Riverside,
                CityRegion.Mixed
            };

            foreach (var region in order)
            {
                ReserveBuilding(region, regionCenters[(int)region]);
            }

            foreach (var center in mapData.VillageCenters)
            {
                ReserveBuilding(CityRegion.Villages, center);
            }
        }


        // 지역의 최소 건물 규격으로 목표 중심 근처 필지 확보
        private void ReserveBuilding(CityRegion region, Vector2Int center)
        {
            // 중심과의 거리순 후보에서 지역 최소 규격 필지 탐색
            var size = BuildingSize(region, true);
            var candidates = RegionCandidates(region);
            candidates.Sort((a, b) =>
            {
                int da = (new Vector2Int(a % mapSize, a / mapSize) - center).sqrMagnitude;
                int db = (new Vector2Int(b % mapSize, b / mapSize) - center).sqrMagnitude;
                int compare = da.CompareTo(db);
                return compare != 0 ? compare : a.CompareTo(b);
            });
            int kind = region == CityRegion.Industrial ? 2 : region == CityRegion.Downtown ? 3 : region == CityRegion.Riverside ? 4 : 0;

            foreach (int index in candidates)
            {
                if (TryBuilding(new RectInt(index % mapSize - size.x / 2, index / mapSize - size.y / 2, size.x, size.y), region, kind, region == CityRegion.Downtown ? 5 : region == CityRegion.Villages
                    ? 10
                    : 15))
                {
                    return;
                }
            }

            throw new InvalidOperationException("지역 최소 시설 부지 확보 실패: " + region);
        }


        // 건물과 물을 피하는 도로 전체 폭 마스크 생성
        private bool[] BuildRoadMask(int width)
        {
            // 도로 전체 폭에 포함된 지형과 건물 점유 여부 검사
            var mask = new bool[mapData.Tiles.Length];
            int low = -width / 2;

            for (int y = width; y < mapSize - width; y++)
            {
                for (int x = width; x < mapSize - width; x++)
                {
                    bool valid = true;

                    for (int dy = low; dy < width + low && valid; dy++)
                    {
                        for (int dx = low; dx < width + low; dx++)
                        {
                            int i = (y + dy) * mapSize + x + dx;

                            if (mapData.Tiles[i] == CityGenerator.Building ||
                                (mapData.Terrain[i] != CityGenerator.Grass &&
                                mapData.Tiles[i] != CityGenerator.Bridge))
                            {
                                valid = false;
                                break;
                            }
                        }
                    }

                    mask[y * mapSize + x] = valid;
                }
            }

            return mask;
        }


        // 좁은 반도의 고립된 도로 후보를 제외하고 가장 큰 연결 공간 보존
        private void KeepConnectedRoadSpace()
        {
            // 폭 확보 후보를 연결 성분별로 순회해 최대 공간 선택
            var visited = new bool[localRoadMask.Length];
            var largest = new List<int>();
            var queue = new Queue<int>();
            int[] dx =
            {
                -1,
                1,
                0,
                0
            };
            int[] dy =
            {
                0,
                0,
                -1,
                1
            };

            for (int i = 0; i < localRoadMask.Length; i++)
            {
                if (!localRoadMask[i] || visited[i])
                {
                    continue;
                }

                var component = new List<int>();
                visited[i] = true;
                queue.Enqueue(i);

                while (queue.Count > 0)
                {
                    int index = queue.Dequeue();
                    component.Add(index);

                    for (int d = 0; d < dx.Length; d++)
                    {
                        int x = index % mapSize + dx[d];
                        int y = index / mapSize + dy[d];

                        if (x < 0 || y < 0 || x >= mapSize || y >= mapSize)
                        {
                            continue;
                        }

                        int next = y * mapSize + x;

                        if (localRoadMask[next] && !visited[next])
                        {
                            visited[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }

                if (component.Count > largest.Count)
                {
                    largest = component;
                }
            }

            // 최대 연결 공간 밖의 도로 후보 제외
            Array.Clear(visited, 0, visited.Length);

            foreach (int i in largest)
            {
                visited[i] = true;
            }

            for (int i = 0; i < visited.Length; i++)
            {
                localRoadMask[i] &= visited[i];
                mainRoadMask[i] &= visited[i];
            }
        }


        // 원하는 지점에 가장 가까운 도로 폭 확보 위치 선택
        private Vector2Int RoadPoint(Vector2Int target, bool[] mask)
        {
            // 도로 마스크의 유효 후보에서 목표와 가장 가까운 위치 탐색
            int best = -1;
            int distance = int.MaxValue;

            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i])
                {
                    continue;
                }

                int dx = i % mapSize - target.x;
                int dy = i / mapSize - target.y;
                int d = dx * dx + dy * dy;

                if (d < distance)
                {
                    distance = d;
                    best = i;
                }
            }

            if (best < 0)
            {
                throw new InvalidOperationException("도로 폭 확보 실패");
            }

            return new Vector2Int(best % mapSize, best / mapSize);
        }


        // 지역별 도로 간격 범위에서 블록 내부 폭 선택
        private int BlockSpacing(CityRegion region)
        {
            // 지역 용도에 대응하는 도로 간격 난수 선택
            switch (region)
            {
                case CityRegion.Downtown:
                    return layoutRandom.Next(12, 21);
                case CityRegion.Mixed:
                    return layoutRandom.Next(20, 33);
                case CityRegion.Villages:
                    return layoutRandom.Next(16, 29);
                case CityRegion.Riverside:
                    return layoutRandom.Next(18, 31);
                default:
                    return layoutRandom.Next(28, 45);
            }
        }


        // 작은 격자와 순환로 및 가지형 도로를 지역마다 조합
        private void BuildLocalStreets()
        {
            // 생활권별 중심과 도로 마스크를 이용한 지역 도로 구성
            for (int region = 0; region < 5; region++)
            {
                if (region == (int)CityRegion.Villages)
                {
                    continue;
                }

                var center = regionCenters[region];
                int spacing = BlockSpacing((CityRegion)region);
                float radius = mapSize == 128 ? spacing * .4f : spacing * .75f;
                var hub = RoadPoint(center, localRoadMask);
                Route(hub, roadNodes[region == 2 ? 0 : region == 0 ? 1 : region == 4 ? 2 : 3], LocalWidth, false);

                if (region == (int)CityRegion.Downtown)
                {
                    double angle = layoutRandom.NextDouble() * .6 - .3;
                    int halfLines = mapSize == 128 ? 0 : 1;

                    for (int line = -halfLines; line <= halfLines; line++)
                    {
                        float shift = line * (spacing + LocalWidth);
                        Vector2Int a = new Vector2Int(center.x + (int)(-radius * Math.Cos(angle) - shift * Math.Sin(angle)),
                            center.y + (int)(-radius * Math.Sin(angle) + shift * Math.Cos(angle)));
                        Vector2Int b = new Vector2Int(center.x + (int)(radius * Math.Cos(angle) - shift * Math.Sin(angle)),
                            center.y + (int)(radius * Math.Sin(angle) + shift * Math.Cos(angle)));
                        var start = RoadPoint(a, localRoadMask);
                        Route(hub, start, LocalWidth, false);
                        Route(start, RoadPoint(b, localRoadMask), LocalWidth, false);
                    }
                }
                else if (region == (int)CityRegion.Riverside)
                {
                    var p = Plan(center.x, center.y);

                    for (int i = -2; i <= 2; i++)
                    {
                        float y = p.y + i * spacing / (float)mapSize * .5f;
                        var next = RoadPoint(World(RiverX(y) - .055f, y), localRoadMask);
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
                        var point = RoadPoint(new Vector2Int(center.x + (int)(Math.Cos(angle) * radius), center.y + (int)(Math.Sin(angle) * radius * .75)), localRoadMask);

                        if (i == 0)
                        {
                            first = point;
                        }

                        Route(previous, point, LocalWidth, region != (int)CityRegion.Industrial);
                        previous = point;
                    }

                    Route(previous, first, LocalWidth, true);
                }
            }

            foreach (var center in mapData.VillageCenters)
            {
                var hub = RoadPoint(center, localRoadMask);
                Route(RoadPoint(center, mainRoadMask), hub, LocalWidth, false);
                int spacing = BlockSpacing(CityRegion.Villages);

                for (int branch = 0; branch < 3; branch++)
                {
                    double angle = branch * Math.PI * 2 / 3 + layoutRandom.NextDouble() * .6;
                    float length = mapSize == 128 ? spacing * .35f : spacing * .6f;
                    var end = RoadPoint(new Vector2Int(center.x + (int)(Math.Cos(angle) * length), center.y + (int)(Math.Sin(angle) * length)), localRoadMask);
                    Route(hub, end, LocalWidth, true);
                }
            }
        }


        // 완만한 중간 지점 또는 직접 연결로 도로 경로 생성
        private bool Route(Vector2Int from, Vector2Int to, int width, bool bend)
        {
            // 시작점과 도착점 및 굴곡 조건에 따른 도로 경로 선택
            if (from == to)
            {
                PaintRoad(from, width);
                return true;
            }

            if (bend && (from - to).sqrMagnitude > MinimumBendDistanceSquared)
            {
                var middle = new Vector2Int((from.x + to.x) / 2 + layoutRandom.Next(-8, 9), (from.y + to.y) / 2 + layoutRandom.Next(-8, 9));
                middle = RoadPoint(middle, width == MainWidth ? mainRoadMask : localRoadMask);
                var first = FindRoute(from, middle, width);
                var second = FindRoute(middle, to, width);

                if (first != null && second != null)
                {
                    DrawRoute(first, width);
                    DrawRoute(second, width);
                    return true;
                }
            }

            // 굴곡 경로 미확보 시 직접 연결 경로로 대체
            var path = FindRoute(from, to, width);

            if (path == null)
            {
                return false;
            }

            DrawRoute(path, width);
            return true;
        }


        // 도로 전체 폭을 보장하는 마스크에서 A별 경로 탐색
        private List<Vector2Int> FindRoute(Vector2Int from, Vector2Int to, int width)
        {
            // 도로 폭 마스크와 이동 비용을 이용한 경로 탐색 준비
            var mask = width == MainWidth ? mainRoadMask : localRoadMask;
            int start = from.y * mapSize + from.x;
            int goal = to.y * mapSize + to.x;
            var distance = new int[mapData.Tiles.Length];
            var parents = new int[distance.Length];
            Array.Fill(distance, int.MaxValue);
            Array.Fill(parents, -1);
            var open = new SortedSet<(int Cost, int Index)>();
            distance[start] = 0;
            open.Add((0, start));
            int[] dx =
            {
                -1,
                1,
                0,
                0,
                -1,
                1,
                -1,
                1
            };
            int[] dy =
            {
                0,
                0,
                -1,
                1,
                -1,
                -1,
                1,
                1
            };

            while (open.Count > 0)
            {
                var item = open.Min;
                open.Remove(item);
                int index = item.Index;

                // 도착점에서 부모 경로를 역추적해 시작점 순서로 반환
                if (index == goal)
                {
                    var path = new List<Vector2Int>();

                    for (int p = goal; p != start; p = parents[p])
                    {
                        path.Add(new Vector2Int(p % mapSize, p / mapSize));
                    }

                    path.Add(from);
                    path.Reverse();
                    return path;
                }

                int x = index % mapSize;
                int y = index / mapSize;

                for (int d = 0; d < dx.Length; d++)
                {
                    int nx = x + dx[d];
                    int ny = y + dy[d];

                    if (nx < 1 || ny < 1 || nx >= mapSize - 1 || ny >= mapSize - 1)
                    {
                        continue;
                    }

                    int next = ny * mapSize + nx;

                    if (!mask[next] || (d >= 4 && (!mask[y * mapSize + nx] || !mask[ny * mapSize + x])))
                    {
                        continue;
                    }

                    Vector2 p = Plan(nx, ny);
                    int noise = (int)((Math.Sin(p.x * 27 + riverPhase) * Math.Cos(p.y * 23) + 1) * 3);
                    int cost = distance[index] + (d >= 4 ? 14 : 10) + noise;

                    if (cost >= distance[next])
                    {
                        continue;
                    }

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
            // 지도에 경로를 보관한 뒤 경로별 도로 폭 반영
            mapData.RoadRoutes.Add(path);

            foreach (var point in path)
            {
                PaintRoad(point, width);
            }
        }


        // 육지와 승인된 교량에만 도로 폭 기록
        private void PaintRoad(Vector2Int point, int width)
        {
            // 타일 중심 거리로 방향에 무관한 차도와 인도 반경 계산
            float radius = width * .5f;
            float roadRadius = radius - PavementWidth;
            float centerOffset = (width % 2 == 0) ? .5f : 0f;
            int low = -width / 2;
            int high = width + low;

            for (int dy = low; dy < high; dy++)
            {
                for (int dx = low; dx < high; dx++)
                {
                    float offsetX = dx + centerOffset;
                    float offsetY = dy + centerOffset;
                    float distanceSquared = offsetX * offsetX + offsetY * offsetY;

                    if (distanceSquared > radius * radius)
                    {
                        continue;
                    }

                    // 기존 지형과 교량 보존 후 원형 범위의 육지에만 타일 적용
                    int i = (point.y + dy) * mapSize + point.x + dx;

                    if (mapData.Terrain[i] == CityGenerator.River && mapData.Tiles[i] == CityGenerator.Bridge)
                    {
                        continue;
                    }

                    if (mapData.Terrain[i] != CityGenerator.Grass || mapData.Tiles[i] == CityGenerator.Building)
                    {
                        continue;
                    }

                    bool isPavement = distanceSquared > roadRadius * roadRadius;

                    if (mapData.Tiles[i] != CityGenerator.Road)
                    {
                        mapData.Tiles[i] = isPavement ? CityGenerator.Pavement : CityGenerator.Road;
                    }
                }
            }
        }


        // 지역 중심에서 가까운 필지 후보의 고정 순서 생성
        private List<int> RegionCandidates(CityRegion region)
        {
            // 지역과 초지 조건에 맞는 후보를 지역 우선도로 정렬
            var candidates = new List<int>();

            for (int i = 0; i < mapData.Tiles.Length; i++)
            {
                if (mapData.Regions[i] == region && mapData.Terrain[i] == CityGenerator.Grass)
                {
                    candidates.Add(i);
                }
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
            // 지역별 최소 규격 또는 난수 규격 선택
            switch (region)
            {
                case CityRegion.Downtown:
                    return new Vector2Int(minimum ? 5 : layoutRandom.Next(5, 11), minimum ? 6 : layoutRandom.Next(6, 13));
                case CityRegion.Villages:
                    return new Vector2Int(minimum ? 4 : layoutRandom.Next(4, 7), minimum ? 5 : layoutRandom.Next(5, 8));
                case CityRegion.Riverside:
                    return new Vector2Int(minimum ? 4 : layoutRandom.Next(4, 9), minimum ? 6 : layoutRandom.Next(6, 11));
                case CityRegion.Industrial:
                    return new Vector2Int(minimum ? 10 : layoutRandom.Next(10, 19), minimum ? 12 : layoutRandom.Next(12, 25));
                default:
                    return new Vector2Int(minimum ? 4 : layoutRandom.Next(4, 11), minimum ? 5 : layoutRandom.Next(5, 11));
            }
        }


        // 필지 비율을 밀도 설정으로 보정한 지역별 건물 배치
        private void PopulateBuildings()
        {
            // 지역 면적과 개발 밀도로 목표 건물 수와 후보 순서 결정
            float[] fill =
            {
                .675f,
                .425f,
                .90f,
                .55f,
                .475f
            };
            int[] lotAreas =
            {
                110,
                160,
                85,
                140,
                400
            };
            // 지역 우선순위에 따른 처리 순서
            CityRegion[] order =
            {
                CityRegion.Downtown,
                CityRegion.Villages,
                CityRegion.Riverside,
                CityRegion.Mixed,
                CityRegion.Industrial
            };

            foreach (var region in order)
            {
                var candidates = RegionCandidates(region);
                int target = Math.Max(region == CityRegion.Downtown ? 3 : 1,
                    (int)(candidates.Count / (float)lotAreas[(int)region] * fill[(int)region] * settingsData.Density / DefaultDensity));
                int count = CountBuildings(region);
                int step = Math.Max(1, candidates.Count / Math.Max(1, target * 60));

                for (int pass = 0; pass < 2 && count < target; pass++)
                {
                    for (int i = 0; i < candidates.Count && count < target; i += step)
                    {
                        int index = candidates[(i + pass * 17) % candidates.Count];
                        var size = BuildingSize(region, pass == 1);
                        var bounds = new RectInt(index % mapSize - size.x / 2, index / mapSize - size.y / 2, size.x, size.y);
                        int gap = region == CityRegion.Downtown ? 5 : region == CityRegion.Villages ? 7 : 10;
                        int kind = region == CityRegion.Industrial ? 2 : region == CityRegion.Downtown ? 3
                            : region == CityRegion.Riverside ? 4 : region == CityRegion.Villages ? 0 : layoutRandom.Next(2);

                        if (TryBuilding(bounds, region, kind, gap))
                        {
                            count++;
                        }
                    }
                }

                if (count == 0)
                {
                    throw new InvalidOperationException("지역 최소 건물 배치 실패: " + region);
                }
            }

            foreach (var village in mapData.VillageCenters)
            {
                bool populated = false;

                foreach (var building in mapData.Buildings)
                {
                    if (building.Kind == 0 && ((Vector2)village - building.Bounds.center).sqrMagnitude < mapSize * mapSize * .01f)
                    {
                        populated = true;
                    }
                }

                for (int r = 0; r < mapSize / 8 && !populated; r++)
                {
                    for (int y = village.y - r; y <= village.y + r && !populated; y++)
                    {
                        for (int x = village.x - r; x <= village.x + r && !populated; x++)
                        {
                            if (x < 1 || y < 1 || x >= mapSize - 5 || y >= mapSize - 6 || mapData.Regions[y * mapSize + x] != CityRegion.Villages)
                            {
                                continue;
                            }

                            populated = TryBuilding(new RectInt(x, y, 4, 5), CityRegion.Villages, 0, 10);
                        }
                    }
                }

                if (!populated)
                {
                    throw new InvalidOperationException("마을 주거 부지 부족");
                }
            }
        }


        // 지역별 실제 건물 수 조회
        private int CountBuildings(CityRegion region)
        {
            // 건물 중심이 속한 지역을 기준으로 기존 건물 집계
            int count = 0;

            foreach (var building in mapData.Buildings)
            {
                if (mapData.Regions[(int)building.Bounds.center.y * mapSize + (int)building.Bounds.center.x] == region)
                {
                    count++;
                }
            }

            return count;
        }


        // 건물 바닥과 간격 및 수변 여유 공간 검사 후 배치
        private bool TryBuilding(RectInt bounds, CityRegion region, int kind, int gap)
        {
            // 필지 중심과 경계 및 기존 건물과의 간격 검증
            int cx = (int)bounds.center.x;
            int cy = (int)bounds.center.y;

            if (bounds.xMin < gap + 1 ||
                bounds.yMin < gap + 1 ||
                bounds.xMax >= mapSize - gap - 1 ||
                bounds.yMax >= mapSize - gap - 1 ||
                mapData.Regions[cy * mapSize + cx] != region)
            {
                return false;
            }

            for (int y = bounds.yMin - gap; y < bounds.yMax + gap; y++)
            {
                for (int x = bounds.xMin - gap; x < bounds.xMax + gap; x++)
                {
                    int i = y * mapSize + x;

                    if (mapData.Tiles[i] == CityGenerator.Building)
                    {
                        return false;
                    }

                    if (bounds.Contains(new Vector2Int(x, y)) && (mapData.Tiles[i] != CityGenerator.Grass || !Inland(x, y, 1)))
                    {
                        return false;
                    }
                }
            }

            for (int y = bounds.yMin - BankBuffer; y < bounds.yMax + BankBuffer; y++)
            {
                for (int x = bounds.xMin - BankBuffer; x < bounds.xMax + BankBuffer; x++)
                {
                    if (x >= 0 && y >= 0 && x < mapSize && y < mapSize && mapData.Terrain[y * mapSize + x] == CityGenerator.River)
                    {
                        return false;
                    }
                }
            }

            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    mapData.Tiles[y * mapSize + x] = CityGenerator.Building;
                }
            }

            var door = new Vector2Int(cx, bounds.yMin - 1);

            // 도로 접근 실패 시 임시 건물 타일을 초지로 복구
            if (areRoadsReady && !FindEntrance(bounds, out door))
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    for (int x = bounds.xMin; x < bounds.xMax; x++)
                    {
                        mapData.Tiles[y * mapSize + x] = CityGenerator.Grass;
                    }
                }

                return false;
            }

            // 재고 전용 난수와 물자 설정에서 건물별 아이템 수량 생성
            var stock = new int[5];

            for (int i = 0; i < stock.Length; i++)
            {
                stock[i] = (int)(stockRandom.Next(0, i == 4 ? 3 : 9) * settingsData.Supplies / SupplyPercentageBase);
            }

            mapData.Buildings.Add(new CityBuilding(bounds, door, kind, stock));
            return true;
        }


        // 건물마다 바다와 강을 피하는 출입 통로 확보
        private void ConnectBuildings()
        {
            // 기존 건물마다 도로 접근 경로 확보 후 출입구 교체
            for (int b = 0; b < mapData.Buildings.Count; b++)
            {
                var building = mapData.Buildings[b];
                var bounds = building.Bounds;

                if (!FindEntrance(bounds, out var door))
                {
                    throw new InvalidOperationException("건물 출입 통로 확보 실패");
                }

                if (door != building.Door)
                {
                    mapData.Buildings[b] = new CityBuilding(bounds, door, building.Kind, building.Stock);
                }
            }
        }


        // 건물 네 면에서 연결 가능한 출입구 선택
        private bool FindEntrance(RectInt bounds, out Vector2Int door)
        {
            // 건물 네 면의 출입구 후보를 정해진 순서로 평가
            var doors = new[]
            {
                new Vector2Int((int)bounds.center.x, bounds.yMin - 1),
                new Vector2Int((int)bounds.center.x, bounds.yMax),
                new Vector2Int(bounds.xMin - 1, (int)bounds.center.y),
                new Vector2Int(bounds.xMax, (int)bounds.center.y)
            };

            foreach (var candidate in doors)
            {
                if (Entrance(candidate))
                {
                    door = candidate;
                    return true;
                }
            }

            door = default;
            return false;
        }


        // 출입구에서 제한 거리 안의 도로까지 보행 경로 탐색
        private bool Entrance(Vector2Int door)
        {
            // 출입구 지형 확인 후 거리 제한 보행 탐색 준비
            int start = door.y * mapSize + door.x;

            if (mapData.Tiles[start] == CityGenerator.Building || !Inland(door.x, door.y))
            {
                return false;
            }

            var parents = new Dictionary<int, int>();
            var queue = new Queue<(int Index, int Depth)>();
            queue.Enqueue((start, 0));
            parents[start] = start;
            int[] offsets =
            {
                -1,
                1,
                -mapSize,
                mapSize
            };

            while (queue.Count > 0)
            {
                var item = queue.Dequeue();
                int index = item.Index;
                byte tile = mapData.Tiles[index];

                // 도로 연결 성공 시 부모 경로의 초지만 보행 통로로 변경
                if (tile == CityGenerator.Road || tile == CityGenerator.Pavement || tile == CityGenerator.Path)
                {
                    for (int p = index; p != start; p = parents[p])
                    {
                        if (mapData.Tiles[p] == CityGenerator.Grass)
                        {
                            mapData.Tiles[p] = CityGenerator.Path;
                        }
                    }

                    if (mapData.Tiles[start] == CityGenerator.Grass)
                    {
                        mapData.Tiles[start] = CityGenerator.Path;
                    }

                    return true;
                }

                // 통로 길이 제한을 넘는 추가 탐색 차단
                if (item.Depth >= MaxEntranceLength)
                {
                    continue;
                }

                foreach (int offset in offsets)
                {
                    int next = index + offset;

                    if (next < 0 ||
                        next >= mapData.Tiles.Length ||
                        Math.Abs(next % mapSize - index % mapSize) > 1 ||
                        parents.ContainsKey(next) ||
                        mapData.Terrain[next] != CityGenerator.Grass ||
                        mapData.Tiles[next] == CityGenerator.Building)
                    {
                        continue;
                    }

                    parents[next] = index;
                    queue.Enqueue((next, item.Depth + 1));
                }
            }

            return false;
        }


        // 생활권과 다리를 최소 연결망 및 대체 경로로 연결
        private void BuildNetwork()
        {
            // 생활권 중심과 교량 접근부를 도로 연결 대상에 등록
            roadNodes.Add(RoadPoint(regionCenters[2], mainRoadMask));
            roadNodes.Add(RoadPoint(regionCenters[0], mainRoadMask));
            roadNodes.Add(RoadPoint(regionCenters[4], mainRoadMask));
            roadNodes.Add(RoadPoint(regionCenters[3], mainRoadMask));

            foreach (var center in mapData.VillageCenters)
            {
                roadNodes.Add(RoadPoint(center, mainRoadMask));
            }

            foreach (var bank in bridgeBanks)
            {
                roadNodes.Add(RoadPoint(bank, mainRoadMask));
            }

            // 대형 이상 맵의 보조 중심을 도로 연결 대상에 포함
            if (mapSize >= 512)
            {
                roadNodes.Add(RoadPoint(World(-.18f, .04f), mainRoadMask));
            }

            for (int i = 1; i < roadNodes.Count; i++)
            {
                int nearest = 0;

                for (int j = 1; j < i; j++)
                {
                    if ((roadNodes[i] - roadNodes[j]).sqrMagnitude < (roadNodes[i] - roadNodes[nearest]).sqrMagnitude)
                    {
                        nearest = j;
                    }
                }

                if (!Route(roadNodes[nearest], roadNodes[i], MainWidth, true) && !Route(roadNodes[nearest], roadNodes[i], LocalWidth, true))
                {
                    throw new InvalidOperationException($"생활권 간 도로 연결 실패: {mapSize}/{mapData.Seed}, {roadNodes[nearest].x},{roadNodes[nearest].y} → {roadNodes[i].x},{roadNodes[i].y}");
                }
            }

            for (int i = 0; i < 4; i++)
            {
                Route(roadNodes[i], roadNodes[(i + 2) % 4], MainWidth, true);
            }

            for (int i = 0; i < bridgeBanks.Count; i += 2)
            {
                Route(RoadPoint(bridgeBanks[i], mainRoadMask), RoadPoint(bridgeBanks[i + 1], mainRoadMask), MainWidth, false);
            }
        }
    }
}
